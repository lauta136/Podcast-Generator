using Microsoft.Extensions.Options;
using PodcastGenerator.Application.Auth;
using PodcastGenerator.Domain.Entities;

namespace PodcastGenerator.UnitTests;

public class SessionServiceTests
{
    private readonly Tokens _tokens = new();
    private readonly Users _users = new();
    private readonly Clock _clock = new();
    private readonly User _user = new() { Id = Guid.NewGuid(), DisplayName = "a" };

    public SessionServiceTests() => _users.All.Add(_user);

    private SessionService Service(int graceSeconds = 30) =>
        new(_tokens, _users, Options.Create(new SessionOptions { RefreshTokenDays = 30, ReuseGraceSeconds = graceSeconds }), _clock);

    [Fact]
    public async Task Only_a_hash_is_stored_and_each_refresh_rotates_within_the_family()
    {
        var issued = await Service().StartAsync(_user, CancellationToken.None);
        var stored = Assert.Single(_tokens.All);
        Assert.NotEqual(issued.Token, stored.TokenHash);
        Assert.Equal(SessionService.Hash(issued.Token), stored.TokenHash);

        var result = await Service().RefreshAsync(issued.Token, CancellationToken.None);

        Assert.NotNull(result.Rotated);
        Assert.Equal(2, _tokens.All.Count);
        Assert.All(_tokens.All, t => Assert.Equal(stored.FamilyId, t.FamilyId));
        Assert.NotNull(_tokens.All[0].UsedAt);
    }

    [Fact]
    public async Task A_just_used_token_renews_access_only_but_later_reuse_ends_the_sign_in()
    {
        var issued = await Service().StartAsync(_user, CancellationToken.None);
        var rotated = (await Service().RefreshAsync(issued.Token, CancellationToken.None)).Rotated!;

        _clock.Advance(TimeSpan.FromSeconds(10));
        var race = await Service().RefreshAsync(issued.Token, CancellationToken.None);
        Assert.Null(race.Rotated); // second tab: access token only

        _clock.Advance(TimeSpan.FromMinutes(5));
        await Assert.ThrowsAsync<SessionEndedException>(() => Service().RefreshAsync(issued.Token, CancellationToken.None));
        await Assert.ThrowsAsync<SessionEndedException>(() => Service().RefreshAsync(rotated.Token, CancellationToken.None)); // family revoked
    }

    [Fact]
    public async Task Expired_unknown_and_ended_sign_ins_are_refused()
    {
        var issued = await Service().StartAsync(_user, CancellationToken.None);
        var other = await Service().StartAsync(_user, CancellationToken.None);

        await Assert.ThrowsAsync<SessionEndedException>(() => Service().RefreshAsync("made-up", CancellationToken.None));

        await Service().EndAsync(other.Token, CancellationToken.None);
        await Assert.ThrowsAsync<SessionEndedException>(() => Service().RefreshAsync(other.Token, CancellationToken.None));

        _clock.Advance(TimeSpan.FromDays(31));
        await Assert.ThrowsAsync<SessionEndedException>(() => Service().RefreshAsync(issued.Token, CancellationToken.None));
    }

    [Fact]
    public async Task Ending_all_sign_ins_revokes_every_family_of_the_user()
    {
        var laptop = await Service().StartAsync(_user, CancellationToken.None);
        var phone = await Service().StartAsync(_user, CancellationToken.None);

        await Service().EndAllAsync(_user.Id, CancellationToken.None);

        await Assert.ThrowsAsync<SessionEndedException>(() => Service().RefreshAsync(laptop.Token, CancellationToken.None));
        await Assert.ThrowsAsync<SessionEndedException>(() => Service().RefreshAsync(phone.Token, CancellationToken.None));
    }

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    private sealed class Tokens : IRefreshTokenRepository
    {
        public List<RefreshToken> All { get; } = [];

        public void Add(RefreshToken token) => All.Add(token);

        public Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken ct) =>
            Task.FromResult(All.SingleOrDefault(t => t.TokenHash == tokenHash) is { } t ? Copy(t) : null);

        public Task<bool> TryMarkUsedAsync(Guid tokenId, DateTimeOffset now, CancellationToken ct)
        {
            var t = All.Single(x => x.Id == tokenId);
            if (t.UsedAt is not null)
            {
                return Task.FromResult(false);
            }

            t.UsedAt = now;
            return Task.FromResult(true);
        }

        public Task RevokeFamilyAsync(Guid familyId, DateTimeOffset now, CancellationToken ct)
        {
            All.Where(t => t.FamilyId == familyId).ToList().ForEach(t => t.RevokedAt ??= now);
            return Task.CompletedTask;
        }

        public Task RevokeAllAsync(Guid userId, DateTimeOffset now, CancellationToken ct)
        {
            All.Where(t => t.UserId == userId).ToList().ForEach(t => t.RevokedAt ??= now);
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;

        // The real repository reads untracked copies; mutations go through the methods above.
        private static RefreshToken Copy(RefreshToken t) => new()
        {
            Id = t.Id,
            UserId = t.UserId,
            FamilyId = t.FamilyId,
            TokenHash = t.TokenHash,
            CreatedAt = t.CreatedAt,
            ExpiresAt = t.ExpiresAt,
            UsedAt = t.UsedAt,
            RevokedAt = t.RevokedAt,
        };
    }

    private sealed class Users : IUserRepository
    {
        public List<User> All { get; } = [];

        public Task<User?> GetAsync(Guid userId, CancellationToken ct) => Task.FromResult(All.SingleOrDefault(u => u.Id == userId));

        public Task<User?> GetByEmailAsync(string normalizedEmail, CancellationToken ct) => throw new NotSupportedException();

        public Task<User?> TryClaimLocalUserAsync(string normalizedEmail, string passwordHash, string displayName, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<bool> TryAddAsync(User user, CancellationToken ct) => throw new NotSupportedException();

        public Task<IReadOnlyList<string>> AudioKeysAsync(Guid userId, CancellationToken ct) => throw new NotSupportedException();

        public Task<bool> HasEpisodesInProgressAsync(Guid userId, CancellationToken ct) => throw new NotSupportedException();

        public void Remove(User user) => throw new NotSupportedException();

        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
