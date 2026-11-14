using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using PodcastGenerator.Domain.Entities;

namespace PodcastGenerator.Api.Auth;

/// <summary>
/// Issues the signed token and carries it in an HttpOnly cookie. A cookie, not a header set by JavaScript, because
/// the browser's audio element can't send headers, and page scripts can't read (or leak) an HttpOnly cookie.
/// SameSite=Strict keeps other sites from sending it, which is the CSRF protection.
/// </summary>
public class TokenService(IOptions<JwtOptions> options, TimeProvider clock)
{
    public static SymmetricSecurityKey KeyFrom(JwtOptions o) => new(Encoding.UTF8.GetBytes(o.SigningKey));

    public (string Token, DateTimeOffset Expires) Create(User user)
    {
        var o = options.Value;
        var now = clock.GetUtcNow();
        var expires = now.AddHours(o.LifetimeHours);
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

    public void SignIn(HttpContext http, User user)
    {
        var (token, expires) = Create(user);
        http.Response.Cookies.Append(options.Value.CookieName, token, CookieFor(http, expires));
    }

    public void SignOut(HttpContext http) =>
        http.Response.Cookies.Delete(options.Value.CookieName, CookieFor(http, expires: null));

    private static CookieOptions CookieFor(HttpContext http, DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Strict,
        Secure = http.Request.IsHttps, // local development runs on http://localhost
        Path = "/api",
        Expires = expires,
        IsEssential = true,
    };
}
