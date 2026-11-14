using PodcastGenerator.Application.Auth;
using PodcastGenerator.Application.Common;
using PodcastGenerator.Domain.Entities;
using PodcastGenerator.Infrastructure.Identity;

namespace PodcastGenerator.UnitTests;

public class AuthServiceTests
{
    private readonly FakeUsers _users = new();
    private readonly CountingHasher _hasher = new();

    private AuthService Service() => new(_users, _hasher, TimeProvider.System);

    [Fact]
    public async Task Register_stores_a_normalized_email_and_a_hash_never_the_password()
    {
        var user = await Service().RegisterAsync("  Lena@Example.COM ", "ein langes passwort", CancellationToken.None);

        Assert.Equal("lena@example.com", user.Email);
        Assert.Equal("lena", user.DisplayName);
        Assert.NotNull(user.PasswordHash);
        Assert.DoesNotContain("ein langes passwort", user.PasswordHash, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_first_account_claims_the_local_user_and_later_ones_are_new()
    {
        _users.LocalUser = new User { Id = Guid.NewGuid(), DisplayName = "Local learner" };

        var first = await Service().RegisterAsync("a@example.com", "ein langes passwort", CancellationToken.None);
        var second = await Service().RegisterAsync("b@example.com", "ein langes passwort", CancellationToken.None);

        Assert.Equal(_users.LocalUser.Id, first.Id);
        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public async Task Register_refuses_an_email_that_is_taken()
    {
        await Service().RegisterAsync("a@example.com", "ein langes passwort", CancellationToken.None);

        await Assert.ThrowsAsync<ConflictException>(() => Service().RegisterAsync("A@example.com", "noch ein passwort", CancellationToken.None));
    }

    [Theory]
    [InlineData("", "ein langes passwort", "Email")]
    [InlineData("@example.com", "ein langes passwort", "Email")]
    [InlineData("a@", "ein langes passwort", "Email")]
    [InlineData("a b@example.com", "ein langes passwort", "Email")]
    [InlineData("a@example.com", "1234567", "Password")]
    public async Task Register_validates_input(string email, string password, string field)
    {
        var ex = await Assert.ThrowsAsync<RequestValidationException>(() => Service().RegisterAsync(email, password, CancellationToken.None));
        Assert.Contains(field, ex.Errors.Keys);
    }

    [Fact]
    public async Task Login_fails_the_same_way_for_an_unknown_email_and_a_wrong_password()
    {
        await Service().RegisterAsync("a@example.com", "ein langes passwort", CancellationToken.None);
        var service = Service();
        _hasher.Verifications = 0;

        var wrong = await Assert.ThrowsAsync<InvalidCredentialsException>(() => service.LoginAsync("a@example.com", "falsch", CancellationToken.None));
        var unknown = await Assert.ThrowsAsync<InvalidCredentialsException>(() => service.LoginAsync("z@example.com", "falsch", CancellationToken.None));

        Assert.Equal(wrong.Message, unknown.Message);
        Assert.Equal(2, _hasher.Verifications); // the unknown email still costs one hash check, so timing doesn't give it away
    }

    [Fact]
    public async Task Login_accepts_the_right_password_in_any_email_spelling()
    {
        var registered = await Service().RegisterAsync("a@example.com", "ein langes passwort", CancellationToken.None);

        var user = await Service().LoginAsync(" A@Example.com", "ein langes passwort", CancellationToken.None);

        Assert.Equal(registered.Id, user.Id);
    }

    [Fact]
    public async Task Login_replaces_a_hash_made_with_outdated_settings()
    {
        var user = await Service().RegisterAsync("a@example.com", "ein langes passwort", CancellationToken.None);
        var oldHash = user.PasswordHash;
        _hasher.ReportOutdated = true;

        await Service().LoginAsync("a@example.com", "ein langes passwort", CancellationToken.None);

        Assert.NotEqual(oldHash, user.PasswordHash);
        Assert.Equal(1, _users.Saves);
    }

    private sealed class CountingHasher : IPasswordHasher
    {
        private readonly IdentityPasswordHasher _real = new();

        public int Verifications { get; set; }

        public bool ReportOutdated { get; set; }

        public string Hash(string password) => _real.Hash(password);

        public PasswordCheck Verify(string hash, string password)
        {
            Verifications++;
            var result = _real.Verify(hash, password);
            return result == PasswordCheck.Correct && ReportOutdated ? PasswordCheck.CorrectNeedsRehash : result;
        }
    }

    private sealed class FakeUsers : IUserRepository
    {
        private readonly List<User> _users = [];

        public User? LocalUser { get; set; }

        public int Saves { get; private set; }

        public Task<User?> GetAsync(Guid userId, CancellationToken ct) => Task.FromResult(_users.SingleOrDefault(u => u.Id == userId));

        public Task<User?> GetByEmailAsync(string normalizedEmail, CancellationToken ct) =>
            Task.FromResult(_users.SingleOrDefault(u => u.Email == normalizedEmail));

        public Task<User?> TryClaimLocalUserAsync(string normalizedEmail, string passwordHash, string displayName, CancellationToken ct)
        {
            if (LocalUser is null || LocalUser.Email is not null)
            {
                return Task.FromResult<User?>(null);
            }

            LocalUser.Email = normalizedEmail;
            LocalUser.PasswordHash = passwordHash;
            LocalUser.DisplayName = displayName;
            _users.Add(LocalUser);
            return Task.FromResult<User?>(LocalUser);
        }

        public Task<bool> TryAddAsync(User user, CancellationToken ct)
        {
            if (_users.Any(u => u.Email == user.Email))
            {
                return Task.FromResult(false);
            }

            _users.Add(user);
            return Task.FromResult(true);
        }

        public Task SaveChangesAsync(CancellationToken ct)
        {
            Saves++;
            return Task.CompletedTask;
        }
    }
}
