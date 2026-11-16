using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PodcastGenerator.Application.Audio;

namespace PodcastGenerator.IntegrationTests;

/// <summary>Own factory: these tests change and delete accounts.</summary>
public class AccountTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Changing_the_password_ends_other_sign_ins_but_keeps_this_one()
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

        Assert.Equal(HttpStatusCode.OK, (await laptop.GetAsync(new Uri("/api/auth/me", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await RefreshAsync(laptop)).StatusCode); // this device's new sign-in works

        // The phone's access token is checked by signature only, so it works until it expires (15 minutes)...
        Assert.Equal(HttpStatusCode.OK, (await phone.GetAsync(new Uri("/api/episodes", UriKind.Relative))).StatusCode);
        // ...but it can't get a new one: its sign-in was ended.
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(phone)).StatusCode);

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
    public async Task Other_tabs_of_a_deleted_account_are_refused()
    {
        await ClaimLocalUserAsync();
        var browser = await RegisterAsync("kurz@example.com", "ein langes passwort");
        var secondTab = factory.CreateAnonymousClient();
        (await secondTab.PostAsJsonAsync("/api/auth/login", new { email = "kurz@example.com", password = "ein langes passwort" })).EnsureSuccessStatusCode();

        (await DeleteAccountAsync(browser, "ein langes passwort")).EnsureSuccessStatusCode();

        // Its access token still verifies, but there is nothing left to use it on, and no sign-in to renew.
        Assert.Equal(HttpStatusCode.Unauthorized, (await secondTab.GetAsync(new Uri("/api/preferences", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await secondTab.GetAsync(new Uri("/api/auth/me", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(secondTab)).StatusCode);
    }

    [Fact]
    public async Task Refreshing_rotates_the_token_and_signing_out_ends_the_sign_in()
    {
        await ClaimLocalUserAsync();
        var browser = await RegisterAsync("rot@example.com", "ein langes passwort");

        var refreshed = await RefreshAsync(browser);
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        var cookies = refreshed.Headers.GetValues("Set-Cookie").ToList();
        Assert.Contains(cookies, c => c.StartsWith("pg_auth=", StringComparison.Ordinal));
        Assert.Contains(cookies, c => c.StartsWith("pg_refresh=", StringComparison.Ordinal)); // a new refresh token each time

        Assert.Equal(HttpStatusCode.OK, (await RefreshAsync(browser)).StatusCode); // the new one works in turn

        Assert.Equal(HttpStatusCode.NoContent, (await browser.PostAsync(new Uri("/api/auth/logout", UriKind.Relative), null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(browser)).StatusCode);
    }

    [Fact]
    public async Task Two_tabs_refreshing_at_once_both_keep_working()
    {
        await ClaimLocalUserAsync();
        var browser = await RegisterAsync("tabs@example.com", "ein langes passwort");
        var refreshCookie = RefreshCookieOf(await RefreshAsync(browser));

        // A second tab sends the same refresh token a moment after the first used it: within the grace window.
        var secondTab = WithRefreshCookie(refreshCookie);
        Assert.Equal(HttpStatusCode.OK, (await RefreshAsync(browser)).StatusCode);
        var raced = await RefreshAsync(secondTab);

        Assert.Equal(HttpStatusCode.OK, raced.StatusCode);
        Assert.DoesNotContain(raced.Headers.GetValues("Set-Cookie"), c => c.StartsWith("pg_refresh=", StringComparison.Ordinal)); // access only
    }

    private static Task<HttpResponseMessage> RefreshAsync(HttpClient client) =>
        client.PostAsync(new Uri("/api/auth/refresh", UriKind.Relative), null);

    private static string RefreshCookieOf(HttpResponseMessage response) =>
        response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("pg_refresh=", StringComparison.Ordinal)).Split(';')[0];

    private HttpClient WithRefreshCookie(string cookie)
    {
        var client = factory.CreateAnonymousClient();
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        return client;
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

/// <summary>No grace window: any second use of a refresh token counts as theft.</summary>
public class RefreshReuseTests(NoReuseGraceApiFactory factory) : IClassFixture<NoReuseGraceApiFactory>
{
    [Fact]
    public async Task A_reused_refresh_token_ends_the_whole_sign_in()
    {
        var owner = factory.CreateAnonymousClient();
        (await owner.PostAsJsonAsync("/api/auth/register", new { email = "theft@example.com", password = "ein langes passwort" })).EnsureSuccessStatusCode();
        var first = await owner.PostAsync(new Uri("/api/auth/refresh", UriKind.Relative), null);
        var copied = first.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("pg_refresh=", StringComparison.Ordinal)).Split(';')[0];

        // The owner refreshes again (rotating past the copied token); then someone uses the copy.
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsync(new Uri("/api/auth/refresh", UriKind.Relative), null)).StatusCode);
        var thief = factory.CreateAnonymousClient();
        thief.DefaultRequestHeaders.Add("Cookie", copied);
        Assert.Equal(HttpStatusCode.Unauthorized, (await thief.PostAsync(new Uri("/api/auth/refresh", UriKind.Relative), null)).StatusCode);

        // The reuse ended the whole sign-in, including the owner's current token.
        Assert.Equal(HttpStatusCode.Unauthorized, (await owner.PostAsync(new Uri("/api/auth/refresh", UriKind.Relative), null)).StatusCode);
    }
}
