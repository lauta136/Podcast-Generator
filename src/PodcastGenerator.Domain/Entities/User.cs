namespace PodcastGenerator.Domain.Entities;

public class User
{
    public Guid Id { get; set; }
    public required string DisplayName { get; set; }

    /// <summary>Sign-in name, stored trimmed and lower-case. Null only for the seeded local user before it is claimed.</summary>
    public string? Email { get; set; }

    public string? PasswordHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public UserPreferences? Preferences { get; set; }
}
