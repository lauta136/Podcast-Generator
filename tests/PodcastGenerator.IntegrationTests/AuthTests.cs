using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using PodcastGenerator.Infrastructure.Persistence;

namespace PodcastGenerator.IntegrationTests;

public class AuthTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Key = "integration-tests-signing-key-0123456789abcdef";

    [Theory]
    [InlineData("/api/episodes")]
    [InlineData("/api/preferences")]
    [InlineData("/api/voices")]
    [InlineData("/api/tts/budget")]
    [InlineData("/api/auth/me")]
    [InlineData("/api/episodes/00000000-0000-0000-0000-000000000009/audio")]
    public async Task Everything_but_sign_in_and_health_needs_a_signed_in_user(string url)
    {
        var anonymous = factory.CreateAnonymousClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(new Uri(url, UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync(new Uri("/health", UriKind.Relative))).StatusCode);
    }

    /// <summary>
    /// One story, because the steps depend on each other: the first account takes over the local user's episodes,
    /// a second account is separate and can't see them, and sign-out/sign-in work through the cookie alone.
    /// </summary>
    [Fact]
    public async Task Accounts_sign_in_with_a_cookie_and_only_see_their_own_episodes()
    {
        var episodeId = await EpisodeApi.CreateAsync(factory.CreateClient(), "Thema Schwarzwald");
        await EpisodeApi.WaitForAsync(factory.CreateClient(), episodeId, "Ready");

        // First account: claims the local user, so the episode made before sign-in existed is kept.
        var anna = factory.CreateAnonymousClient();
        var registered = await anna.PostAsJsonAsync("/api/auth/register", new { email = "  Anna@Example.com ", password = "richtig langes passwort" });
        Assert.Equal(HttpStatusCode.OK, registered.StatusCode);
        var cookie = Assert.Single(registered.Headers.GetValues("Set-Cookie"));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api", cookie, StringComparison.OrdinalIgnoreCase);
        var annaUser = await registered.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(SeedData.LocalUserId, annaUser.GetProperty("id").GetGuid());
        Assert.Equal("anna@example.com", annaUser.GetProperty("email").GetString());
        Assert.DoesNotContain("password", await registered.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        Assert.Equal("anna@example.com", (await GetAsync(anna, "/api/auth/me")).GetProperty("email").GetString());
        Assert.Equal(1, (await GetAsync(anna, "/api/episodes")).GetProperty("total").GetInt32());
        Assert.Equal(HttpStatusCode.OK, (await anna.GetAsync(new Uri($"/api/episodes/{episodeId}/audio", UriKind.Relative))).StatusCode);

        // Second account: its own user, an empty library, and no access to Anna's episode by id.
        var ben = factory.CreateAnonymousClient();
        var benUser = await (await ben.PostAsJsonAsync("/api/auth/register", new { email = "ben@example.com", password = "noch ein passwort" }))
            .Content.ReadFromJsonAsync<JsonElement>();
        Assert.NotEqual(SeedData.LocalUserId, benUser.GetProperty("id").GetGuid());
        Assert.Equal(0, (await GetAsync(ben, "/api/episodes")).GetProperty("total").GetInt32());
        foreach (var url in new[] { $"/api/episodes/{episodeId}", $"/api/episodes/{episodeId}/audio", $"/api/episodes/{episodeId}/transcript" })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await ben.GetAsync(new Uri(url, UriKind.Relative))).StatusCode);
        }

        Assert.Equal(HttpStatusCode.NotFound, (await ben.DeleteAsync(new Uri($"/api/episodes/{episodeId}", UriKind.Relative))).StatusCode);
        Assert.Equal("de", (await GetAsync(ben, "/api/preferences")).GetProperty("targetLanguage").GetString()); // own defaults

        // The same email again, in any spelling, is refused.
        var duplicate = await factory.CreateAnonymousClient().PostAsJsonAsync("/api/auth/register", new { email = "ANNA@example.com", password = "ein anderes passwort" });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        // Sign out, then back in.
        Assert.Equal(HttpStatusCode.NoContent, (await anna.PostAsync(new Uri("/api/auth/logout", UriKind.Relative), null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anna.GetAsync(new Uri("/api/auth/me", UriKind.Relative))).StatusCode);

        var wrongPassword = await anna.PostAsJsonAsync("/api/auth/login", new { email = "anna@example.com", password = "falsches passwort" });
        var unknownEmail = await anna.PostAsJsonAsync("/api/auth/login", new { email = "niemand@example.com", password = "falsches passwort" });
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownEmail.StatusCode);
        Assert.Equal( // the answer doesn't reveal which accounts exist
            (await wrongPassword.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString(),
            (await unknownEmail.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString());

        var login = await anna.PostAsJsonAsync("/api/auth/login", new { email = "Anna@example.com", password = "richtig langes passwort" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Equal(1, (await GetAsync(anna, "/api/episodes")).GetProperty("total").GetInt32());
    }

    [Theory]
    [InlineData("kein-at-zeichen", "langes passwort hier", "Email")]
    [InlineData("a@b@c.de", "langes passwort hier", "Email")]
    [InlineData("gut@example.com", "kurz", "Password")]
    public async Task Registration_validates_email_and_password(string email, string password, string field)
    {
        var response = await factory.CreateAnonymousClient().PostAsJsonAsync("/api/auth/register", new { email, password });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").TryGetProperty(field, out _));
    }

    [Theory]
    [InlineData(Key, "podcast-generator", -1)] // expired
    [InlineData("another-signing-key-another-signing-key-0000", "podcast-generator", 60)] // forged with a different key
    [InlineData(Key, "someone-else", 60)] // wrong issuer
    public async Task Expired_forged_or_foreign_tokens_are_rejected(string key, string issuer, int expiresInMinutes)
    {
        var now = DateTime.UtcNow;
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = "podcast-generator-web",
            Subject = new ClaimsIdentity([new Claim("sub", SeedData.LocalUserId.ToString())]),
            NotBefore = now.AddMinutes(-120),
            Expires = now.AddMinutes(expiresInMinutes),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256),
        });
        var client = factory.CreateAnonymousClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(new Uri("/api/episodes", UriKind.Relative))).StatusCode);
    }

    private static async Task<JsonElement> GetAsync(HttpClient client, string url) =>
        await client.GetFromJsonAsync<JsonElement>(url, JsonDefaults.Web);
}

public class LoginRateLimitTests(StrictLoginLimitApiFactory factory) : IClassFixture<StrictLoginLimitApiFactory>
{
    [Fact]
    public async Task Too_many_sign_in_attempts_are_refused_for_a_while()
    {
        var client = factory.CreateAnonymousClient();
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
        {
            statuses.Add((await client.PostAsJsonAsync("/api/auth/login", new { email = "x@example.com", password = "geraten" + i })).StatusCode);
        }

        Assert.Equal([HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests], statuses);
    }
}
