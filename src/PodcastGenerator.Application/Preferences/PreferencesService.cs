using Microsoft.Extensions.Options;
using PodcastGenerator.Application.Common;
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
    public async Task<UserPreferences> GetAsync(CancellationToken ct)
    {
        var existing = await repository.GetAsync(currentUser.UserId, ct);
        if (existing is not null)
        {
            return existing;
        }

        // First visit: persist defaults so later reads and writes are uniform.
        var defaults = new UserPreferences { UserId = currentUser.UserId, UpdatedAt = clock.GetUtcNow() };
        repository.Add(defaults);
        await repository.SaveChangesAsync(ct);
        return defaults;
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

        if (request.SpeakingRate is { } rate && (rate < options.MinSpeakingRate || rate > options.MaxSpeakingRate))
        {
            errors[nameof(request.SpeakingRate)] = [$"Must be between {options.MinSpeakingRate} and {options.MaxSpeakingRate}, or empty for the level's default."];
        }

        if (errors.Count > 0)
        {
            throw new RequestValidationException(errors);
        }
    }
}
