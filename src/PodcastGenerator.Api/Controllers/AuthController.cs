using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using PodcastGenerator.Api.Auth;
using PodcastGenerator.Api.Dtos;
using PodcastGenerator.Application.Auth;
using PodcastGenerator.Application.Common;

namespace PodcastGenerator.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(
    IAuthService auth,
    ISessionService sessions,
    TokenService tokens,
    ICurrentUser currentUser,
    IOptions<RegistrationOptions> registration) : ControllerBase
{
    /// <summary>Whether sign-up needs an invite code, so the form can ask for one. Never reveals the code.</summary>
    [HttpGet("registration")]
    [AllowAnonymous]
    public RegistrationDto Registration() => new(registration.Value.InviteRequired);

    /// <summary>Creates an account and signs it in.</summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting(AuthSetup.RateLimitPolicy)]
    public async Task<UserDto> Register(RegisterRequest request, CancellationToken ct)
    {
        var user = await auth.RegisterAsync(request.Email, request.Password, request.InviteCode, ct);
        tokens.SignIn(HttpContext, user, await sessions.StartAsync(user, ct));
        return UserDto.From(user);
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(AuthSetup.RateLimitPolicy)]
    public async Task<UserDto> Login(CredentialsRequest request, CancellationToken ct)
    {
        var user = await auth.LoginAsync(request.Email, request.Password, ct);
        tokens.SignIn(HttpContext, user, await sessions.StartAsync(user, ct));
        return UserDto.From(user);
    }

    /// <summary>
    /// Exchanges the refresh cookie for a new access token and a new refresh token. The web app calls it before
    /// the 15-minute access token runs out, and after any 401.
    /// </summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<ActionResult<UserDto>> Refresh(CancellationToken ct)
    {
        var token = tokens.RefreshTokenFrom(HttpContext);
        if (token is null)
        {
            return Unauthorized();
        }

        try
        {
            var result = await sessions.RefreshAsync(token, ct);
            tokens.SignIn(HttpContext, result.User, result.Rotated);
            return UserDto.From(result.User);
        }
        catch (SessionEndedException)
        {
            tokens.SignOut(HttpContext);
            return Unauthorized();
        }
    }

    [HttpPost("logout")]
    [AllowAnonymous] // signing out with an expired access token must still end the sign-in and clear the cookies
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        await sessions.EndAsync(tokens.RefreshTokenFrom(HttpContext), ct);
        tokens.SignOut(HttpContext);
        return NoContent();
    }

    /// <summary>
    /// Changes the password and ends every sign-in: other devices are out once their access token expires
    /// (at most 15 minutes). This device gets a fresh sign-in.
    /// </summary>
    [HttpPut("password")]
    [EnableRateLimiting(AuthSetup.RateLimitPolicy)]
    public async Task<UserDto> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        var user = await auth.ChangePasswordAsync(currentUser.UserId, request.CurrentPassword, request.NewPassword, ct);
        await sessions.EndAllAsync(user.Id, ct);
        tokens.SignIn(HttpContext, user, await sessions.StartAsync(user, ct));
        return UserDto.From(user);
    }

    /// <summary>Deletes the account with all of its data, audio and sign-ins, and signs out.</summary>
    [HttpDelete("account")]
    [EnableRateLimiting(AuthSetup.RateLimitPolicy)]
    public async Task<IActionResult> DeleteAccount(DeleteAccountRequest request, CancellationToken ct)
    {
        await auth.DeleteAccountAsync(currentUser.UserId, request.Password, ct);
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
            tokens.SignOut(HttpContext); // the account is gone; the access token isn't yet
            return Unauthorized();
        }

        return UserDto.From(user);
    }
}
