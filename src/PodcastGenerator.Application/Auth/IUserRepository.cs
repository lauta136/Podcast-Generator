using PodcastGenerator.Domain.Entities;

namespace PodcastGenerator.Application.Auth;

public interface IUserRepository
{
    Task<User?> GetAsync(Guid userId, CancellationToken ct);

    Task<User?> GetByEmailAsync(string normalizedEmail, CancellationToken ct);

    /// <summary>
    /// Gives the seeded local user (the single user from before sign-in existed) to the first account, so its
    /// episodes are kept. Atomic: of two simultaneous first registrations only one gets it. Null if already claimed.
    /// </summary>
    Task<User?> TryClaimLocalUserAsync(string normalizedEmail, string passwordHash, string displayName, CancellationToken ct);

    /// <summary>False if the email is already registered.</summary>
    Task<bool> TryAddAsync(User user, CancellationToken ct);

    /// <summary>Storage keys of all of the user's audio (chunks and stitched episodes).</summary>
    Task<IReadOnlyList<string>> AudioKeysAsync(Guid userId, CancellationToken ct);

    Task<bool> HasEpisodesInProgressAsync(Guid userId, CancellationToken ct);

    /// <summary>Deletes the user; the database cascades to preferences, seasons, episodes and listening progress.</summary>
    void Remove(User user);

    Task SaveChangesAsync(CancellationToken ct);
}
