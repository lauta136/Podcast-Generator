using PodcastGenerator.Domain.Enums;

namespace PodcastGenerator.Domain.Entities;

/// <summary>
/// A series of connected episodes on one big topic. The arc (one line per episode) is planned by the LLM in the
/// first episode's job; later episodes are generated one at a time from it and from the earlier episodes' summaries.
/// </summary>
public class Season
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    /// <summary>The topic until the arc is planned, then the series title from the arc.</summary>
    public required string Title { get; set; }

    public string? Description { get; set; }
    public required string Topic { get; set; }
    public CefrLevel Level { get; set; }
    public EpisodeStyle Style { get; set; }
    public int EpisodeCount { get; set; }

    /// <summary>Settings every episode of the season is generated with.</summary>
    public int DurationMin { get; set; }

    /// <summary>Null: the preferences or the level default when each episode is created.</summary>
    public decimal? SpeakingRate { get; set; }

    /// <summary>Planned arc as JSON (title, description, one entry per episode), null until planned.</summary>
    public string? ArcJson { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public List<Episode> Episodes { get; set; } = [];
}
