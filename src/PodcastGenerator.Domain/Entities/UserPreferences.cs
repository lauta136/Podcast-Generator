using PodcastGenerator.Domain.Enums;

namespace PodcastGenerator.Domain.Entities;

public class UserPreferences
{
    public Guid UserId { get; set; }

    /// <summary>ISO 639-1 code, e.g. "en".</summary>
    public string NativeLanguage { get; set; } = "en";

    /// <summary>ISO 639-1 code. Only "de" is supported for now.</summary>
    public string TargetLanguage { get; set; } = "de";

    public CefrLevel DefaultLevel { get; set; } = CefrLevel.B1;
    public int DefaultDurationMin { get; set; } = 10;
    public List<string> Interests { get; set; } = [];

    /// <summary>Provider voice name, e.g. "de-DE-Wavenet-H". Null means the provider default.</summary>
    public string? VoiceName { get; set; }

    public TtsModelFamily VoiceModelFamily { get; set; } = TtsModelFamily.WaveNet;
    public decimal PlaybackSpeed { get; set; } = 1.0m;
    public DateTimeOffset UpdatedAt { get; set; }
}
