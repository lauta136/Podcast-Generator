using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PodcastGenerator.Application.Episodes;
using PodcastGenerator.Application.Generation;

namespace PodcastGenerator.Infrastructure.Jobs;

/// <summary>
/// Polls for runnable jobs and runs the pipeline. Jobs whose lease expired (crash, kill) are picked up again;
/// the pipeline skips completed steps, so nothing finished is redone.
/// </summary>
public sealed partial class GenerationWorker(
    IServiceScopeFactory scopes,
    IOptions<WorkerOptions> options,
    TimeProvider clock,
    ILogger<GenerationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = options.Value;
        if (!o.Enabled)
        {
            return;
        }

        var lease = TimeSpan.FromSeconds(o.LeaseSeconds);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await RunNextAsync(lease, stoppingToken))
                {
                    await Task.Delay(o.PollIntervalMs, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                LogWorkerError(logger, e);
                await Task.Delay(o.PollIntervalMs, stoppingToken);
            }
        }
    }

    private async Task<bool> RunNextAsync(TimeSpan lease, CancellationToken stoppingToken)
    {
        ClaimedJob? job;
        await using (var claimScope = scopes.CreateAsyncScope())
        {
            job = await claimScope.ServiceProvider.GetRequiredService<JobLeases>().TryClaimAsync(lease, stoppingToken);
        }

        if (job is null)
        {
            return false;
        }

        LogJobStarted(logger, job.EpisodeId);
        using var heartbeatStop = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var heartbeat = KeepLeaseAsync(job.JobId, lease, heartbeatStop.Token);

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IEpisodeGenerator>().RunAsync(job.EpisodeId, stoppingToken);
            await FinishAsync(job, error: null);
            LogJobCompleted(logger, job.EpisodeId);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<JobLeases>().ReleaseAsync(job.JobId, CancellationToken.None);
            throw;
        }
        catch (Exception e)
        {
            LogJobFailed(logger, e, job.EpisodeId);
            await FinishAsync(job, e.Message);
        }
        finally
        {
            await heartbeatStop.CancelAsync();
            await heartbeat;
        }

        return true;
    }

    private async Task FinishAsync(ClaimedJob job, string? error)
    {
        // Fresh scope: the pipeline's DbContext may hold half-applied changes after a failure.
        await using var scope = scopes.CreateAsyncScope();
        var episodes = scope.ServiceProvider.GetRequiredService<IEpisodeRepository>();
        var jobEntity = await episodes.GetJobAsync(job.EpisodeId, CancellationToken.None);
        var episode = await episodes.GetForGenerationAsync(job.EpisodeId, CancellationToken.None);
        if (jobEntity is null || episode is null)
        {
            return; // deleted while generating
        }

        if (error is null)
        {
            GenerationState.Complete(jobEntity, clock.GetUtcNow());
        }
        else
        {
            GenerationState.Fail(episode, jobEntity, error, clock.GetUtcNow());
        }

        await episodes.SaveChangesAsync(CancellationToken.None);
    }

    private async Task KeepLeaseAsync(Guid jobId, TimeSpan lease, CancellationToken ct)
    {
        try
        {
            while (true)
            {
                await Task.Delay(lease / 3, clock, ct);
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<JobLeases>().ExtendLeaseAsync(jobId, lease, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // job finished
        }
        catch (Exception e)
        {
            LogWorkerError(logger, e);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Generating episode {EpisodeId}")]
    private static partial void LogJobStarted(ILogger logger, Guid episodeId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Episode {EpisodeId} generated")]
    private static partial void LogJobCompleted(ILogger logger, Guid episodeId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Episode {EpisodeId} failed; it can be retried")]
    private static partial void LogJobFailed(ILogger logger, Exception e, Guid episodeId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Generation worker error")]
    private static partial void LogWorkerError(ILogger logger, Exception e);
}
