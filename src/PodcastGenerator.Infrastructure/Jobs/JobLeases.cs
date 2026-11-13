using Microsoft.EntityFrameworkCore;
using Npgsql;
using PodcastGenerator.Infrastructure.Persistence;

namespace PodcastGenerator.Infrastructure.Jobs;

/// <param name="Attempt">The job's attempt counter after this claim. It identifies the owner: a later claim by
/// another worker increments it, so lease writes from a worker that lost the job match no row.</param>
public sealed record ClaimedJob(Guid JobId, Guid EpisodeId, int Attempt);

/// <summary>Lease-based job claiming on PostgreSQL. SKIP LOCKED keeps two workers off the same job.</summary>
public class JobLeases(AppDbContext db, TimeProvider clock)
{
    public async Task<ClaimedJob?> TryClaimAsync(TimeSpan lease, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        const string sql = """
            UPDATE "GenerationJobs"
            SET "Status" = 'Running', "LockedUntil" = @until, "Attempts" = "Attempts" + 1, "UpdatedAt" = @now
            WHERE "Id" = (
                SELECT "Id" FROM "GenerationJobs"
                WHERE "Status" = 'Queued' OR ("Status" = 'Running' AND "LockedUntil" < @now)
                ORDER BY "CreatedAt"
                LIMIT 1
                FOR UPDATE SKIP LOCKED)
            RETURNING "Id", "EpisodeId", "Attempts"
            """;

        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("until", now + lease);
        command.Parameters.AddWithValue("now", now);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? new ClaimedJob(reader.GetGuid(0), reader.GetGuid(1), reader.GetInt32(2)) : null;
    }

    /// <summary>Returns false when the job is no longer ours (claimed again after our lease expired, or finished).</summary>
    public async Task<bool> ExtendLeaseAsync(ClaimedJob job, TimeSpan lease, CancellationToken ct)
    {
        var until = clock.GetUtcNow() + lease;
        return await db.GenerationJobs
            .Where(j => j.Id == job.JobId && j.Attempts == job.Attempt && j.Status == Domain.Enums.JobStatus.Running)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.LockedUntil, until), ct) == 1;
    }

    /// <summary>Graceful shutdown: hand the job back immediately instead of waiting for the lease to expire.</summary>
    public Task ReleaseAsync(ClaimedJob job, CancellationToken ct) =>
        db.GenerationJobs
            .Where(j => j.Id == job.JobId && j.Attempts == job.Attempt)
            .ExecuteUpdateAsync(
                s => s.SetProperty(j => j.Status, Domain.Enums.JobStatus.Queued).SetProperty(j => j.LockedUntil, (DateTimeOffset?)null),
                ct);
}
