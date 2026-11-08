using PodcastGenerator.Application.Generation;
using PodcastGenerator.Domain.Entities;
using PodcastGenerator.Domain.Enums;

namespace PodcastGenerator.UnitTests;

public class GenerationStateTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

    [Fact]
    public void Requeue_resets_only_failed_sections()
    {
        var done = new EpisodeSection { Title = "a", Summary = "a", Status = StepStatus.Completed, Attempts = 1 };
        var failed = new EpisodeSection { Title = "b", Summary = "b", Status = StepStatus.Failed, Attempts = 2, LastError = "boom" };
        var episode = new Episode { Topic = "t", Status = EpisodeStatus.Failed, LastError = "boom", Sections = [done, failed] };
        var job = new GenerationJob { Status = JobStatus.Failed, LastError = "boom" };

        GenerationState.Requeue(episode, job, Now);

        Assert.Equal(StepStatus.Completed, done.Status);
        Assert.Equal(StepStatus.Pending, failed.Status);
        Assert.Null(failed.LastError);
        Assert.Equal(2, failed.Attempts); // history kept
        Assert.Equal(EpisodeStatus.Pending, episode.Status);
        Assert.Equal(JobStatus.Queued, job.Status);
        Assert.Null(job.LockedUntil);
    }

    [Fact]
    public void Fail_marks_episode_and_job_and_truncates_the_error()
    {
        var episode = new Episode { Topic = "t", Status = EpisodeStatus.GeneratingSections };
        var job = new GenerationJob { Status = JobStatus.Running, LockedUntil = Now };

        GenerationState.Fail(episode, job, new string('x', 5000), Now);

        Assert.Equal(EpisodeStatus.Failed, episode.Status);
        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Null(job.LockedUntil);
        Assert.Equal(GenerationState.MaxErrorLength, episode.LastError!.Length);
    }
}
