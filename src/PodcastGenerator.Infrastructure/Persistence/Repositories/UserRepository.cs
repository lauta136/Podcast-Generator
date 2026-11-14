using Microsoft.EntityFrameworkCore;
using Npgsql;
using PodcastGenerator.Application.Auth;
using PodcastGenerator.Domain.Entities;

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

    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
