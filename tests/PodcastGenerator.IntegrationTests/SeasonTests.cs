using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PodcastGenerator.Application.Audio;

namespace PodcastGenerator.IntegrationTests;

public class SeasonTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    /// <summary>
    /// The whole life of a season, because each step needs the previous one: the arc is planned with episode 1,
    /// later episodes are made one at a time from it, the order is protected, and the season ends complete.
    /// </summary>
    [Fact]
    public async Task A_season_is_planned_once_and_its_episodes_build_on_each_other()
    {
        var created = await _client.PostAsJsonAsync("/api/seasons", new { topic = "Hanse", episodeCount = 3, durationMin = 10, level = "B1" }, JsonDefaults.Web);
        Assert.Equal(HttpStatusCode.Accepted, created.StatusCode);
        var seasonUrl = created.Headers.Location!.AbsolutePath;
        var first = await created.Content.ReadFromJsonAsync<JsonElement>(JsonDefaults.Web);
        Assert.Equal(1, first.GetProperty("seasonOrder").GetInt32());
        await EpisodeApi.WaitForAsync(_client, first.GetProperty("id").GetGuid(), "Ready");

        var season = await GetAsync(seasonUrl);
        Assert.True(season.GetProperty("planned").GetBoolean());
        Assert.Equal("Serie Hanse", season.GetProperty("title").GetString());
        var parts = season.GetProperty("parts").EnumerateArray().ToList();
        Assert.Equal(3, parts.Count);
        Assert.Equal("Folge 1 Hanse", parts[0].GetProperty("episode").GetProperty("topic").GetString()); // topic from the arc
        Assert.Equal(JsonValueKind.Null, parts[1].GetProperty("episode").ValueKind);
        Assert.Equal(1, factory.Llm.ArcCalls("Hanse"));

        var library = await GetAsync("/api/episodes?search=Folge%201%20Hanse");
        Assert.Equal("Serie Hanse", library.GetProperty("items")[0].GetProperty("seasonTitle").GetString());

        // Episode 2: made from the arc and episode 1's summary.
        var second = await NextAsync(seasonUrl);
        Assert.Equal(HttpStatusCode.Conflict, (await PostNextAsync(seasonUrl)).StatusCode); // episode 2 isn't ready yet
        await EpisodeApi.WaitForAsync(_client, second, "Ready");

        var outline = Assert.Single(factory.Llm.PromptsFor("outline", "Topic: Folge 2 Hanse"));
        Assert.Contains("This is episode 2 of 3 of the series \"Serie Hanse\"", outline, StringComparison.Ordinal);
        Assert.Contains("3. Folge 3 Hanse: Episode 3 covers part 3.", outline, StringComparison.Ordinal); // the whole arc
        Assert.Contains("- Episode 1: Part 1 was covered.", outline, StringComparison.Ordinal); // what episode 1 said
        var opening = factory.Llm.PromptsFor("section", "of the episode \"Folge 2 Hanse\"").First();
        Assert.Contains("this is part 2 of the series", opening, StringComparison.Ordinal);

        // The order is protected: episode 1 can't be deleted while episode 2 exists, the latest can, and is made again.
        var episode1 = first.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Conflict, (await _client.DeleteAsync(new Uri($"/api/episodes/{episode1}", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync(new Uri($"/api/episodes/{second}", UriKind.Relative))).StatusCode);
        var secondAgain = await NextAsync(seasonUrl);
        await EpisodeApi.WaitForAsync(_client, secondAgain, "Ready");

        var third = await NextAsync(seasonUrl);
        await EpisodeApi.WaitForAsync(_client, third, "Ready");
        var lastSection = factory.Llm.PromptsFor("section", "of the episode \"Folge 3 Hanse\"").Last();
        Assert.Contains("wrap-up of the whole series", lastSection, StringComparison.Ordinal);

        Assert.Equal(HttpStatusCode.Conflict, (await PostNextAsync(seasonUrl)).StatusCode); // all 3 exist
        Assert.Equal(1, factory.Llm.ArcCalls("Hanse")); // planned once for the whole season

        var list = await _client.GetFromJsonAsync<JsonElement>("/api/seasons", JsonDefaults.Web);
        var summary = list.EnumerateArray().Single(s => s.GetProperty("title").GetString() == "Serie Hanse");
        Assert.Equal(3, summary.GetProperty("episodesReady").GetInt32());
    }

    [Fact]
    public async Task Deleting_a_season_deletes_its_episodes_and_audio()
    {
        var created = await _client.PostAsJsonAsync("/api/seasons", new { topic = "Bodensee Saison", episodeCount = 2, durationMin = 10 }, JsonDefaults.Web);
        var seasonUrl = created.Headers.Location!.AbsolutePath;
        var episodeId = (await created.Content.ReadFromJsonAsync<JsonElement>(JsonDefaults.Web)).GetProperty("id").GetGuid();
        await EpisodeApi.WaitForAsync(_client, episodeId, "Ready");
        var audio = Path.Combine(factory.AudioRoot, AudioGenerator.EpisodeAudioKey(episodeId));
        Assert.True(File.Exists(audio));

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync(new Uri(seasonUrl, UriKind.Relative))).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync(new Uri(seasonUrl, UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync(new Uri($"/api/episodes/{episodeId}", UriKind.Relative))).StatusCode);
        Assert.False(File.Exists(audio));
    }

    [Fact]
    public async Task A_failed_arc_is_planned_again_on_retry()
    {
        var created = await _client.PostAsJsonAsync("/api/seasons", new { topic = "ArcOutageOnce Rhein", episodeCount = 2, durationMin = 10 }, JsonDefaults.Web);
        var seasonUrl = created.Headers.Location!.AbsolutePath;
        var episodeId = (await created.Content.ReadFromJsonAsync<JsonElement>(JsonDefaults.Web)).GetProperty("id").GetGuid();
        await EpisodeApi.WaitForAsync(_client, episodeId, "Failed");
        Assert.False((await GetAsync(seasonUrl)).GetProperty("planned").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict, (await PostNextAsync(seasonUrl)).StatusCode); // no plan, no episode 2

        await _client.PostAsync(new Uri($"/api/episodes/{episodeId}/retry", UriKind.Relative), null);
        await EpisodeApi.WaitForAsync(_client, episodeId, "Ready");

        Assert.True((await GetAsync(seasonUrl)).GetProperty("planned").GetBoolean());
        Assert.Equal(2, factory.Llm.ArcCalls("ArcOutageOnce Rhein"));
    }

    [Theory]
    [InlineData("", 3)]
    [InlineData("Eifel", 1)]
    [InlineData("Eifel", 13)]
    public async Task Invalid_seasons_are_400(string topic, int episodeCount)
    {
        var response = await _client.PostAsJsonAsync("/api/seasons", new { topic, episodeCount }, JsonDefaults.Web);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Another_account_cannot_see_or_extend_a_season()
    {
        var created = await _client.PostAsJsonAsync("/api/seasons", new { topic = "Harz Saison", episodeCount = 2, durationMin = 10 }, JsonDefaults.Web);
        var seasonUrl = created.Headers.Location!.AbsolutePath;
        // The first account in a database takes over the local user (who owns this season); the second is someone else.
        await factory.CreateAnonymousClient().PostAsJsonAsync("/api/auth/register", new { email = "season-owner@example.com", password = "ein langes passwort" });
        var other = factory.CreateAnonymousClient();
        var registered = await (await other.PostAsJsonAsync("/api/auth/register", new { email = "season-other@example.com", password = "ein langes passwort" }))
            .Content.ReadFromJsonAsync<JsonElement>(JsonDefaults.Web);
        Assert.NotEqual(Infrastructure.Persistence.SeedData.LocalUserId, registered.GetProperty("id").GetGuid());

        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(new Uri(seasonUrl, UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsync(new Uri(seasonUrl + "/episodes", UriKind.Relative), null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync(new Uri(seasonUrl, UriKind.Relative))).StatusCode);
        Assert.Empty((await other.GetFromJsonAsync<JsonElement>("/api/seasons", JsonDefaults.Web)).EnumerateArray());
    }

    private async Task<JsonElement> GetAsync(string url) => await _client.GetFromJsonAsync<JsonElement>(url, JsonDefaults.Web);

    private Task<HttpResponseMessage> PostNextAsync(string seasonUrl) =>
        _client.PostAsync(new Uri(seasonUrl + "/episodes", UriKind.Relative), null);

    private async Task<Guid> NextAsync(string seasonUrl)
    {
        var response = await PostNextAsync(seasonUrl);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(JsonDefaults.Web)).GetProperty("id").GetGuid();
    }
}
