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

    Task SaveChangesAsync(CancellationToken ct);
}
