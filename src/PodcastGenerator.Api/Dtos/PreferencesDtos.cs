using System.ComponentModel.DataAnnotations;
using PodcastGenerator.Domain.Entities;
using PodcastGenerator.Domain.Enums;

namespace PodcastGenerator.Api.Dtos;

public sealed record PreferencesDto(
    string NativeLanguage,
    string TargetLanguage,
    CefrLevel DefaultLevel,
    int DefaultDurationMin,
    IReadOnlyList<string> Interests,
    string? VoiceName,
    TtsModelFamily VoiceModelFamily,
    decimal PlaybackSpeed,
    DateTimeOffset UpdatedAt)
{
    public static PreferencesDto From(UserPreferences p) => new(
        p.NativeLanguage,
        p.TargetLanguage,
        p.DefaultLevel,
        p.DefaultDurationMin,
        p.Interests,
        p.VoiceName,
        p.VoiceModelFamily,
        p.PlaybackSpeed,
        p.UpdatedAt);
}

public sealed class UpdatePreferencesRequest
{
    [Required, StringLength(8, MinimumLength = 2)]
    public string NativeLanguage { get; init; } = "";

    [Required, StringLength(8, MinimumLength = 2)]
    public string TargetLanguage { get; init; } = "";

    [EnumDataType(typeof(CefrLevel))]
    public CefrLevel DefaultLevel { get; init; }

    public int DefaultDurationMin { get; init; }

    [MaxLength(20)]
    public List<string> Interests { get; init; } = [];

    [StringLength(100)]
    public string? VoiceName { get; init; }

    [EnumDataType(typeof(TtsModelFamily))]
    public TtsModelFamily VoiceModelFamily { get; init; }

    [Range(typeof(decimal), "0.75", "2.0", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    public decimal PlaybackSpeed { get; init; } = 1.0m;
}
