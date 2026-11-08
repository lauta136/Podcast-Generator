using PodcastGenerator.Domain.Entities;
using PodcastGenerator.Domain.Enums;

namespace PodcastGenerator.Application.Generation;

/// <summary>Status transitions for an episode and its job, kept in one place so they're testable.</summary>
public static class GenerationState
{
    public const int MaxErrorLength = 1000;

    public static void Requeue(Episode episode, GenerationJob job, DateTimeOffset now)
    {
        foreach (var section in episode.Sections.Where(s => s.Status == StepStatus.Failed))
        {
            section.Status = StepStatus.Pending;
            section.LastError = null;
        }

        episode.Status = EpisodeStatus.Pending;
        episode.LastError = null;
        episode.UpdatedAt = now;

        job.Status = JobStatus.Queued;
        job.LockedUntil = null;
        job.LastError = null;
        job.UpdatedAt = now;
    }

    public static void Fail(Episode episode, GenerationJob job, string error, DateTimeOffset now)
    {
        var message = Truncate(error);
        episode.Status = EpisodeStatus.Failed;
        episode.LastError = message;
        episode.UpdatedAt = now;

        job.Status = JobStatus.Failed;
        job.LockedUntil = null;
        job.LastError = message;
        job.UpdatedAt = now;
    }

    public static void Complete(GenerationJob job, DateTimeOffset now)
    {
        job.Status = JobStatus.Completed;
        job.LockedUntil = null;
        job.LastError = null;
        job.UpdatedAt = now;
    }

    public static string Truncate(string error) => error.Length <= MaxErrorLength ? error : error[..MaxErrorLength];
}
