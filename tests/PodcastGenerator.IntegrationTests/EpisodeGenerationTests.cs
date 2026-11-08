using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace PodcastGenerator.IntegrationTests;

public class EpisodeGenerationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Created_episode_is_generated_in_the_background_until_ready()
    {
        var response = await _client.PostAsJsonAsync("/api/episodes", new { topic = "Kaffee in Wien", durationMin = 10, level = "B1" }, JsonDefaults.Web);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.EndsWith("/status", response.Headers.Location?.ToString());
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>(JsonDefaults.Web)).GetProperty("id").GetGuid();

        var status = await WaitForAsync(id, "Ready");
        Assert.Equal(2, status.GetProperty("sectionsTotal").GetInt32());
        Assert.Equal(2, status.GetProperty("sectionsCompleted").GetInt32());

        var transcript = await _client.GetFromJsonAsync<JsonElement>($"/api/episodes/{id}/transcript", JsonDefaults.Web);
        var sections = transcript.GetProperty("sections");
        Assert.Equal(2, sections.GetArrayLength());
        Assert.True(sections[0].GetProperty("segments").GetArrayLength() > 10);
        Assert.Equal(1, factory.Llm.Calls("Kaffee in Wien", "outline"));
    }

    [Fact]
    public async Task Retry_after_a_failure_redoes_only_the_failed_section()
    {
        const string topic = "Berliner Mauer FailSectionTwoOnce";
        var id = await CreateAsync(topic);

        var failed = await WaitForAsync(id, "Failed");
        Assert.Contains("Simulated provider outage", failed.GetProperty("lastError").GetString());
        Assert.Equal(1, failed.GetProperty("sectionsCompleted").GetInt32());

        var retry = await _client.PostAsync(new Uri($"/api/episodes/{id}/retry", UriKind.Relative), null);
        Assert.Equal(HttpStatusCode.Accepted, retry.StatusCode);
        await WaitForAsync(id, "Ready");

        Assert.Equal(1, factory.Llm.Calls(topic, "outline"));
        Assert.Equal(1, factory.Llm.Calls(topic, "section-1"));
        Assert.Equal(2, factory.Llm.Calls(topic, "section-2"));
    }

    [Fact]
    public async Task Short_episode_gets_one_continuation_before_the_closing_section()
    {
        const string topic = "Oktoberfest ShortOnWords";
        var id = await CreateAsync(topic);
        await WaitForAsync(id, "Ready");

        var transcript = await _client.GetFromJsonAsync<JsonElement>($"/api/episodes/{id}/transcript", JsonDefaults.Web);
        var titles = transcript.GetProperty("sections").EnumerateArray().Select(s => s.GetProperty("title").GetString()).ToList();
        Assert.Equal(["Teil 1", "Noch mehr dazu", "Teil 2"], titles);
        Assert.Equal(3, factory.Llm.Calls(topic, "section"));
    }

    [Fact]
    public async Task Retrying_a_ready_episode_is_a_conflict()
    {
        var id = await CreateAsync("Brezeln");
        await WaitForAsync(id, "Ready");

        var retry = await _client.PostAsync(new Uri($"/api/episodes/{id}/retry", UriKind.Relative), null);

        Assert.Equal(HttpStatusCode.Conflict, retry.StatusCode);
    }

    [Fact]
    public async Task Ready_episode_can_be_rated()
    {
        var id = await CreateAsync("Fußball");
        await WaitForAsync(id, "Ready");

        var put = await _client.PutAsJsonAsync($"/api/episodes/{id}/rating", new { rating = "TooHard" }, JsonDefaults.Web);
        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);

        var episode = await _client.GetFromJsonAsync<JsonElement>($"/api/episodes/{id}", JsonDefaults.Web);
        Assert.Equal("TooHard", episode.GetProperty("difficultyRating").GetString());
    }

    [Fact]
    public async Task Missing_topic_without_surprise_me_is_rejected()
    {
        var response = await _client.PostAsJsonAsync("/api/episodes", new { durationMin = 10 }, JsonDefaults.Web);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_episode_is_not_found()
    {
        var response = await _client.GetAsync(new Uri($"/api/episodes/{Guid.NewGuid()}/status", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<Guid> CreateAsync(string topic)
    {
        var response = await _client.PostAsJsonAsync("/api/episodes", new { topic, durationMin = 10, level = "B1" }, JsonDefaults.Web);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>(JsonDefaults.Web)).GetProperty("id").GetGuid();
    }

    private async Task<JsonElement> WaitForAsync(Guid id, string expected)
    {
        var deadline = DateTime.UtcNow + Timeout;
        JsonElement status;
        do
        {
            status = await _client.GetFromJsonAsync<JsonElement>($"/api/episodes/{id}/status", JsonDefaults.Web);
            if (status.GetProperty("status").GetString() == expected)
            {
                return status;
            }

            await Task.Delay(100);
        }
        while (DateTime.UtcNow < deadline);

        throw new TimeoutException($"Episode {id} did not reach {expected}; last status: {status}");
    }
}
