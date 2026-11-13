using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using PodcastGenerator.Application.Audio;
using PodcastGenerator.Application.Tts;
using PodcastGenerator.Domain.Enums;
using PodcastGenerator.Infrastructure.Tts;

namespace PodcastGenerator.UnitTests;

/// <summary>Builds valid MPEG-2 Layer III frames: 24 kHz, 32 kbps, mono → 96 bytes and 24 ms per frame.</summary>
internal static class SyntheticMp3
{
    public const double FrameSeconds = 576 / 24000.0;

    public static byte[] Frames(int count, bool withXing = false, bool withId3 = false)
    {
        using var ms = new MemoryStream();
        if (withId3)
        {
            ms.Write([(byte)'I', (byte)'D', (byte)'3', 3, 0, 0, 0, 0, 0, 20]); // 20-byte tag body
            ms.Write(new byte[20]);
        }

        if (withXing)
        {
            ms.Write(Frame("Xing"));
        }

        for (var i = 0; i < count; i++)
        {
            ms.Write(Frame(null));
        }

        return ms.ToArray();
    }

    private static byte[] Frame(string? tag)
    {
        var frame = new byte[96];
        frame[0] = 0xFF;
        frame[1] = 0xF3; // MPEG-2, Layer III, no CRC
        frame[2] = 0x44; // bitrate index 4 (32 kbps), sample rate index 1 (24 kHz), no padding
        frame[3] = 0xC4; // mono
        if (tag is not null)
        {
            Encoding.ASCII.GetBytes(tag).CopyTo(frame, 13);
        }

        return frame;
    }
}

public class Mp3Tests
{
    [Fact]
    public void Duration_counts_audio_frames()
    {
        var mp3 = SyntheticMp3.Frames(250);
        Assert.Equal(250 * SyntheticMp3.FrameSeconds, Mp3.DurationSeconds(mp3), 6);
    }

    [Fact]
    public void Id3_tags_and_xing_headers_are_not_audio()
    {
        var mp3 = SyntheticMp3.Frames(100, withXing: true, withId3: true);
        Assert.Equal(100 * SyntheticMp3.FrameSeconds, Mp3.DurationSeconds(mp3), 6);
    }

    [Fact]
    public void Concatenation_keeps_all_audio_and_drops_headers()
    {
        var joined = Mp3.Concatenate([SyntheticMp3.Frames(10, withXing: true, withId3: true), SyntheticMp3.Frames(20, withXing: true)]);

        Assert.Equal(30 * 96, joined.Length); // only plain frames left
        Assert.Equal(30 * SyntheticMp3.FrameSeconds, Mp3.DurationSeconds(joined), 6);
        Assert.Equal(-1, joined.AsSpan().IndexOf("Xing"u8));
    }

    [Fact]
    public void Garbage_is_not_audio() => Assert.Equal(0, Mp3.DurationSeconds(Encoding.UTF8.GetBytes("<html>error</html>")));
}

public class TtsBudgetTests
{
    [Fact]
    public void Billable_chars_count_utf8_bytes() => Assert.Equal(7, TtsBudget.BillableChars("Grüße")); // G r e: 1 byte each; ü, ß: 2 each

    [Fact]
    public void Cache_key_changes_with_voice_model_speed_or_text()
    {
        var key = AudioGenerator.CacheKey("Hallo", "de-DE-Wavenet-H", TtsModelFamily.WaveNet, "google", 0.85m);

        Assert.Equal(key, AudioGenerator.CacheKey("Hallo", "de-DE-Wavenet-H", TtsModelFamily.WaveNet, "google", 0.850m));
        Assert.NotEqual(key, AudioGenerator.CacheKey("Hallo!", "de-DE-Wavenet-H", TtsModelFamily.WaveNet, "google", 0.85m));
        Assert.NotEqual(key, AudioGenerator.CacheKey("Hallo", "de-DE-Wavenet-C", TtsModelFamily.WaveNet, "google", 0.85m));
        Assert.NotEqual(key, AudioGenerator.CacheKey("Hallo", "de-DE-Wavenet-H", TtsModelFamily.Chirp3HD, "google", 0.85m));
        Assert.NotEqual(key, AudioGenerator.CacheKey("Hallo", "de-DE-Wavenet-H", TtsModelFamily.WaveNet, "google", 1.0m));
    }
}

public class GoogleCloudTtsProviderTests
{
    [Theory]
    [InlineData("de-DE-Wavenet-H", TtsModelFamily.WaveNet)]
    [InlineData("de-DE-Chirp3-HD-Charon", TtsModelFamily.Chirp3HD)]
    [InlineData("de-DE-Standard-A", null)]
    [InlineData("de-DE-Neural2-B", null)]
    public void Only_budgeted_voice_families_are_offered(string voice, TtsModelFamily? expected) =>
        Assert.Equal(expected, GoogleCloudTtsProvider.FamilyOf(voice));

    [Fact]
    public async Task Synthesize_decodes_audio_measures_it_and_sends_the_key_as_a_header()
    {
        var audio = Convert.ToBase64String(SyntheticMp3.Frames(50));
        var handler = new Handler(HttpStatusCode.OK, $$"""{"audioContent":"{{audio}}"}""");

        var result = await Create(handler).SynthesizeAsync(Request(), CancellationToken.None);

        Assert.Equal(50 * SyntheticMp3.FrameSeconds, result.DurationSec, 6);
        // Exact URL: "text:synthesize" without "./" parses as a URI with the scheme "text", which only the real
        // socket handler rejects, so a fake handler would never notice.
        Assert.Equal("https://tts.test/v1/text:synthesize", handler.Url);
        Assert.Equal("tts-key", handler.ApiKeyHeader);
        Assert.Contains("\"audioEncoding\":\"MP3\"", handler.Body);
        Assert.Contains("\"name\":\"de-DE-Wavenet-H\"", handler.Body);
        Assert.Contains("\"speakingRate\":0.85", handler.Body);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    [InlineData(HttpStatusCode.Forbidden, false)]
    public async Task Errors_are_classified_as_transient_or_not(HttpStatusCode status, bool transient)
    {
        var ex = await Assert.ThrowsAnyAsync<TtsException>(() => Create(new Handler(status, "{}")).SynthesizeAsync(Request(), CancellationToken.None));
        Assert.Equal(transient, ex is TtsTransientException);
    }

    [Fact]
    public async Task Missing_key_fails_with_a_clear_message()
    {
        var ex = await Assert.ThrowsAsync<TtsException>(() => Create(new Handler(HttpStatusCode.OK, "{}"), "").SynthesizeAsync(Request(), CancellationToken.None));
        Assert.Contains("Tts:Google:ApiKey", ex.Message);
    }

    [Fact]
    public async Task Voices_are_filtered_to_budgeted_families()
    {
        var handler = new Handler(HttpStatusCode.OK, """
            {"voices":[
              {"name":"de-DE-Standard-A","languageCodes":["de-DE"],"ssmlGender":"FEMALE"},
              {"name":"de-DE-Wavenet-H","languageCodes":["de-DE"],"ssmlGender":"MALE"},
              {"name":"de-DE-Chirp3-HD-Charon","languageCodes":["de-DE"],"ssmlGender":"MALE"}]}
            """);

        var voices = await Create(handler).GetVoicesAsync("de-DE", CancellationToken.None);

        Assert.Equal(["de-DE-Wavenet-H", "de-DE-Chirp3-HD-Charon"], voices.Select(v => v.Name));
        Assert.Equal("https://tts.test/v1/voices?languageCode=de-DE", handler.Url);
    }

    private static TtsRequest Request() => new("Hallo Welt.", "de-DE", "de-DE-Wavenet-H", TtsModelFamily.WaveNet, 0.85m);

    private static GoogleCloudTtsProvider Create(Handler handler, string key = "tts-key") =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://tts.test/v1/") }, Options.Create(new GoogleTtsOptions { ApiKey = key }));

    private sealed class Handler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public string? ApiKeyHeader { get; private set; }

        public string Url { get; private set; } = "";

        public string Body { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            ApiKeyHeader = request.Headers.TryGetValues("X-Goog-Api-Key", out var v) ? v.Single() : null;
            Url = request.RequestUri!.ToString();
            Body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}
