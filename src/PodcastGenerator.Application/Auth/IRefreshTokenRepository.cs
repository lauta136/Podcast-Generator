using PodcastGenerator.Domain.Entities;

namespace PodcastGenerator.Application.Auth;

public interface IRefreshTokenRepository
{
    void Add(RefreshToken token);

    Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken ct);

    /// <summary>Marks the token used if nobody did yet (one conditional UPDATE). False if it was already used.</summary>
    Task<bool> TryMarkUsedAsync(Guid tokenId, DateTimeOffset now, CancellationToken ct);

    Task RevokeFamilyAsync(Guid familyId, DateTimeOffset now, CancellationToken ct);

    Task RevokeAllAsync(Guid userId, DateTimeOffset now, CancellationToken ct);

    Task SaveChangesAsync(CancellationToken ct);
}
