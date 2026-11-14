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

    /// <summary>How long a sign-in lasts.</summary>
    public int LifetimeHours { get; set; } = 168;

    public string CookieName { get; set; } = "pg_auth";
}
