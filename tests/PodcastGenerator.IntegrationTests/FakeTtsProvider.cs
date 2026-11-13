using System.Collections.Concurrent;
using PodcastGenerator.Application.Tts;
using PodcastGenerator.Domain.Enums;

namespace PodcastGenerator.IntegrationTests;

/// <summary>
/// Returns real, valid MP3 (MPEG-2 Layer III, 24 kHz, 96-byte frames of 24 ms), one frame per 5 characters,
/// so parsing, stitching, timings and Range serving run on genuine audio frames. Never calls Google.
/// </summary>
public sealed class FakeTtsProvider : ITtsProvider
{
    public const double FrameSeconds = 576 / 24000.0;

    private readonly ConcurrentDictionary<string, int> _calls = new();

    /// <summary>The first request for a text matching this fails permanently (once per text).</summary>
    public Func<string, bool>? FailOnceWhen { get; set; }

    /// <summary>Requests whose text contains this fail transiently (503-like) on their first attempt.</summary>
    public string? TransientOnceWhenTextContains { get; set; }

    public string Name => "fake";

    public int CallsFor(string textFragment) => _calls.Where(kv => kv.Key.Contains(textFragment, StringComparison.Ordinal)).Sum(kv => kv.Value);

    public int TotalCalls => _calls.Values.Sum();

    public Task<TtsResult> SynthesizeAsync(TtsRequest request, CancellationToken ct)
    {
        var n = _calls.AddOrUpdate(request.Text, 1, (_, c) => c + 1);
        if (FailOnceWhen is { } fail && fail(request.Text) && n == 1)
        {
            throw new TtsException("Simulated: voice not available");
        }

        if (TransientOnceWhenTextContains is { } transient && request.Text.Contains(transient, StringComparison.Ordinal) && n == 1)
        {
            throw new TtsTransientException("Simulated: 503 from TTS");
        }

        var frames = Math.Max(1, request.Text.Length / 5);
        var mp3 = new byte[frames * 96];
        for (var i = 0; i < frames; i++)
        {
            mp3[i * 96] = 0xFF;
            mp3[(i * 96) + 1] = 0xF3;
            mp3[(i * 96) + 2] = 0x44;
            mp3[(i * 96) + 3] = 0xC4;
        }

        return Task.FromResult(new TtsResult(mp3, frames * FrameSeconds));
    }

    public Task<IReadOnlyList<VoiceInfo>> GetVoicesAsync(string languageCode, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<VoiceInfo>>(
        [
            new("de-DE-Wavenet-H", languageCode, "MALE", TtsModelFamily.WaveNet),
            new("de-DE-Chirp3-HD-Charon", languageCode, "MALE", TtsModelFamily.Chirp3HD),
        ]);
}
