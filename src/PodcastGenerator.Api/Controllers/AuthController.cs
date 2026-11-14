using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PodcastGenerator.Api.Auth;
using PodcastGenerator.Api.Dtos;
using PodcastGenerator.Application.Auth;
using PodcastGenerator.Application.Common;

namespace PodcastGenerator.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(IAuthService auth, TokenService tokens, ICurrentUser currentUser) : ControllerBase
{
    /// <summary>Creates an account and signs it in.</summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting(AuthSetup.RateLimitPolicy)]
    public async Task<UserDto> Register(CredentialsRequest request, CancellationToken ct)
    {
        var user = await auth.RegisterAsync(request.Email, request.Password, ct);
        tokens.SignIn(HttpContext, user);
        return UserDto.From(user);
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(AuthSetup.RateLimitPolicy)]
    public async Task<UserDto> Login(CredentialsRequest request, CancellationToken ct)
    {
        var user = await auth.LoginAsync(request.Email, request.Password, ct);
        tokens.SignIn(HttpContext, user);
        return UserDto.From(user);
    }

    [HttpPost("logout")]
    [AllowAnonymous] // signing out with an expired token must still clear the cookie
    public IActionResult Logout()
    {
        tokens.SignOut(HttpContext);
        return NoContent();
    }

    /// <summary>The signed-in user. The web app calls it on load to decide between the app and the sign-in screen.</summary>
    [HttpGet("me")]
    public async Task<ActionResult<UserDto>> Me(CancellationToken ct)
    {
        var user = await auth.FindAsync(currentUser.UserId, ct);
        if (user is null)
        {
            tokens.SignOut(HttpContext); // the account is gone; the token isn't
            return Unauthorized();
        }

        return UserDto.From(user);
    }
}
