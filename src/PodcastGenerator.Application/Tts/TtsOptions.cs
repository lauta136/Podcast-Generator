using PodcastGenerator.Domain.Enums;

namespace PodcastGenerator.Application.Tts;

/// <summary>App-level TTS settings bound from "Tts". Provider credentials live in the provider's own options.</summary>
public class TtsOptions
{
    public const string SectionName = "Tts";

    /// <summary>
    /// Monthly character budget per voice family. Synthesis stops before going past it. Defaults are 90% of the
    /// most conservative published free tier (1M); Google pages disagree about WaveNet (1M vs 4M), so check yours.
    /// </summary>
    public Dictionary<TtsModelFamily, BudgetOptions> Budgets { get; set; } = new()
    {
        [TtsModelFamily.WaveNet] = new() { MonthlyChars = 900_000 },
        [TtsModelFamily.Chirp3HD] = new() { MonthlyChars = 900_000 },
    };

    /// <summary>Voice used when the user hasn't picked one.</summary>
    public Dictionary<TtsModelFamily, string> DefaultVoices { get; set; } = new()
    {
        [TtsModelFamily.WaveNet] = "de-DE-Wavenet-H",
        [TtsModelFamily.Chirp3HD] = "de-DE-Chirp3-HD-Charon",
    };

    /// <summary>Average characters per German word (incl. space and umlaut bytes), for estimates before generation.</summary>
    public double CharsPerWord { get; set; } = 7.0;

    /// <summary>Pause before retrying a chunk after a transient error (multiplied by the attempt number).</summary>
    public int ChunkRetryDelayMs { get; set; } = 5000;

    public string ResolveVoice(string? chosen, TtsModelFamily family) =>
        string.IsNullOrWhiteSpace(chosen) ? DefaultVoices[family] : chosen;
}

public class BudgetOptions
{
    public long MonthlyChars { get; set; }
}
