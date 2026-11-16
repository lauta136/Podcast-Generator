namespace PodcastGenerator.Domain.Entities;

/// <summary>
/// One link in a sign-in's chain of refresh tokens. Only a hash is stored. Each use replaces the token with a new
/// one in the same family; a token used twice means it was copied, and the whole family (that sign-in) is revoked.
/// </summary>
public class RefreshToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    /// <summary>One sign-in on one device: every rotation keeps the family.</summary>
    public Guid FamilyId { get; set; }

    /// <summary>SHA-256 of the token, hex. The token itself is only ever in the user's cookie.</summary>
    public required string TokenHash { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>When it was exchanged for a new one. A second use after this is a reuse.</summary>
    public DateTimeOffset? UsedAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }
}
