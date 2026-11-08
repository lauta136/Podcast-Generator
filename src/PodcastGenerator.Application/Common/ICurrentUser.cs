namespace PodcastGenerator.Application.Common;

/// <summary>The user making the request. MVP: always the seeded local user; JWT later.</summary>
public interface ICurrentUser
{
    Guid UserId { get; }
}
