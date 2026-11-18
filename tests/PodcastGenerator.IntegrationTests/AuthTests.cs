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
        var cookies = registered.Headers.GetValues("Set-Cookie").ToList();
        var access = Assert.Single(cookies, c => c.StartsWith("pg_auth=", StringComparison.Ordinal));
        var refresh = Assert.Single(cookies, c => c.StartsWith("pg_refresh=", StringComparison.Ordinal));
        Assert.All(cookies, c =>
        {
            Assert.Contains("httponly", c, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("samesite=strict", c, StringComparison.OrdinalIgnoreCase);
        });
        Assert.Contains("path=/api;", access + ";", StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api/auth", refresh, StringComparison.OrdinalIgnoreCase); // the long-lived token only goes to /api/auth
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

public class BehindProxyTests(BehindProxyApiFactory factory) : IClassFixture<BehindProxyApiFactory>
{
    [Fact]
    public async Task The_sign_in_limit_counts_each_client_behind_the_proxy_separately()
    {
        async Task<HttpStatusCode> Attempt(string clientAddress, int i)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
            {
                Content = JsonContent.Create(new { email = "x@example.com", password = "geraten" + i }),
            };
            request.Headers.Add("X-Forwarded-For", clientAddress);
            return (await factory.CreateAnonymousClient().SendAsync(request)).StatusCode;
        }

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, await Attempt("203.0.113.7", i));
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, await Attempt("203.0.113.7", 3));
        Assert.Equal(HttpStatusCode.Unauthorized, await Attempt("198.51.100.20", 0)); // someone else isn't locked out

        // A client can't dodge the limit by adding its own address in front: only the proxy's entry counts.
        Assert.Equal(HttpStatusCode.TooManyRequests, await Attempt("10.9.9.9, 203.0.113.7", 4));
    }

    [Fact]
    public async Task Cookies_are_secure_when_the_proxy_received_https()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/register")
        {
            Content = JsonContent.Create(new { email = "proxy@example.com", password = "ein langes passwort" }),
        };
        request.Headers.Add("X-Forwarded-Proto", "https");
        request.Headers.Add("X-Forwarded-For", "192.0.2.44");

        var response = await factory.CreateAnonymousClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.All(response.Headers.GetValues("Set-Cookie"), c => Assert.Contains("secure", c, StringComparison.OrdinalIgnoreCase));
    }
}

public class InviteOnlyTests(InviteOnlyApiFactory factory) : IClassFixture<InviteOnlyApiFactory>
{
    [Fact]
    public async Task Sign_up_needs_the_invite_code_and_the_form_can_ask_for_it()
    {
        var client = factory.CreateAnonymousClient();

        var registration = await client.GetFromJsonAsync<JsonElement>("/api/auth/registration");
        Assert.True(registration.GetProperty("inviteRequired").GetBoolean());
        Assert.DoesNotContain(InviteOnlyApiFactory.Code, registration.GetRawText(), StringComparison.Ordinal);

        var refused = await client.PostAsJsonAsync("/api/auth/register", new { email = "gast@example.com", password = "ein langes passwort" });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("inviteCode", await refused.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Set-Cookie", refused.Headers.Select(h => h.Key));

        var wrong = await client.PostAsJsonAsync("/api/auth/register", new { email = "gast@example.com", password = "ein langes passwort", inviteCode = "geraten" });
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);

        var invited = await client.PostAsJsonAsync("/api/auth/register", new { email = "gast@example.com", password = "ein langes passwort", inviteCode = InviteOnlyApiFactory.Code });
        Assert.Equal(HttpStatusCode.OK, invited.StatusCode);
        Assert.Equal("gast@example.com", (await client.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("email").GetString());
    }

}

public class OpenSignUpTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Without_a_code_configured_the_form_doesnt_ask_for_one()
    {
        var registration = await factory.CreateAnonymousClient().GetFromJsonAsync<JsonElement>("/api/auth/registration");
        Assert.False(registration.GetProperty("inviteRequired").GetBoolean());
    }
}
