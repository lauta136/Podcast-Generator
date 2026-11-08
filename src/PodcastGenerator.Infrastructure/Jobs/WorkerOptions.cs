namespace PodcastGenerator.Infrastructure.Jobs;

/// <summary>Bound from "Worker".</summary>
public class WorkerOptions
{
    public const string SectionName = "Worker";

    public bool Enabled { get; set; } = true;
    public int PollIntervalMs { get; set; } = 2000;

    /// <summary>A crashed worker's job becomes claimable again after this long. Renewed while the job runs.</summary>
    public int LeaseSeconds { get; set; } = 60;
}
