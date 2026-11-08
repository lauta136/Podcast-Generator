using PodcastGenerator.Domain.Enums;

namespace PodcastGenerator.Domain.Entities;

/// <summary>One synthesized paragraph. Chunks are stitched into the episode MP3.</summary>
public class AudioSegment
{
    public Guid Id { get; set; }
    public Guid EpisodeId { get; set; }
    public Guid SectionId { get; set; }
    public int ParagraphIndex { get; set; }

    /// <summary>Playback order across the whole episode.</summary>
    public int Order { get; set; }

    /// <summary>hash(text + voice + model + provider). Completed chunks are never resynthesized.</summary>
    public required string CacheKey { get; set; }

    public required string Provider { get; set; }
    public required string VoiceName { get; set; }
    public TtsModelFamily ModelFamily { get; set; }
    public int Characters { get; set; }

    public string? StorageKey { get; set; }
    public double? DurationSec { get; set; }

    public StepStatus Status { get; set; } = StepStatus.Pending;
    public int Attempts { get; set; }
    public string? LastError { get; set; }
}
