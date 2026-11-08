using Microsoft.EntityFrameworkCore;
using PodcastGenerator.Application.Preferences;
using PodcastGenerator.Domain.Entities;

namespace PodcastGenerator.Infrastructure.Persistence.Repositories;

public class PreferencesRepository(AppDbContext db) : IPreferencesRepository
{
    public Task<UserPreferences?> GetAsync(Guid userId, CancellationToken ct) =>
        db.UserPreferences.SingleOrDefaultAsync(p => p.UserId == userId, ct);

    public void Add(UserPreferences preferences) => db.UserPreferences.Add(preferences);

    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
