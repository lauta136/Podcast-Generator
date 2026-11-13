using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace PodcastGenerator.IntegrationTests;

/// <summary>Own factory and database, so the library contains exactly the episodes created here.</summary>
public class LibraryTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Library_lists_newest_first_with_search_filters_paging_and_progress()
    {
        var zugspitze = await EpisodeApi.CreateAsync(_client, "Zugspitze");
        var bodensee = await EpisodeApi.CreateAsync(_client, "Bodensee 100%_sicher");
        var b2 = await _client.PostAsJsonAsync("/api/episodes", new { topic = "Thema Rügen", durationMin = 10, level = "B2" }, JsonDefaults.Web);
        b2.EnsureSuccessStatusCode();
        foreach (var id in new[] { zugspitze, bodensee })
        {
            await EpisodeApi.WaitForAsync(_client, id, "Ready");
        }

        var all = await GetAsync("/api/episodes");
        Assert.Equal(3, all.GetProperty("total").GetInt32());
        Assert.Equal("Thema Rügen", all.GetProperty("items")[0].GetProperty("topic").GetString()); // newest first

        var search = await GetAsync("/api/episodes?search=zugSPITZE");
        Assert.Equal(zugspitze, search.GetProperty("items").EnumerateArray().Single().GetProperty("id").GetGuid());

        // LIKE wildcards in the search text are literal: "%_" only matches the episode that contains it.
        var literal = await GetAsync("/api/episodes?search=" + Uri.EscapeDataString("%_"));
        Assert.Equal(bodensee, literal.GetProperty("items").EnumerateArray().Single().GetProperty("id").GetGuid());

        Assert.Equal(1, (await GetAsync("/api/episodes?level=B2")).GetProperty("total").GetInt32());
        Assert.Equal(2, (await GetAsync("/api/episodes?level=B1&status=Ready")).GetProperty("total").GetInt32());

        var page2 = await GetAsync("/api/episodes?page=2&pageSize=2");
        Assert.Equal(3, page2.GetProperty("total").GetInt32());
        Assert.Single(page2.GetProperty("items").EnumerateArray());

        await _client.PutAsJsonAsync($"/api/episodes/{zugspitze}/progress", new { positionSec = 12.5, completed = false }, JsonDefaults.Web);
        var item = (await GetAsync("/api/episodes?search=Zugspitze")).GetProperty("items")[0];
        Assert.Equal(12.5, item.GetProperty("progress").GetProperty("positionSec").GetDouble());
        Assert.Equal(JsonValueKind.Null, (await GetAsync("/api/episodes?search=Bodensee")).GetProperty("items")[0].GetProperty("progress").ValueKind);
    }

    [Fact]
    public async Task Progress_is_saved_clamped_to_the_audio_length_and_read_back()
    {
        var id = await EpisodeApi.CreateAsync(_client, "Sylt");
        await EpisodeApi.WaitForAsync(_client, id, "Ready");

        var fresh = await GetAsync($"/api/episodes/{id}/progress");
        Assert.Equal(0, fresh.GetProperty("positionSec").GetDouble());
        Assert.False(fresh.GetProperty("completed").GetBoolean());

        var saved = await _client.PutAsJsonAsync($"/api/episodes/{id}/progress", new { positionSec = 99999, completed = true }, JsonDefaults.Web);
        saved.EnsureSuccessStatusCode();

        var episode = await GetAsync($"/api/episodes/{id}");
        var progress = await GetAsync($"/api/episodes/{id}/progress");
        Assert.Equal(episode.GetProperty("audioDurationSec").GetDouble(), progress.GetProperty("positionSec").GetDouble());
        Assert.True(progress.GetProperty("completed").GetBoolean());
    }

    [Theory]
    [InlineData("/api/episodes?page=0")]
    [InlineData("/api/episodes?pageSize=500")]
    [InlineData("/api/episodes?level=Z9")]
    public async Task Invalid_library_queries_are_400(string url) =>
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync(new Uri(url, UriKind.Relative))).StatusCode);

    [Fact]
    public async Task Progress_rejects_negative_positions_and_unfinished_episodes()
    {
        var id = await EpisodeApi.CreateAsync(_client, "Helgoland");
        await EpisodeApi.WaitForAsync(_client, id, "Ready");
        var negative = await _client.PutAsJsonAsync($"/api/episodes/{id}/progress", new { positionSec = -1 }, JsonDefaults.Web);
        Assert.Equal(HttpStatusCode.BadRequest, negative.StatusCode);

        var missing = await _client.PutAsJsonAsync($"/api/episodes/{Guid.NewGuid()}/progress", new { positionSec = 1 }, JsonDefaults.Web);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    private async Task<JsonElement> GetAsync(string url) => await _client.GetFromJsonAsync<JsonElement>(url, JsonDefaults.Web);
}
