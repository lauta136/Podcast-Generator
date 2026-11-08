using Microsoft.Extensions.Options;
using PodcastGenerator.Application;
using PodcastGenerator.Application.Common;
using PodcastGenerator.Application.Preferences;
using PodcastGenerator.Domain.Entities;
using PodcastGenerator.Domain.Enums;

namespace PodcastGenerator.UnitTests;

public class PreferencesServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private readonly FakeRepository _repository = new();

    private PreferencesService CreateService() => new(
        _repository,
        new FakeCurrentUser(),
        Options.Create(new EpisodeOptions { MinDurationMin = 5, MaxDurationMin = 60 }),
        TimeProvider.System);

    private static UpdatePreferences ValidRequest() => new(
        NativeLanguage: "en",
        TargetLanguage: "de",
        DefaultLevel: CefrLevel.B1,
        DefaultDurationMin: 20,
        Interests: ["history"],
        VoiceName: null,
        VoiceModelFamily: TtsModelFamily.WaveNet,
        PlaybackSpeed: 1.0m);

    [Fact]
    public async Task Get_creates_defaults_on_first_visit()
    {
        var prefs = await CreateService().GetAsync(CancellationToken.None);

        Assert.Equal("en", prefs.NativeLanguage);
        Assert.Equal("de", prefs.TargetLanguage);
        Assert.Equal(CefrLevel.B1, prefs.DefaultLevel);
        Assert.Equal(1, _repository.Saves);
    }

    [Fact]
    public async Task Update_normalizes_interests()
    {
        var request = ValidRequest() with { Interests = [" History ", "history", "", "Football"] };

        var prefs = await CreateService().UpdateAsync(request, CancellationToken.None);

        Assert.Equal(["History", "Football"], prefs.Interests);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(61)]
    public async Task Update_rejects_duration_outside_configured_range(int minutes)
    {
        var request = ValidRequest() with { DefaultDurationMin = minutes };

        var ex = await Assert.ThrowsAsync<RequestValidationException>(
            () => CreateService().UpdateAsync(request, CancellationToken.None));

        Assert.Contains(nameof(UpdatePreferences.DefaultDurationMin), ex.Errors.Keys);
    }

    [Fact]
    public async Task Update_rejects_unsupported_target_language()
    {
        var request = ValidRequest() with { TargetLanguage = "fr" };

        var ex = await Assert.ThrowsAsync<RequestValidationException>(
            () => CreateService().UpdateAsync(request, CancellationToken.None));

        Assert.Contains(nameof(UpdatePreferences.TargetLanguage), ex.Errors.Keys);
    }

    private sealed class FakeCurrentUser : ICurrentUser
    {
        public Guid UserId => PreferencesServiceTests.UserId;
    }

    private sealed class FakeRepository : IPreferencesRepository
    {
        private UserPreferences? _stored;

        public int Saves { get; private set; }

        public Task<UserPreferences?> GetAsync(Guid userId, CancellationToken ct) => Task.FromResult(_stored);

        public void Add(UserPreferences preferences) => _stored = preferences;

        public Task SaveChangesAsync(CancellationToken ct)
        {
            Saves++;
            return Task.CompletedTask;
        }
    }
}
