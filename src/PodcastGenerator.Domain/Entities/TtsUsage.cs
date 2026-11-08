using PodcastGenerator.Domain.Enums;

namespace PodcastGenerator.Domain.Entities;

/// <summary>Characters sent to the TTS provider per model family per calendar month (project-wide).</summary>
public class TtsUsage
{
    public TtsModelFamily ModelFamily { get; set; }

    /// <summary>First day of the month (UTC).</summary>
    public DateOnly Month { get; set; }

    public long Characters { get; set; }
}
