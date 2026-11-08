using PodcastGenerator.Domain.Enums;

namespace PodcastGenerator.Domain.Entities;

public class Episode
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    public Guid? SeasonId { get; set; }
    public int? SeasonOrder { get; set; }

    public required string Topic { get; set; }
    public string LanguageCode { get; set; } = "de";
    public CefrLevel Level { get; set; }
    public EpisodeStyle Style { get; set; }
    public int DurationMin { get; set; }
    public int TargetWords { get; set; }

    public string? VoiceName { get; set; }
    public TtsModelFamily VoiceModelFamily { get; set; }

    // Filled by the outline step.
    public string? Title { get; set; }
    public string? Description { get; set; }

    /// <summary>Short summary used as context for later episodes in a season.</summary>
    public string? Summary { get; set; }

    public EpisodeStatus Status { get; set; } = EpisodeStatus.Pending;
    public string? LastError { get; set; }
    public DifficultyRating? DifficultyRating { get; set; }

    /// <summary>Storage key of the stitched episode MP3, set when audio is complete.</summary>
    public string? AudioStorageKey { get; set; }

    public double? AudioDurationSec { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Season? Season { get; set; }
    public List<EpisodeSection> Sections { get; set; } = [];
    public List<AudioSegment> AudioSegments { get; set; } = [];
}
