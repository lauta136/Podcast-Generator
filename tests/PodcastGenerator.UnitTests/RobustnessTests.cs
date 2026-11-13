using System.Net;
using System.Text.Json;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PodcastGenerator.Application;
using PodcastGenerator.Application.Generation;
using PodcastGenerator.Application.Llm;
using PodcastGenerator.Application.Tts;
using PodcastGenerator.Domain.Enums;
using PodcastGenerator.Infrastructure.Llm;
using PodcastGenerator.Infrastructure.Storage;

namespace PodcastGenerator.UnitTests;

public class BudgetMonthTests
{
    [Theory]
    [InlineData("2026-12-01T05:00:00Z", "2026-11-01")] // 21:00 on 30 Nov in California: Google still bills November
    [InlineData("2026-12-01T09:00:00Z", "2026-12-01")]
    [InlineData("2026-07-01T06:30:00Z", "2026-06-01")] // summer time: UTC-7
    [InlineData("2026-07-01T07:30:00Z", "2026-07-01")]
    public void Month_follows_the_billing_time_zone(string utcNow, string expected)
    {
        var budget = new TtsBudget(null!, Options.Create(new TtsOptions()), new FixedClock(DateTimeOffset.Parse(utcNow, System.Globalization.CultureInfo.InvariantCulture)));
        Assert.Equal(DateOnly.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), budget.CurrentMonth());
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

public sealed class LocalFileAudioStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"podcast_storage_{Guid.NewGuid():N}");

    [Theory]
    [InlineData("")]
    [InlineData("/")]
    public async Task A_trailing_slash_in_the_configured_path_still_accepts_keys(string suffix)
    {
        var storage = Create(_root + suffix);

        await storage.SaveAsync("episodes/x/episode.mp3", new MemoryStream([1, 2, 3]), "audio/mpeg", CancellationToken.None);

        Assert.True(await storage.ExistsAsync("episodes/x/episode.mp3", CancellationToken.None));
        Assert.True(File.Exists(Path.Combine(_root, "episodes", "x", "episode.mp3")));
    }

    [Fact]
    public async Task Keys_cannot_escape_the_root()
    {
        var storage = Create(_root + "/");
        await Assert.ThrowsAsync<ArgumentException>(() => storage.ExistsAsync("../outside.mp3", CancellationToken.None));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static LocalFileAudioStorage Create(string path) =>
        new(Options.Create(new StorageOptions { LocalPath = path }), new Env());

    private sealed class Env : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

public class LlmReplyRobustnessTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Null_fields_in_a_reply_fail_validation_instead_of_crashing()
    {
        var section = JsonSerializer.Deserialize<SectionResponse>("""{"paragraphs":null,"recap":null}""", Web)!;
        Assert.NotEmpty(section.Validate(500));

        var withNullItem = JsonSerializer.Deserialize<SectionResponse>("""{"paragraphs":["Hallo.",null],"recap":"r"}""", Web)!;
        Assert.Equal(["Hallo."], withNullItem.Paragraphs);

        var outline = JsonSerializer.Deserialize<OutlineResponse>("""{"title":null,"description":null,"sections":null}""", Web)!;
        Assert.NotEmpty(outline.Validate(2));
        Assert.Equal("", outline.Description);

        var proofread = JsonSerializer.Deserialize<ProofreadResponse>("""{"paragraphs":null,"corrections":null}""", Web)!;
        Assert.NotEmpty(proofread.Validate(["Hallo."]));
    }

    [Theory]
    [InlineData("this is not json")]
    [InlineData("""{"choices":[{}]}""")]
    [InlineData("""{"choices":null}""")]
    public async Task Malformed_successful_replies_are_invalid_responses_so_they_get_repaired(string body)
    {
        using var handler = new FixedHandler(body);
        var provider = new OpenAiCompatibleLlmProvider(
            new HttpClient(handler) { BaseAddress = new Uri("https://llm.test/v1/") },
            Options.Create(new LlmOptions { BaseUrl = "https://llm.test/v1/", Model = "m", ApiKey = "k", RequestsPerMinute = 1000 }),
            new RequestRateLimiter(1000, TimeProvider.System),
            TimeProvider.System,
            NullLogger<OpenAiCompatibleLlmProvider>.Instance);

        await Assert.ThrowsAsync<LlmInvalidResponseException>(
            () => provider.GenerateTextAsync(new LlmRequest("test", "system", "user"), CancellationToken.None));
    }

    [Fact]
    public void Control_characters_are_stripped_before_storage() =>
        Assert.Equal("Hallo Welt.", TextSegmenter.CleanForSpeech("Hallo\u0000 Welt.\u0007"));

    private sealed class FixedHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
    }
}

public class SpeakingRateValidationTests
{
    private readonly EpisodeOptions _options = new();

    [Theory]
    [InlineData("0.85", true)]
    [InlineData("0.6", true)]
    [InlineData("1.25", true)]
    [InlineData("0.855", false)] // would be stored as 0.86 but used as 0.855
    [InlineData("0.5", false)]
    [InlineData("1.3", false)]
    public void Rates_must_be_in_range_with_two_decimals(string rate, bool valid) =>
        Assert.Equal(valid, _options.SpeakingRateError(decimal.Parse(rate, System.Globalization.CultureInfo.InvariantCulture)) is null);
}
