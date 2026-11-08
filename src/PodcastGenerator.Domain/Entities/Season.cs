using PodcastGenerator.Domain.Enums;

namespace PodcastGenerator.Domain.Entities;

/// <summary>A group of episodes on one big topic. Post-MVP: schema only, no API yet.</summary>
public class Season
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public required string Title { get; set; }
    public required string Topic { get; set; }
    public CefrLevel Level { get; set; }

    /// <summary>Planned episode arc as JSON, produced by the LLM when the season is created.</summary>
    public string? ArcJson { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public List<Episode> Episodes { get; set; } = [];
}
