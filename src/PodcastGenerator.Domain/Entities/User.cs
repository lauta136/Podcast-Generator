namespace PodcastGenerator.Domain.Entities;

public class User
{
    public Guid Id { get; set; }
    public required string DisplayName { get; set; }

    /// <summary>Sign-in name, stored trimmed and lower-case. Null only for the seeded local user before it is claimed.</summary>
    public string? Email { get; set; }

    public string? PasswordHash { get; set; }

    /// <summary>
    /// Goes up when the password changes. Every sign-in token carries the version it was issued with, and tokens
    /// with an older one are refused: changing the password signs out every other device.
    /// </summary>
    public int SessionVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public UserPreferences? Preferences { get; set; }
}
