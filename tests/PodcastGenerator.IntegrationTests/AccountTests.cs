using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PodcastGenerator.Application.Audio;

namespace PodcastGenerator.IntegrationTests;

/// <summary>Own factory: these tests change and delete accounts.</summary>
public class AccountTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Changing_the_password_signs_out_other_devices_but_keeps_this_one()
    {
        await ClaimLocalUserAsync();
        var laptop = await RegisterAsync("lena@example.com", "das alte passwort");
        var phone = factory.CreateAnonymousClient();
        (await phone.PostAsJsonAsync("/api/auth/login", new { email = "lena@example.com", password = "das alte passwort" })).EnsureSuccessStatusCode();

        var wrong = await laptop.PutAsJsonAsync("/api/auth/password", new { currentPassword = "falsch", newPassword = "das neue passwort" });
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode); // 400, not 401: the user stays signed in
        Assert.True((await wrong.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").TryGetProperty("CurrentPassword", out _));

        var changed = await laptop.PutAsJsonAsync("/api/auth/password", new { currentPassword = "das alte passwort", newPassword = "das neue passwort" });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await laptop.GetAsync(new Uri("/api/auth/me", UriKind.Relative))).StatusCode); // fresh cookie
        Assert.Equal(HttpStatusCode.Unauthorized, (await phone.GetAsync(new Uri("/api/episodes", UriKind.Relative))).StatusCode); // signed out

        var oldLogin = await factory.CreateAnonymousClient().PostAsJsonAsync("/api/auth/login", new { email = "lena@example.com", password = "das alte passwort" });
        var newLogin = await factory.CreateAnonymousClient().PostAsJsonAsync("/api/auth/login", new { email = "lena@example.com", password = "das neue passwort" });
        Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);
        Assert.Equal(HttpStatusCode.OK, newLogin.StatusCode);
    }

    [Fact]
    public async Task Deleting_the_account_removes_its_data_and_audio_and_nobody_elses()
    {
        await ClaimLocalUserAsync();
        var owner = await RegisterAsync("max@example.com", "ein langes passwort");
        var other = await RegisterAsync("other@example.com", "ein langes passwort");

        var episode = await EpisodeApi.CreateAsync(owner, "Thema Allgäu");
        var season = await owner.PostAsJsonAsync("/api/seasons", new { topic = "Allgäu Saison", episodeCount = 2, durationMin = 10 }, JsonDefaults.Web);
        var seasonEpisode = (await season.Content.ReadFromJsonAsync<JsonElement>(JsonDefaults.Web)).GetProperty("id").GetGuid();
        var otherEpisode = await EpisodeApi.CreateAsync(other, "Thema Ostfriesland");

        // Still generating: refused.
        Assert.Equal(HttpStatusCode.Conflict, (await DeleteAccountAsync(owner, "ein langes passwort")).StatusCode);

        await EpisodeApi.WaitForAsync(owner, episode, "Ready");
        await EpisodeApi.WaitForAsync(owner, seasonEpisode, "Ready");
        await EpisodeApi.WaitForAsync(other, otherEpisode, "Ready");
        var audio = Path.Combine(factory.AudioRoot, AudioGenerator.EpisodeAudioKey(episode));
        var otherAudio = Path.Combine(factory.AudioRoot, AudioGenerator.EpisodeAudioKey(otherEpisode));
        Assert.True(File.Exists(audio));

        Assert.Equal(HttpStatusCode.BadRequest, (await DeleteAccountAsync(owner, "falsch")).StatusCode);

        var deleted = await DeleteAccountAsync(owner, "ein langes passwort");

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await owner.GetAsync(new Uri("/api/auth/me", UriKind.Relative))).StatusCode);
        var login = await factory.CreateAnonymousClient().PostAsJsonAsync("/api/auth/login", new { email = "max@example.com", password = "ein langes passwort" });
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
        Assert.False(File.Exists(audio));

        // The other account is untouched, and the email can be registered again from scratch.
        Assert.Equal(HttpStatusCode.OK, (await other.GetAsync(new Uri($"/api/episodes/{otherEpisode}", UriKind.Relative))).StatusCode);
        Assert.True(File.Exists(otherAudio));
        var again = await RegisterAsync("max@example.com", "ein neues passwort");
        Assert.Equal(0, (await again.GetFromJsonAsync<JsonElement>("/api/episodes", JsonDefaults.Web)).GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task A_token_of_a_deleted_account_stops_working_at_once()
    {
        await ClaimLocalUserAsync();
        var browser = await RegisterAsync("kurz@example.com", "ein langes passwort");
        var secondTab = factory.CreateAnonymousClient();
        (await secondTab.PostAsJsonAsync("/api/auth/login", new { email = "kurz@example.com", password = "ein langes passwort" })).EnsureSuccessStatusCode();

        (await DeleteAccountAsync(browser, "ein langes passwort")).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Unauthorized, (await secondTab.GetAsync(new Uri("/api/preferences", UriKind.Relative))).StatusCode);
    }

    /// <summary>The first account in a database takes over the seeded local user; claim it so later accounts are new.</summary>
    private async Task ClaimLocalUserAsync() =>
        await factory.CreateAnonymousClient().PostAsJsonAsync("/api/auth/register", new { email = "first@example.com", password = "ein langes passwort" });

    private async Task<HttpClient> RegisterAsync(string email, string password)
    {
        var client = factory.CreateAnonymousClient();
        (await client.PostAsJsonAsync("/api/auth/register", new { email, password })).EnsureSuccessStatusCode();
        return client;
    }

    private static Task<HttpResponseMessage> DeleteAccountAsync(HttpClient client, string password) =>
        client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/auth/account") { Content = JsonContent.Create(new { password }) });
}
