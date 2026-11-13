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

    /// <summary>
    /// How fast each voice family speaks, used to turn minutes into words. Measured on German voices
    /// (2026-11-13): WaveNet read a B1 episode at 160 words/min including paragraph pauses, and its duration
    /// scales exactly with the speaking rate. Chirp 3: HD is ~6% slower and reacts less to the rate
    /// (0.75× made audio 1.21× longer, not 1.33×), hence the exponent.
    /// </summary>
    public Dictionary<TtsModelFamily, PaceOptions> Pace { get; set; } = new()
    {
        [TtsModelFamily.WaveNet] = new() { WordsPerMinute = 160, RateExponent = 1.0 },
        [TtsModelFamily.Chirp3HD] = new() { WordsPerMinute = 150, RateExponent = 0.66 },
    };

    /// <summary>Words per minute of generated audio for this family at this speaking rate.</summary>
    public double WordsPerMinute(TtsModelFamily family, decimal speakingRate)
    {
        var pace = Pace[family];
        return pace.WordsPerMinute * Math.Pow((double)speakingRate, pace.RateExponent);
    }

    /// <summary>Average characters per German word (incl. space and umlaut bytes), for estimates before generation.</summary>
    public double CharsPerWord { get; set; } = 7.0;

    /// <summary>Pause before retrying a chunk after a transient error (multiplied by the attempt number).</summary>
    public int ChunkRetryDelayMs { get; set; } = 5000;

    public string ResolveVoice(string? chosen, TtsModelFamily family) =>
        string.IsNullOrWhiteSpace(chosen) ? DefaultVoices[family] : chosen;
}

public class PaceOptions
{
    /// <summary>Words per minute at speaking rate 1.0, pauses included.</summary>
    public double WordsPerMinute { get; set; }

    /// <summary>Audio pace = rate^exponent. 1.0 means the rate applies fully.</summary>
    public double RateExponent { get; set; } = 1.0;
}

public class BudgetOptions
{
    public long MonthlyChars { get; set; }
}
