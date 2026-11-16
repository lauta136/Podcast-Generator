using PodcastGenerator.Application.Common;
using PodcastGenerator.Application.Tts;
using PodcastGenerator.Domain.Entities;

namespace PodcastGenerator.Application.Auth;

public interface IAuthService
{
    Task<User> RegisterAsync(string email, string password, CancellationToken ct);

    /// <summary>Throws <see cref="InvalidCredentialsException"/> for an unknown email and for a wrong password alike.</summary>
    Task<User> LoginAsync(string email, string password, CancellationToken ct);

    /// <summary>Null if the account no longer exists (a token can outlive it).</summary>
    Task<User?> FindAsync(Guid userId, CancellationToken ct);

    /// <summary>Sets a new password and raises the session version, which signs out every other device.</summary>
    Task<User> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, CancellationToken ct);

    /// <summary>Deletes the account with all of its episodes, seasons, listening progress and audio files.</summary>
    Task DeleteAccountAsync(Guid userId, string password, CancellationToken ct);
}

public class AuthService(IUserRepository users, IPasswordHasher hasher, IAudioStorage storage, TimeProvider clock) : IAuthService
{
    public const int MinPasswordLength = 8;
    public const int MaxPasswordLength = 128;
    public const int MaxEmailLength = 254;

    // Verified when the email is unknown, so that case takes as long as a wrong password.
    private readonly Lazy<string> _decoyHash = new(() => hasher.Hash(Guid.NewGuid().ToString()));

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    public async Task<User> RegisterAsync(string email, string password, CancellationToken ct)
    {
        var normalized = NormalizeEmail(email);
        Validate(normalized, password);

        var hash = hasher.Hash(password);
        var displayName = normalized[..normalized.IndexOf('@', StringComparison.Ordinal)];
        displayName = displayName.Length <= 100 ? displayName : displayName[..100];

        if (await users.GetByEmailAsync(normalized, ct) is null)
        {
            if (await users.TryClaimLocalUserAsync(normalized, hash, displayName, ct) is { } claimed)
            {
                return claimed;
            }

            var user = new User
            {
                Id = Guid.NewGuid(),
                Email = normalized,
                PasswordHash = hash,
                DisplayName = displayName,
                CreatedAt = clock.GetUtcNow(),
            };
            if (await users.TryAddAsync(user, ct))
            {
                return user;
            }
        }

        throw new ConflictException("An account with this email already exists. Sign in instead.");
    }

    public async Task<User> LoginAsync(string email, string password, CancellationToken ct)
    {
        var user = await users.GetByEmailAsync(NormalizeEmail(email), ct);
        var check = hasher.Verify(user?.PasswordHash ?? _decoyHash.Value, password);
        if (user?.PasswordHash is null || check == PasswordCheck.Wrong)
        {
            throw new InvalidCredentialsException();
        }

        if (check == PasswordCheck.CorrectNeedsRehash)
        {
            user.PasswordHash = hasher.Hash(password);
            await users.SaveChangesAsync(ct);
        }

        return user;
    }

    public Task<User?> FindAsync(Guid userId, CancellationToken ct) => users.GetAsync(userId, ct);

    public async Task<User> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, CancellationToken ct)
    {
        var user = await RequireUserWithPasswordAsync(userId, currentPassword, "CurrentPassword", ct);
        if (newPassword.Length is < MinPasswordLength or > MaxPasswordLength)
        {
            throw new RequestValidationException("NewPassword", $"Use {MinPasswordLength} to {MaxPasswordLength} characters.");
        }

        user.PasswordHash = hasher.Hash(newPassword);
        await users.SaveChangesAsync(ct);
        return user;
    }

    public async Task DeleteAccountAsync(Guid userId, string password, CancellationToken ct)
    {
        var user = await RequireUserWithPasswordAsync(userId, password, "Password", ct);
        if (await users.HasEpisodesInProgressAsync(userId, ct))
        {
            throw new ConflictException("An episode is being generated right now. Delete the account once it is ready or has failed.");
        }

        var keys = await users.AudioKeysAsync(userId, ct);
        users.Remove(user);
        await users.SaveChangesAsync(ct);

        foreach (var key in keys)
        {
            await storage.DeleteAsync(key, ct); // after the DB delete: a crash leaves orphan files, never half an account
        }
    }

    /// <summary>
    /// Re-checks the password for a sensitive change. A wrong one is a field error (400), not 401: the user is signed
    /// in, and a 401 would make the web app show the sign-in screen.
    /// </summary>
    private async Task<User> RequireUserWithPasswordAsync(Guid userId, string password, string field, CancellationToken ct)
    {
        var user = await users.GetAsync(userId, ct) ?? throw new NotFoundException("The account no longer exists.");
        if (user.PasswordHash is null || hasher.Verify(user.PasswordHash, password) == PasswordCheck.Wrong)
        {
            throw new RequestValidationException(field, "The password is incorrect.");
        }

        return user;
    }

    private static void Validate(string email, string password)
    {
        var errors = new Dictionary<string, string[]>();
        var at = email.IndexOf('@', StringComparison.Ordinal);
        if (email.Length > MaxEmailLength || at <= 0 || at != email.LastIndexOf('@') || at == email.Length - 1 || email.Any(char.IsWhiteSpace))
        {
            errors["Email"] = ["Enter a valid email address."];
        }

        // Length over composition rules (NIST 800-63B): long passphrases beat forced symbols.
        if (password.Length is < MinPasswordLength or > MaxPasswordLength)
        {
            errors["Password"] = [$"Use {MinPasswordLength} to {MaxPasswordLength} characters."];
        }

        if (errors.Count > 0)
        {
            throw new RequestValidationException(errors);
        }
    }
}
