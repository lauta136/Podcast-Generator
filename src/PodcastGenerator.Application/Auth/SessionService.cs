using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using PodcastGenerator.Domain.Entities;

namespace PodcastGenerator.Application.Auth;

/// <param name="Token">The refresh token for the cookie. Never stored: only its hash is.</param>
public sealed record IssuedRefreshToken(string Token, DateTimeOffset ExpiresAt);

/// <param name="Rotated">The replacement refresh token; null when a just-used token was presented again in the grace window.</param>
public sealed record RefreshResult(User User, IssuedRefreshToken? Rotated);

/// <summary>The refresh token is unknown, expired, revoked, or was reused: the sign-in is over.</summary>
public class SessionEndedException(string message) : Exception(message);

public interface ISessionService
{
    /// <summary>Starts a sign-in on one device (a new token family).</summary>
    Task<IssuedRefreshToken> StartAsync(User user, CancellationToken ct);

    /// <summary>Exchanges a refresh token for a new one (rotation) and returns its user for a new access token.</summary>
    Task<RefreshResult> RefreshAsync(string token, CancellationToken ct);

    /// <summary>Ends the sign-in this token belongs to. Unknown tokens are ignored.</summary>
    Task EndAsync(string? token, CancellationToken ct);

    /// <summary>Ends every sign-in of the user (password change).</summary>
    Task EndAllAsync(Guid userId, CancellationToken ct);
}

public class SessionService(
    IRefreshTokenRepository tokens,
    IUserRepository users,
    IOptions<SessionOptions> options,
    TimeProvider clock) : ISessionService
{
    public static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public async Task<IssuedRefreshToken> StartAsync(User user, CancellationToken ct)
    {
        var issued = Issue(user.Id, Guid.NewGuid());
        await tokens.SaveChangesAsync(ct);
        return issued;
    }

    public async Task<RefreshResult> RefreshAsync(string token, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var stored = await tokens.GetByHashAsync(Hash(token), ct) ?? throw new SessionEndedException("Unknown sign-in.");
        if (stored.RevokedAt is not null || stored.ExpiresAt <= now)
        {
            throw new SessionEndedException("The sign-in has ended.");
        }

        var user = await users.GetAsync(stored.UserId, ct) ?? throw new SessionEndedException("The account no longer exists.");

        if (stored.UsedAt is null && await tokens.TryMarkUsedAsync(stored.Id, now, ct))
        {
            var rotated = Issue(stored.UserId, stored.FamilyId);
            await tokens.SaveChangesAsync(ct);
            return new RefreshResult(user, rotated);
        }

        // Used before. Moments ago: two tabs refreshed together, so only renew the access token.
        var usedAt = stored.UsedAt ?? now;
        if (now - usedAt <= TimeSpan.FromSeconds(options.Value.ReuseGraceSeconds))
        {
            return new RefreshResult(user, Rotated: null);
        }

        // Long ago: a copy of an old token is in use. End the whole sign-in, for the thief and the owner alike.
        await tokens.RevokeFamilyAsync(stored.FamilyId, now, ct);
        throw new SessionEndedException("This sign-in was used from somewhere else and has been ended.");
    }

    public async Task EndAsync(string? token, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(token) || await tokens.GetByHashAsync(Hash(token), ct) is not { } stored)
        {
            return;
        }

        await tokens.RevokeFamilyAsync(stored.FamilyId, clock.GetUtcNow(), ct);
    }

    public Task EndAllAsync(Guid userId, CancellationToken ct) => tokens.RevokeAllAsync(userId, clock.GetUtcNow(), ct);

    private IssuedRefreshToken Issue(Guid userId, Guid familyId)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var now = clock.GetUtcNow();
        var expires = now.AddDays(options.Value.RefreshTokenDays);
        tokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FamilyId = familyId,
            TokenHash = Hash(token),
            CreatedAt = now,
            ExpiresAt = expires,
        });
        return new IssuedRefreshToken(token, expires);
    }
}
