using PodcastGenerator.Domain.Enums;

namespace PodcastGenerator.Domain.Entities;

public class GenerationJob
{
    public Guid Id { get; set; }
    public Guid EpisodeId { get; set; }
    public JobStatus Status { get; set; } = JobStatus.Queued;

    /// <summary>Lease: a worker owns the job until this time. Expired leases are picked up again.</summary>
    public DateTimeOffset? LockedUntil { get; set; }

    public int Attempts { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
