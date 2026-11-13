using Microsoft.Extensions.Options;
using PodcastGenerator.Application.Common;
using PodcastGenerator.Application.Tts;
using PodcastGenerator.Domain.Entities;

namespace PodcastGenerator.Application.Preferences;

public interface IPreferencesService
{
    Task<UserPreferences> GetAsync(CancellationToken ct);
    Task<UserPreferences> UpdateAsync(UpdatePreferences request, CancellationToken ct);
}

public class PreferencesService(
    IPreferencesRepository repository,
    ICurrentUser currentUser,
    IOptions<EpisodeOptions> episodeOptions,
    TimeProvider clock) : IPreferencesService
{
    public const int MaxInterestLength = 100;

    public async Task<UserPreferences> GetAsync(CancellationToken ct)
    {
        var existing = await repository.GetAsync(currentUser.UserId, ct);
        if (existing is not null)
        {
            return existing;
        }

        // First visit: persist defaults so later reads and writes are uniform. Two first requests may race;
        // the loser reads the winner's row instead of failing.
        var defaults = new UserPreferences { UserId = currentUser.UserId, UpdatedAt = clock.GetUtcNow() };
        return await repository.TryAddAsync(defaults, ct)
            ? defaults
            : await repository.GetAsync(currentUser.UserId, ct) ?? throw new InvalidOperationException("Preferences vanished after a concurrent insert.");
    }

    public async Task<UserPreferences> UpdateAsync(UpdatePreferences request, CancellationToken ct)
    {
        Validate(request);

        var prefs = await GetAsync(ct);
        prefs.NativeLanguage = request.NativeLanguage;
        prefs.TargetLanguage = request.TargetLanguage;
        prefs.DefaultLevel = request.DefaultLevel;
        prefs.DefaultDurationMin = request.DefaultDurationMin;
        prefs.Interests = request.Interests
            .Select(i => i.Trim())
            .Where(i => i.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        prefs.VoiceName = string.IsNullOrWhiteSpace(request.VoiceName) ? null : request.VoiceName.Trim();
        prefs.VoiceModelFamily = request.VoiceModelFamily;
        prefs.PlaybackSpeed = request.PlaybackSpeed;
        prefs.SpeakingRate = request.SpeakingRate;
        prefs.UpdatedAt = clock.GetUtcNow();

        await repository.SaveChangesAsync(ct);
        return prefs;
    }

    private void Validate(UpdatePreferences request)
    {
        var errors = new Dictionary<string, string[]>();
        var options = episodeOptions.Value;

        if (!SupportedLanguages.Target.Contains(request.TargetLanguage))
        {
            errors[nameof(request.TargetLanguage)] = [$"Supported target languages: {string.Join(", ", SupportedLanguages.Target)}."];
        }

        if (!SupportedLanguages.Native.Contains(request.NativeLanguage))
        {
            errors[nameof(request.NativeLanguage)] = [$"Supported native languages: {string.Join(", ", SupportedLanguages.Native)}."];
        }

        if (request.DefaultDurationMin < options.MinDurationMin || request.DefaultDurationMin > options.MaxDurationMin)
        {
            errors[nameof(request.DefaultDurationMin)] = [$"Must be between {options.MinDurationMin} and {options.MaxDurationMin} minutes."];
        }

        if (request.SpeakingRate is { } rate && options.SpeakingRateError(rate) is { } rateError)
        {
            errors[nameof(request.SpeakingRate)] = [$"{rateError} Leave it empty for the level's default."];
        }

        // JSON null reaches here despite the non-nullable type.
        if (request.Interests is null || request.Interests.Any(i => i is null))
        {
            errors[nameof(request.Interests)] = ["Must be a list of texts (use [] for none)."];
        }
        else if (request.Interests.Any(i => i.Trim().Length > MaxInterestLength))
        {
            errors[nameof(request.Interests)] = [$"Each interest can have at most {MaxInterestLength} characters."];
        }

        // Google bills by the voice name, and the budget is counted per family: the two must agree.
        if (!string.IsNullOrWhiteSpace(request.VoiceName))
        {
            var family = VoiceFamilies.Of(request.VoiceName);
            if (family is null)
            {
                errors[nameof(request.VoiceName)] = ["Only WaveNet and Chirp 3: HD voices are supported (see GET /api/voices)."];
            }
            else if (family != request.VoiceModelFamily)
            {
                errors[nameof(request.VoiceName)] = [$"This is a {family} voice; set voiceModelFamily to {family} or choose a {request.VoiceModelFamily} voice."];
            }
        }

        if (errors.Count > 0)
        {
            throw new RequestValidationException(errors);
        }
    }
}
