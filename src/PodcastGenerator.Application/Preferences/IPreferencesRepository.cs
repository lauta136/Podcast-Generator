using PodcastGenerator.Domain.Entities;

namespace PodcastGenerator.Application.Preferences;

public interface IPreferencesRepository
{
    Task<UserPreferences?> GetAsync(Guid userId, CancellationToken ct);
    /// <summary>Inserts the row; false if another request inserted preferences for the same user first.</summary>
    Task<bool> TryAddAsync(UserPreferences preferences, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
