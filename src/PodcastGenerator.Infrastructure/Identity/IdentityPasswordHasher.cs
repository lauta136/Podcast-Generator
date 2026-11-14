using Microsoft.AspNetCore.Identity;
using PodcastGenerator.Application.Auth;
using PodcastGenerator.Domain.Entities;

namespace PodcastGenerator.Infrastructure.Identity;

/// <summary>ASP.NET Core Identity's hasher (PBKDF2-HMAC-SHA512, per-password salt), without the rest of Identity.</summary>
public class IdentityPasswordHasher : IPasswordHasher
{
    private static readonly User Unused = new() { DisplayName = "" }; // the hasher ignores the user argument
    private readonly PasswordHasher<User> _hasher = new();

    public string Hash(string password) => _hasher.HashPassword(Unused, password);

    public PasswordCheck Verify(string hash, string password)
    {
        try
        {
            return _hasher.VerifyHashedPassword(Unused, hash, password) switch
            {
                PasswordVerificationResult.Success => PasswordCheck.Correct,
                PasswordVerificationResult.SuccessRehashNeeded => PasswordCheck.CorrectNeedsRehash,
                _ => PasswordCheck.Wrong,
            };
        }
        catch (FormatException)
        {
            return PasswordCheck.Wrong; // a corrupt stored hash must not become a 500
        }
    }
}
