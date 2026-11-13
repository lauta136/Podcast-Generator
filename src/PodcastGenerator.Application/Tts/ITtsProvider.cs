using PodcastGenerator.Domain.Enums;

namespace PodcastGenerator.Application.Tts;

public interface ITtsProvider
{
    /// <summary>Provider name stored with each audio chunk and part of its cache key, e.g. "google".</summary>
    string Name { get; }

    /// <summary>Returns MP3 bytes plus their measured duration.</summary>
    Task<TtsResult> SynthesizeAsync(TtsRequest request, CancellationToken ct);

    Task<IReadOnlyList<VoiceInfo>> GetVoicesAsync(string languageCode, CancellationToken ct);
}

/// <param name="SpeakingRate">1.0 = the voice's natural pace.</param>
public sealed record TtsRequest(string Text, string LanguageCode, string VoiceName, TtsModelFamily ModelFamily, decimal SpeakingRate);

public sealed record TtsResult(byte[] Mp3, double DurationSec);

public sealed record VoiceInfo(string Name, string LanguageCode, string Gender, TtsModelFamily ModelFamily);

/// <summary>Synthesis failed (bad voice, provider error after retries). The step fails and can be retried.</summary>
public class TtsException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Synthesizing would go past the configured free-tier budget. The episode pauses; nothing is charged.</summary>
public class TtsBudgetExceededException(TtsModelFamily family, long used, long budget) : Exception(
    $"Paused: the monthly {family} budget is used up ({used:N0} of {budget:N0} characters). " +
    "Nothing was charged. Retry next month, raise Tts:Budgets after checking your Google free tier, or switch voice quality.")
{
    public TtsModelFamily Family { get; } = family;
}

/// <summary>Temporary provider trouble (429, 5xx, timeout): worth another attempt after a pause.</summary>
public class TtsTransientException(string message, Exception? inner = null) : TtsException(message, inner);
