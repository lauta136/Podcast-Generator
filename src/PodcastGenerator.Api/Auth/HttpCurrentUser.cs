using Microsoft.IdentityModel.JsonWebTokens;
using PodcastGenerator.Application.Common;

namespace PodcastGenerator.Api.Auth;

/// <summary>Reads the user id from the validated token's "sub" claim.</summary>
public class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public Guid UserId =>
        Guid.TryParse(accessor.HttpContext?.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var id)
            ? id
            : throw new InvalidOperationException("No signed-in user on this request.");
}
