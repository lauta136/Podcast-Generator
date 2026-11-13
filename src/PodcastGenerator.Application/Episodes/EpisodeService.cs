using Microsoft.Extensions.Options;
using PodcastGenerator.Application.Common;
using PodcastGenerator.Application.Generation;
using PodcastGenerator.Application.Preferences;
using PodcastGenerator.Application.Tts;
using PodcastGenerator.Domain.Entities;
using PodcastGenerator.Domain.Enums;

namespace PodcastGenerator.Application.Episodes;

public interface IEpisodeService
{
    Task<Episode> CreateAsync(CreateEpisode request, CancellationToken ct);
    Task<Episode> GetAsync(Guid episodeId, CancellationToken ct);
    Task<EpisodeProgress> GetProgressAsync(Guid episodeId, CancellationToken ct);
    Task<Episode> GetTranscriptAsync(Guid episodeId, CancellationToken ct);
    Task RetryAsync(Guid episodeId, CancellationToken ct);
    Task RateAsync(Guid episodeId, DifficultyRating rating, CancellationToken ct);
    Task DeleteAsync(Guid episodeId, CancellationToken ct);
    Task<Stream> OpenAudioAsync(Guid episodeId, CancellationToken ct);
    Task<EpisodeEstimate> EstimateAsync(CefrLevel? level, int? durationMin, decimal? speakingRate, CancellationToken ct);
}

public class EpisodeService(
    IEpisodeRepository episodes,
    IPreferencesService preferences,
    ICurrentUser currentUser,
    IOptions<EpisodeOptions> options,
    IOptions<TtsOptions> ttsOptions,
    TtsBudget budget,
    IAudioStorage storage,
    TimeProvider clock) : IEpisodeService
{
    public const int MaxTopicLength = 500;

    /// <summary>Validates and queues the episode. Generation happens in the background worker, never here.</summary>
    public async Task<Episode> CreateAsync(CreateEpisode request, CancellationToken ct)
    {
        var prefs = await preferences.GetAsync(ct);
        var o = options.Value;
        var level = request.Level ?? prefs.DefaultLevel;
        var duration = request.DurationMin ?? prefs.DefaultDurationMin;
        var topic = ResolveTopic(request, prefs);

        if (duration < o.MinDurationMin || duration > o.MaxDurationMin)
        {
            throw new RequestValidationException(nameof(request.DurationMin), $"Must be between {o.MinDurationMin} and {o.MaxDurationMin} minutes.");
        }

        // Refuse up front rather than generate text that can't be voiced this month.
        var estimate = await EstimateAsync(level, duration, request.SpeakingRate, ct);
        if (!estimate.FitsBudget)
        {
            throw new ConflictException(
                $"Not enough free {estimate.VoiceModelFamily} budget left this month: this episode needs about " +
                $"{estimate.EstimatedChars:N0} characters, {estimate.BudgetRemaining:N0} remain. Choose a shorter episode or another voice quality.");
        }

        var now = clock.GetUtcNow();
        var episode = new Episode
        {
            Id = Guid.NewGuid(),
            UserId = currentUser.UserId,
            Topic = topic,
            LanguageCode = prefs.TargetLanguage,
            Level = level,
            Style = request.Style,
            DurationMin = duration,
            TargetWords = estimate.TargetWords,
            VoiceName = prefs.VoiceName,
            VoiceModelFamily = prefs.VoiceModelFamily,
            SpeakingRate = estimate.SpeakingRate,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var job = new GenerationJob { Id = Guid.NewGuid(), EpisodeId = episode.Id, CreatedAt = now, UpdatedAt = now };

        episodes.Add(episode, job);
        await episodes.SaveChangesAsync(ct);
        return episode;
    }

    public async Task<Episode> GetAsync(Guid episodeId, CancellationToken ct) =>
        await episodes.GetAsync(currentUser.UserId, episodeId, ct) ?? throw NotFound(episodeId);

    public async Task<EpisodeProgress> GetProgressAsync(Guid episodeId, CancellationToken ct)
    {
        var episode = await episodes.GetWithTranscriptAsync(currentUser.UserId, episodeId, ct) ?? throw NotFound(episodeId);
        return new EpisodeProgress(
            episode.Id,
            episode.Status,
            episode.Title,
            episode.LastError,
            episode.Sections.Count,
            episode.Sections.Count(s => s.Status == StepStatus.Completed),
            episode.AudioSegments.Count,
            episode.AudioSegments.Count(a => a.Status == StepStatus.Completed));
    }

    public async Task<Episode> GetTranscriptAsync(Guid episodeId, CancellationToken ct) =>
        await episodes.GetWithTranscriptAsync(currentUser.UserId, episodeId, ct) ?? throw NotFound(episodeId);

    /// <summary>Re-queues a failed episode. Completed sections are kept; only failed ones run again.</summary>
    public async Task RetryAsync(Guid episodeId, CancellationToken ct)
    {
        var episode = await episodes.GetWithTranscriptAsync(currentUser.UserId, episodeId, ct) ?? throw NotFound(episodeId);
        if (episode.Status is not (EpisodeStatus.Failed or EpisodeStatus.PausedForBudget))
        {
            throw new ConflictException($"Only failed or paused episodes can be retried (status: {episode.Status}).");
        }

        var job = await episodes.GetJobAsync(episodeId, ct)
            ?? throw new InvalidOperationException($"Episode {episodeId} has no generation job.");

        GenerationState.Requeue(episode, job, clock.GetUtcNow());
        await episodes.SaveChangesAsync(ct);
    }

    public async Task RateAsync(Guid episodeId, DifficultyRating rating, CancellationToken ct)
    {
        var episode = await episodes.GetAsync(currentUser.UserId, episodeId, ct) ?? throw NotFound(episodeId);
        if (episode.Status != EpisodeStatus.Ready)
        {
            throw new ConflictException("Only finished episodes can be rated.");
        }

        episode.DifficultyRating = rating;
        episode.UpdatedAt = clock.GetUtcNow();
        await episodes.SaveChangesAsync(ct);
    }

    public async Task<Stream> OpenAudioAsync(Guid episodeId, CancellationToken ct)
    {
        var episode = await episodes.GetAsync(currentUser.UserId, episodeId, ct) ?? throw NotFound(episodeId);
        return episode is { Status: EpisodeStatus.Ready, AudioStorageKey: { } key }
            ? await storage.OpenReadAsync(key, ct)
            : throw new NotFoundException($"Episode {episodeId} has no audio yet (status: {episode.Status}).");
    }

    /// <summary>Deletes the episode and all of its audio files.</summary>
    public async Task DeleteAsync(Guid episodeId, CancellationToken ct)
    {
        var episode = await episodes.GetWithTranscriptAsync(currentUser.UserId, episodeId, ct) ?? throw NotFound(episodeId);
        if (episode.Status is EpisodeStatus.GeneratingOutline or EpisodeStatus.GeneratingSections or EpisodeStatus.GeneratingAudio)
        {
            throw new ConflictException("The episode is being generated right now; delete it once it is ready or has failed.");
        }

        var keys = episode.AudioSegments.Select(a => a.StorageKey).Append(episode.AudioStorageKey).OfType<string>().ToList();
        episodes.Remove(episode);
        await episodes.SaveChangesAsync(ct);

        foreach (var key in keys)
        {
            await storage.DeleteAsync(key, ct); // after the DB delete: a crash leaves orphan files, never broken episodes
        }
    }

    public async Task<EpisodeEstimate> EstimateAsync(CefrLevel? level, int? durationMin, decimal? speakingRate, CancellationToken ct)
    {
        var prefs = await preferences.GetAsync(ct);
        var lvl = level ?? prefs.DefaultLevel;
        var minutes = durationMin ?? prefs.DefaultDurationMin;
        var rate = speakingRate ?? prefs.SpeakingRate ?? options.Value.SpeakingRateFor(lvl);
        if (rate < options.Value.MinSpeakingRate || rate > options.Value.MaxSpeakingRate)
        {
            throw new RequestValidationException(
                nameof(CreateEpisode.SpeakingRate), $"Must be between {options.Value.MinSpeakingRate} and {options.Value.MaxSpeakingRate}.");
        }

        // Slower speech fills the same minutes with fewer words.
        var words = LengthEstimator.TargetWords(minutes, ttsOptions.Value.WordsPerMinute(prefs.VoiceModelFamily, rate));
        var chars = (long)Math.Ceiling(words * ttsOptions.Value.CharsPerWord);
        var status = (await budget.GetStatusAsync(ct)).Single(s => s.Family == prefs.VoiceModelFamily);
        return new EpisodeEstimate(
            lvl, minutes, words, rate, prefs.VoiceModelFamily, chars, status.Remaining, status.Budget, chars <= status.Remaining);
    }

    private static string ResolveTopic(CreateEpisode request, UserPreferences prefs)
    {
        var topic = request.Topic?.Trim();
        if (!string.IsNullOrEmpty(topic))
        {
            return topic.Length <= MaxTopicLength
                ? topic
                : throw new RequestValidationException(nameof(request.Topic), $"At most {MaxTopicLength} characters.");
        }

        if (!request.SurpriseMe)
        {
            throw new RequestValidationException(nameof(request.Topic), "Enter a topic or choose 'Surprise me'.");
        }

        return prefs.Interests.Count > 0
            ? $"Surprise me: pick a specific, surprising angle on {prefs.Interests[Random.Shared.Next(prefs.Interests.Count)]}"
            : "Surprise me: pick a specific, surprising topic about everyday life, history or culture in German-speaking countries";
    }

    private static NotFoundException NotFound(Guid id) => new($"Episode {id} was not found.");
}
