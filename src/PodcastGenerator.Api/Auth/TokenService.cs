using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using PodcastGenerator.Application.Auth;
using PodcastGenerator.Domain.Entities;

namespace PodcastGenerator.Api.Auth;

/// <summary>
/// Two HttpOnly, SameSite=Strict cookies. The access token (a 15-minute JWT, checked by signature only) goes to
/// all of /api; the refresh token (random, stored hashed) only to /api/auth. Cookies rather than headers set by
/// JavaScript, because the audio element can't send headers and scripts can't read (or leak) HttpOnly cookies.
/// SameSite=Strict keeps other sites from sending them, which is the CSRF protection.
/// </summary>
public class TokenService(IOptions<JwtOptions> options, TimeProvider clock)
{
    public static SymmetricSecurityKey KeyFrom(JwtOptions o) => new(Encoding.UTF8.GetBytes(o.SigningKey));

    /// <summary>The access token. Its only claim is the user id: everything else is read where it lives.</summary>
    public (string Token, DateTimeOffset Expires) Create(User user)
    {
        var o = options.Value;
        var now = clock.GetUtcNow();
        var expires = now.AddMinutes(o.AccessTokenMinutes);
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = o.Issuer,
            Audience = o.Audience,
            Subject = new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString())]),
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(KeyFrom(o), SecurityAlgorithms.HmacSha256),
        });
        return (token, expires);
    }

    /// <summary>A new access token and, unless it's only a renewal in the grace window, a new refresh token.</summary>
    public void SignIn(HttpContext http, User user, IssuedRefreshToken? refresh)
    {
        var o = options.Value;
        var (token, expires) = Create(user);
        http.Response.Cookies.Append(o.CookieName, token, CookieFor(http, "/api", expires));
        if (refresh is not null)
        {
            http.Response.Cookies.Append(o.RefreshCookieName, refresh.Token, CookieFor(http, "/api/auth", refresh.ExpiresAt));
        }
    }

    public void SignOut(HttpContext http)
    {
        var o = options.Value;
        http.Response.Cookies.Delete(o.CookieName, CookieFor(http, "/api", expires: null));
        http.Response.Cookies.Delete(o.RefreshCookieName, CookieFor(http, "/api/auth", expires: null));
    }

    public string? RefreshTokenFrom(HttpContext http) =>
        http.Request.Cookies.TryGetValue(options.Value.RefreshCookieName, out var token) ? token : null;

    private static CookieOptions CookieFor(HttpContext http, string path, DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Strict,
        Secure = http.Request.IsHttps, // local development runs on http://localhost
        Path = path,
        Expires = expires,
        IsEssential = true,
    };
}
