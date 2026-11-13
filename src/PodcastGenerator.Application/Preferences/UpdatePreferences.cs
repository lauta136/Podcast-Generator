using PodcastGenerator.Domain.Enums;

namespace PodcastGenerator.Application.Preferences;

public sealed record UpdatePreferences(
    string NativeLanguage,
    string TargetLanguage,
    CefrLevel DefaultLevel,
    int DefaultDurationMin,
    IReadOnlyList<string> Interests,
    string? VoiceName,
    TtsModelFamily VoiceModelFamily,
    decimal PlaybackSpeed,
    decimal? SpeakingRate = null);
