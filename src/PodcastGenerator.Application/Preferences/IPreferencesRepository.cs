using PodcastGenerator.Domain.Entities;

namespace PodcastGenerator.Application.Preferences;

public interface IPreferencesRepository
{
    Task<UserPreferences?> GetAsync(Guid userId, CancellationToken ct);
    void Add(UserPreferences preferences);
    Task SaveChangesAsync(CancellationToken ct);
}
