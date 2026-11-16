namespace PodcastGenerator.Application.Auth;

/// <summary>Bound from "Auth:Sessions".</summary>
public class SessionOptions
{
    public const string SectionName = "Auth:Sessions";

    /// <summary>A sign-in lasts this long without use; every refresh starts the period again.</summary>
    public int RefreshTokenDays { get; set; } = 30;

    /// <summary>
    /// A just-used token presented again within this window is a race (two tabs refreshing at once), not theft:
    /// it gets a new access token but no new refresh token. Later reuse revokes the sign-in.
    /// </summary>
    public int ReuseGraceSeconds { get; set; } = 30;
}
