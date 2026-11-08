namespace PodcastGenerator.Domain.Entities;

public class User
{
    public Guid Id { get; set; }
    public required string DisplayName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public UserPreferences? Preferences { get; set; }
}
