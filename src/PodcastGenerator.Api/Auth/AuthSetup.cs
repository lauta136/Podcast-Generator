using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PodcastGenerator.Application.Common;

namespace PodcastGenerator.Api.Auth;

public static partial class AuthSetup
{
    public const string RateLimitPolicy = "auth";

    public static void WarnIfTemporarySigningKey(this WebApplication app)
    {
        if (app.Configuration.GetValue<bool>("Auth:Jwt:Ephemeral"))
        {
            LogTemporaryKey(app.Logger);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Auth:Jwt:SigningKey is not set: using a temporary key, so sign-ins end when the API restarts. See README.")]
    private static partial void LogTemporaryKey(ILogger logger);

    /// <summary>
    /// JWT validation (from the cookie, or an Authorization header for API clients), sign-in required on every
    /// endpoint unless it opts out, and a per-address limit on sign-in attempts.
    /// </summary>
    public static void AddAppAuth(this WebApplicationBuilder builder)
    {
        var section = builder.Configuration.GetSection(JwtOptions.SectionName);
        if (string.IsNullOrEmpty(section["SigningKey"]) && builder.Environment.IsDevelopment())
        {
            // Convenience for a first run. Sign-ins end when the API restarts; set the secret to keep them.
            section["SigningKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
            builder.Configuration["Auth:Jwt:Ephemeral"] = "true";
        }

        builder.Services.AddOptions<JwtOptions>()
            .Bind(section)
            .Validate(
                o => Encoding.UTF8.GetByteCount(o.SigningKey) >= JwtOptions.MinKeyBytes,
                $"Auth:Jwt:SigningKey must be set to at least {JwtOptions.MinKeyBytes} random bytes (see README).")
            .ValidateOnStart();

        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
        builder.Services.AddSingleton<TokenService>();

        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwt) =>
            {
                var o = jwt.Value;
                bearer.MapInboundClaims = false; // keep "sub" as "sub"
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = o.Issuer,
                    ValidAudience = o.Audience,
                    IssuerSigningKey = TokenService.KeyFrom(o),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
                bearer.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        if (!context.Request.Headers.ContainsKey("Authorization")
                            && context.Request.Cookies.TryGetValue(o.CookieName, out var token))
                        {
                            context.Token = token;
                        }

                        return Task.CompletedTask;
                    },
                };
            });

        // Secure by default: a new endpoint needs sign-in unless it says [AllowAnonymous].
        builder.Services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        var attempts = builder.Configuration.GetValue("Auth:MaxAttemptsPerMinute", 10);
        builder.Services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.AddPolicy(RateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
                http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = attempts, Window = TimeSpan.FromMinutes(1) }));
        });
    }
}
