using Microsoft.EntityFrameworkCore;
using Npgsql;
using PodcastGenerator.Application.Auth;
using PodcastGenerator.Domain.Entities;
using PodcastGenerator.Domain.Enums;

namespace PodcastGenerator.Infrastructure.Persistence.Repositories;

public class UserRepository(AppDbContext db) : IUserRepository
{
    public Task<User?> GetAsync(Guid userId, CancellationToken ct) =>
        db.Users.SingleOrDefaultAsync(u => u.Id == userId, ct);

    public Task<User?> GetByEmailAsync(string normalizedEmail, CancellationToken ct) =>
        db.Users.SingleOrDefaultAsync(u => u.Email == normalizedEmail, ct);

    public async Task<User?> TryClaimLocalUserAsync(string normalizedEmail, string passwordHash, string displayName, CancellationToken ct)
    {
        try
        {
            // One conditional UPDATE: only the first caller finds the row unclaimed.
            var claimed = await db.Users
                .Where(u => u.Id == SeedData.LocalUserId && u.Email == null)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(u => u.Email, normalizedEmail)
                        .SetProperty(u => u.PasswordHash, passwordHash)
                        .SetProperty(u => u.DisplayName, displayName),
                    ct);
            return claimed == 1 ? await db.Users.SingleAsync(u => u.Id == SeedData.LocalUserId, ct) : null;
        }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return null;
        }
    }

    public async Task<bool> TryAddAsync(User user, CancellationToken ct)
    {
        var entry = db.Users.Add(user);
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            entry.State = EntityState.Detached;
            return false;
        }
    }

    public async Task<IReadOnlyList<string>> AudioKeysAsync(Guid userId, CancellationToken ct)
    {
        var chunks = await db.AudioSegments
            .Where(a => a.StorageKey != null && db.Episodes.Any(e => e.Id == a.EpisodeId && e.UserId == userId))
            .Select(a => a.StorageKey!)
            .ToListAsync(ct);
        var stitched = await db.Episodes
            .Where(e => e.UserId == userId && e.AudioStorageKey != null)
            .Select(e => e.AudioStorageKey!)
            .ToListAsync(ct);
        return [.. chunks, .. stitched];
    }

    public Task<bool> HasEpisodesInProgressAsync(Guid userId, CancellationToken ct) =>
        db.Episodes.AnyAsync(e => e.UserId == userId && (e.Status == EpisodeStatus.Pending || e.Status == EpisodeStatus.GeneratingOutline
            || e.Status == EpisodeStatus.GeneratingSections || e.Status == EpisodeStatus.GeneratingAudio), ct);

    public void Remove(User user) => db.Users.Remove(user);

    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
