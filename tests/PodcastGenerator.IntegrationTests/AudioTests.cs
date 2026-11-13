using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PodcastGenerator.Application.Audio;
using PodcastGenerator.Infrastructure.Persistence;

namespace PodcastGenerator.IntegrationTests;

public class AudioTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Ready_episode_has_one_mp3_and_every_sentence_has_a_timing()
    {
        var id = await EpisodeApi.CreateAsync(_client, "Zugspitze");
        var status = await EpisodeApi.WaitForAsync(_client, id, "Ready");
        Assert.True(status.GetProperty("audioTotal").GetInt32() > 2);
        Assert.Equal(status.GetProperty("audioTotal").GetInt32(), status.GetProperty("audioCompleted").GetInt32());

        var audio = await _client.GetAsync(new Uri($"/api/episodes/{id}/audio", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, audio.StatusCode);
        Assert.Equal("audio/mpeg", audio.Content.Headers.ContentType?.MediaType);
        Assert.Equal("bytes", audio.Headers.AcceptRanges.Single());
        var bytes = await audio.Content.ReadAsByteArrayAsync();
        var fileSeconds = Mp3.DurationSeconds(bytes);

        var episode = await _client.GetFromJsonAsync<JsonElement>($"/api/episodes/{id}", JsonDefaults.Web);
        Assert.Equal(fileSeconds, episode.GetProperty("audioDurationSec").GetDouble(), 2);

        // Timings: contiguous, increasing, ending where the audio ends.
        var transcript = await _client.GetFromJsonAsync<JsonElement>($"/api/episodes/{id}/transcript", JsonDefaults.Web);
        var segments = transcript.GetProperty("sections").EnumerateArray().SelectMany(s => s.GetProperty("segments").EnumerateArray()).ToList();
        double previousEnd = 0;
        foreach (var s in segments)
        {
            var start = s.GetProperty("startSec").GetDouble();
            var end = s.GetProperty("endSec").GetDouble();
            Assert.Equal(previousEnd, start, 2);
            Assert.True(end > start);
            previousEnd = end;
        }

        Assert.Equal(fileSeconds, previousEnd, 1);
    }

    [Fact]
    public async Task Audio_supports_range_requests_for_seeking()
    {
        var id = await EpisodeApi.CreateAsync(_client, "Bodensee");
        await EpisodeApi.WaitForAsync(_client, id, "Ready");

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/episodes/{id}/audio");
        request.Headers.Range = new RangeHeaderValue(960, 1919);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Equal(960, response.Content.Headers.ContentRange?.From);
        Assert.Equal(960, (await response.Content.ReadAsByteArrayAsync()).Length);
    }

    [Fact]
    public async Task Audio_is_not_found_before_the_episode_is_ready()
    {
        var response = await _client.GetAsync(new Uri($"/api/episodes/{Guid.NewGuid()}/audio", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Synthesized_characters_are_counted_against_the_budget()
    {
        var before = await UsedAsync();
        var id = await EpisodeApi.CreateAsync(_client, "Elbphilharmonie");
        await EpisodeApi.WaitForAsync(_client, id, "Ready");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var chunkChars = await db.AudioSegments.Where(a => a.EpisodeId == id).SumAsync(a => a.Characters);
        Assert.Equal(before + chunkChars, await UsedAsync());
    }

    [Fact]
    public async Task A_transient_tts_error_is_retried_within_the_run()
    {
        factory.Tts.TransientOnceWhenTextContains = "Thema Starnberger See";
        var id = await EpisodeApi.CreateAsync(_client, "Starnberger See");
        await EpisodeApi.WaitForAsync(_client, id, "Ready");

        // The topic opens both sections' first paragraph: each of those chunks failed once, then succeeded.
        Assert.Equal(4, factory.Tts.CallsFor("Thema Starnberger See"));
        Assert.Equal(2, factory.Tts.CallsFor("Thema Starnberger See. Das ist Satz 1 aus Teil 2"));
    }

    [Fact]
    public async Task Retry_after_an_audio_failure_resynthesizes_only_the_failed_chunk()
    {
        // Fails the first chunk of section 2, after all of section 1 is synthesized.
        factory.Tts.FailOnceWhen = text => text.StartsWith("Thema Wattenmeer. Das ist Satz 1 aus Teil 2", StringComparison.Ordinal);
        var id = await EpisodeApi.CreateAsync(_client, "Wattenmeer");
        var failed = await EpisodeApi.WaitForAsync(_client, id, "Failed");
        Assert.Contains("voice not available", failed.GetProperty("lastError").GetString());
        Assert.True(failed.GetProperty("audioCompleted").GetInt32() > 0);
        var callsBefore = factory.Tts.CallsFor("Thema Wattenmeer. Das ist Satz 1 aus Teil 1");

        var retry = await _client.PostAsync(new Uri($"/api/episodes/{id}/retry", UriKind.Relative), null);
        Assert.Equal(HttpStatusCode.Accepted, retry.StatusCode);
        await EpisodeApi.WaitForAsync(_client, id, "Ready");

        Assert.Equal(1, callsBefore);
        Assert.Equal(1, factory.Tts.CallsFor("Thema Wattenmeer. Das ist Satz 1 aus Teil 1")); // finished chunk not redone
        Assert.Equal(2, factory.Tts.CallsFor("Thema Wattenmeer. Das ist Satz 1 aus Teil 2"));
    }

    [Fact]
    public async Task Deleting_an_episode_removes_its_audio_files()
    {
        var id = await EpisodeApi.CreateAsync(_client, "Loreley");
        await EpisodeApi.WaitForAsync(_client, id, "Ready");
        var folder = Path.Combine(factory.AudioRoot, Path.GetDirectoryName(AudioGenerator.EpisodeAudioKey(id))!);
        Assert.True(File.Exists(Path.Combine(factory.AudioRoot, AudioGenerator.EpisodeAudioKey(id))));
        Assert.NotEmpty(Directory.GetFiles(folder, "*.mp3", SearchOption.AllDirectories));

        var delete = await _client.DeleteAsync(new Uri($"/api/episodes/{id}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Empty(Directory.Exists(folder) ? Directory.GetFiles(folder, "*.mp3", SearchOption.AllDirectories) : []);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync(new Uri($"/api/episodes/{id}", UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task Voices_and_budget_endpoints_report_provider_and_usage()
    {
        var voices = await _client.GetFromJsonAsync<JsonElement>("/api/voices?language=de", JsonDefaults.Web);
        Assert.Equal(2, voices.GetArrayLength());

        var budget = await _client.GetFromJsonAsync<JsonElement>("/api/tts/budget", JsonDefaults.Web);
        var wavenet = budget.EnumerateArray().Single(b => b.GetProperty("modelFamily").GetString() == "WaveNet");
        Assert.Equal(900_000, wavenet.GetProperty("monthlyBudget").GetInt64());
    }

    [Fact]
    public async Task Estimate_reports_words_chars_and_budget_fit()
    {
        var estimate = await _client.GetFromJsonAsync<JsonElement>("/api/episodes/estimate?level=B1&durationMin=10", JsonDefaults.Web);

        Assert.Equal(0.85m, estimate.GetProperty("speakingRate").GetDecimal()); // B1 default
        Assert.Equal(1360, estimate.GetProperty("targetWords").GetInt32()); // 10 min × 160 wpm × 0.85
        Assert.Equal(9520, estimate.GetProperty("estimatedChars").GetInt64()); // 1360 words × 7
        Assert.True(estimate.GetProperty("fitsBudget").GetBoolean());

        var faster = await _client.GetFromJsonAsync<JsonElement>("/api/episodes/estimate?level=B1&durationMin=10&speakingRate=1.0", JsonDefaults.Web);
        Assert.Equal(1600, faster.GetProperty("targetWords").GetInt32());
    }

    [Fact]
    public async Task Episodes_default_to_the_level_speed_and_can_override_it()
    {
        var byLevel = await EpisodeApi.CreateAsync(_client, "Thema Lüneburger Heide");
        var created = await _client.PostAsJsonAsync(
            "/api/episodes", new { topic = "Thema Spreewald", durationMin = 10, level = "B1", speakingRate = 1.1 }, JsonDefaults.Web);
        created.EnsureSuccessStatusCode();
        var overridden = (await created.Content.ReadFromJsonAsync<JsonElement>(JsonDefaults.Web)).GetProperty("id").GetGuid();

        await EpisodeApi.WaitForAsync(_client, byLevel, "Ready");
        await EpisodeApi.WaitForAsync(_client, overridden, "Ready");

        var episode = await _client.GetFromJsonAsync<JsonElement>($"/api/episodes/{overridden}", JsonDefaults.Web);
        Assert.Equal(1.1m, episode.GetProperty("speakingRate").GetDecimal());
        Assert.Equal(1760, episode.GetProperty("targetWords").GetInt32());
        Assert.Equal([0.85m], factory.Tts.RatesFor("Thema Lüneburger Heide"));
        Assert.Equal([1.1m], factory.Tts.RatesFor("Thema Spreewald"));
    }

    [Theory]
    [InlineData(0.3)]
    [InlineData(2.0)]
    public async Task Speaking_rate_outside_the_allowed_range_is_rejected(double rate)
    {
        var response = await _client.PostAsJsonAsync(
            "/api/episodes", new { topic = "Thema Harz", durationMin = 10, level = "B1", speakingRate = rate }, JsonDefaults.Web);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(JsonDefaults.Web);
        Assert.True(problem.GetProperty("errors").TryGetProperty("SpeakingRate", out _));
    }

    private async Task<long> UsedAsync()
    {
        var budget = await _client.GetFromJsonAsync<JsonElement>("/api/tts/budget", JsonDefaults.Web);
        return budget.EnumerateArray().Single(b => b.GetProperty("modelFamily").GetString() == "WaveNet").GetProperty("used").GetInt64();
    }
}

public class BudgetPauseTests(TinyBudgetApiFactory factory) : IClassFixture<TinyBudgetApiFactory>
{
    [Fact]
    public async Task Synthesis_pauses_at_the_budget_and_never_goes_past_it()
    {
        var client = factory.CreateClient();
        var id = await EpisodeApi.CreateAsync(client, "Hamburger Hafen");

        var paused = await EpisodeApi.WaitForAsync(client, id, "PausedForBudget");
        Assert.Contains("Nothing was charged", paused.GetProperty("lastError").GetString());
        Assert.True(paused.GetProperty("audioCompleted").GetInt32() < paused.GetProperty("audioTotal").GetInt32());

        var budget = await client.GetFromJsonAsync<JsonElement>("/api/tts/budget", JsonDefaults.Web);
        var used = budget.EnumerateArray().Single(b => b.GetProperty("modelFamily").GetString() == "WaveNet").GetProperty("used").GetInt64();
        Assert.InRange(used, 1, TinyBudgetApiFactory.Budget);

        // Retrying while the budget is still exhausted pauses again without sending anything.
        var callsBefore = factory.Tts.TotalCalls;
        await client.PostAsync(new Uri($"/api/episodes/{id}/retry", UriKind.Relative), null);
        await Task.Delay(500);
        await EpisodeApi.WaitForAsync(client, id, "PausedForBudget");
        Assert.Equal(callsBefore, factory.Tts.TotalCalls);
    }
}

public class BudgetRefusalTests(NoBudgetApiFactory factory) : IClassFixture<NoBudgetApiFactory>
{
    [Fact]
    public async Task Creating_an_episode_that_cannot_fit_the_budget_is_refused()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/episodes", new { topic = "Kölner Dom", durationMin = 10 }, JsonDefaults.Web);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("Not enough free WaveNet budget", await response.Content.ReadAsStringAsync());
        Assert.Equal(0, factory.Llm.Calls("Kölner Dom", "outline")); // no LLM quota spent either
    }
}
