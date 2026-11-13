using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PodcastGenerator.Application.Episodes;
using PodcastGenerator.Application.Generation;
using PodcastGenerator.Application.Tts;

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
        using var leaseLost = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var heartbeat = KeepLeaseAsync(job, lease, leaseLost, heartbeatStop.Token);

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IEpisodeGenerator>().RunAsync(job.EpisodeId, leaseLost.Token);
            await FinishAsync(job, error: null);
            LogJobCompleted(logger, job.EpisodeId);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<JobLeases>().ReleaseAsync(job, CancellationToken.None);
            throw;
        }
        catch (OperationCanceledException) when (leaseLost.IsCancellationRequested)
        {
            // Another worker owns the job now; it resumes from the persisted steps. Writing anything here would clash.
            LogLeaseLost(logger, job.EpisodeId);
        }
        catch (TtsBudgetExceededException e)
        {
            LogJobPaused(logger, job.EpisodeId, e.Message);
            await FinishAsync(job, e.Message, paused: true);
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

    private async Task FinishAsync(ClaimedJob job, string? error, bool paused = false)
    {
        // Fresh scope: the pipeline's DbContext may hold half-applied changes after a failure.
        await using var scope = scopes.CreateAsyncScope();
        var episodes = scope.ServiceProvider.GetRequiredService<IEpisodeRepository>();
        var jobEntity = await episodes.GetJobAsync(job.EpisodeId, CancellationToken.None);
        var episode = await episodes.GetForGenerationAsync(job.EpisodeId, CancellationToken.None);
        if (jobEntity is null || episode is null || jobEntity.Attempts != job.Attempt)
        {
            return; // deleted while generating, or claimed by another worker after our lease expired
        }

        if (error is null)
        {
            GenerationState.Complete(jobEntity, clock.GetUtcNow());
        }
        else if (paused)
        {
            GenerationState.Pause(episode, jobEntity, error, clock.GetUtcNow());
        }
        else
        {
            GenerationState.Fail(episode, jobEntity, error, clock.GetUtcNow());
        }

        await episodes.SaveChangesAsync(CancellationToken.None);
    }

    /// <summary>
    /// Renews the lease every lease/3. A failed renewal is logged and retried on the next tick (two more chances
    /// before expiry). If the job turns out to belong to someone else, the run is cancelled.
    /// </summary>
    private async Task KeepLeaseAsync(ClaimedJob job, TimeSpan lease, CancellationTokenSource leaseLost, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(lease / 3, clock, ct);
                await using var scope = scopes.CreateAsyncScope();
                if (!await scope.ServiceProvider.GetRequiredService<JobLeases>().ExtendLeaseAsync(job, lease, ct))
                {
                    await leaseLost.CancelAsync();
                    return;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return; // job finished
            }
            catch (Exception e)
            {
                LogHeartbeatError(logger, e, job.EpisodeId);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Generating episode {EpisodeId}")]
    private static partial void LogJobStarted(ILogger logger, Guid episodeId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Episode {EpisodeId} generated")]
    private static partial void LogJobCompleted(ILogger logger, Guid episodeId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Episode {EpisodeId} failed; it can be retried")]
    private static partial void LogJobFailed(ILogger logger, Exception e, Guid episodeId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Episode {EpisodeId} paused: {Reason}")]
    private static partial void LogJobPaused(ILogger logger, Guid episodeId, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Episode {EpisodeId}: lease renewal failed, retrying")]
    private static partial void LogHeartbeatError(ILogger logger, Exception e, Guid episodeId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Episode {EpisodeId}: lease lost to another worker, stopping this run")]
    private static partial void LogLeaseLost(ILogger logger, Guid episodeId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Generation worker error")]
    private static partial void LogWorkerError(ILogger logger, Exception e);
}
