using PodcastGenerator.Application.Common;
using PodcastGenerator.Infrastructure.Persistence;

namespace PodcastGenerator.Infrastructure.Identity;

/// <summary>MVP identity: every request belongs to the seeded local user.</summary>
public class LocalCurrentUser : ICurrentUser
{
    public Guid UserId => SeedData.LocalUserId;
}
