using Microsoft.EntityFrameworkCore;
using Npgsql;
using PodcastGenerator.Application.Common;
using PodcastGenerator.Application.Preferences;
using PodcastGenerator.Domain.Entities;

namespace PodcastGenerator.Infrastructure.Persistence.Repositories;

public class PreferencesRepository(AppDbContext db) : IPreferencesRepository
{
    public Task<UserPreferences?> GetAsync(Guid userId, CancellationToken ct) =>
        db.UserPreferences.SingleOrDefaultAsync(p => p.UserId == userId, ct);

    public async Task<bool> TryAddAsync(UserPreferences preferences, CancellationToken ct)
    {
        var entry = db.UserPreferences.Add(preferences);
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
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        {
            // No user row: the account was deleted while this access token was still valid.
            entry.State = EntityState.Detached;
            throw new AccountGoneException();
        }
    }

    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
