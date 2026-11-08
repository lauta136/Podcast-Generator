namespace PodcastGenerator.Infrastructure.Persistence;

public static class SeedData
{
    /// <summary>The single local user of the MVP. Replaced by real accounts with JWT auth.</summary>
    public static readonly Guid LocalUserId = new("00000000-0000-0000-0000-000000000001");
}
