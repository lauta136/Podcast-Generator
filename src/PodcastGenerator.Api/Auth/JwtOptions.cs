namespace PodcastGenerator.Api.Auth;

/// <summary>Bound from "Auth:Jwt". The signing key is a secret: user-secrets locally, an environment variable elsewhere.</summary>
public class JwtOptions
{
    public const string SectionName = "Auth:Jwt";
    public const int MinKeyBytes = 32; // HS256 needs at least 256 bits

    /// <summary>Random text of at least 32 bytes. Anyone who has it can sign in as anyone.</summary>
    public string SigningKey { get; set; } = "";

    public string Issuer { get; set; } = "podcast-generator";
    public string Audience { get; set; } = "podcast-generator-web";

    /// <summary>
    /// Life of an access token. It is checked by signature only, so this is also the longest a signed-out device
    /// (password change, deleted account) can keep going.
    /// </summary>
    public int AccessTokenMinutes { get; set; } = 15;

    public string CookieName { get; set; } = "pg_auth";

    /// <summary>Sent only to /api/auth, so the long-lived token isn't attached to every request.</summary>
    public string RefreshCookieName { get; set; } = "pg_refresh";
}
