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

    [Theory]
    [InlineData("0.5")]
    [InlineData("1.5")]
    public async Task Update_rejects_speaking_rate_outside_configured_range(string rate)
    {
        var request = ValidRequest() with { SpeakingRate = decimal.Parse(rate, System.Globalization.CultureInfo.InvariantCulture) };

        var ex = await Assert.ThrowsAsync<RequestValidationException>(
            () => CreateService().UpdateAsync(request, CancellationToken.None));

        Assert.Contains(nameof(UpdatePreferences.SpeakingRate), ex.Errors.Keys);
    }

    [Fact]
    public async Task Update_stores_speaking_rate_and_null_means_level_default()
    {
        var service = CreateService();

        Assert.Equal(0.9m, (await service.UpdateAsync(ValidRequest() with { SpeakingRate = 0.9m }, CancellationToken.None)).SpeakingRate);
        Assert.Null((await service.UpdateAsync(ValidRequest() with { SpeakingRate = null }, CancellationToken.None)).SpeakingRate);
    }

    [Theory]
    [InlineData("de-DE-Chirp3-HD-Charon", TtsModelFamily.WaveNet)] // would be billed as Chirp but counted as WaveNet
    [InlineData("de-DE-Wavenet-H", TtsModelFamily.Chirp3HD)]
    [InlineData("de-DE-Studio-B", TtsModelFamily.WaveNet)] // a family with no budget at all
    public async Task Update_rejects_a_voice_that_does_not_match_the_budgeted_family(string voice, TtsModelFamily family)
    {
        var request = ValidRequest() with { VoiceName = voice, VoiceModelFamily = family };

        var ex = await Assert.ThrowsAsync<RequestValidationException>(
            () => CreateService().UpdateAsync(request, CancellationToken.None));

        Assert.Contains(nameof(UpdatePreferences.VoiceName), ex.Errors.Keys);
    }

    [Fact]
    public async Task Update_accepts_a_matching_voice() =>
        Assert.Equal("de-DE-Chirp3-HD-Charon", (await CreateService().UpdateAsync(
            ValidRequest() with { VoiceName = "de-DE-Chirp3-HD-Charon", VoiceModelFamily = TtsModelFamily.Chirp3HD },
            CancellationToken.None)).VoiceName);

    [Fact]
    public async Task Update_rejects_null_or_overlong_interests()
    {
        foreach (var interests in new[] { null!, new[] { "history", null! }, new[] { new string('x', PreferencesService.MaxInterestLength + 1) } })
        {
            var ex = await Assert.ThrowsAsync<RequestValidationException>(
                () => CreateService().UpdateAsync(ValidRequest() with { Interests = interests }, CancellationToken.None));
            Assert.Contains(nameof(UpdatePreferences.Interests), ex.Errors.Keys);
        }
    }

    [Fact]
    public async Task Get_reads_the_row_another_request_created_first()
    {
        var winner = new UserPreferences { UserId = UserId, DefaultLevel = CefrLevel.C1 };
        _repository.RaceWith(winner);

        var prefs = await CreateService().GetAsync(CancellationToken.None);

        Assert.Same(winner, prefs);
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
        private UserPreferences? _racer;

        /// <summary>Simulates another request inserting this row between our read and our insert.</summary>
        public void RaceWith(UserPreferences row) => _racer = row;

        public int Saves { get; private set; }

        public Task<UserPreferences?> GetAsync(Guid userId, CancellationToken ct)
        {
            var current = _stored;
            if (_racer is not null)
            {
                _stored = _racer;
                _racer = null;
            }

            return Task.FromResult(current);
        }

        public Task<bool> TryAddAsync(UserPreferences preferences, CancellationToken ct)
        {
            if (_stored is not null)
            {
                return Task.FromResult(false);
            }

            _stored = preferences;
            Saves++;
            return Task.FromResult(true);
        }

        public Task SaveChangesAsync(CancellationToken ct)
        {
            Saves++;
            return Task.CompletedTask;
        }
    }
}
