namespace PodcastGenerator.Application.Common;

/// <summary>The signed-in user making the request, taken from the validated token.</summary>
public interface ICurrentUser
{
    Guid UserId { get; }
}
