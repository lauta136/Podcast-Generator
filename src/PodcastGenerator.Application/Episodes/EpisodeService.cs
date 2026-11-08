using Microsoft.Extensions.Options;
using PodcastGenerator.Application.Common;
using PodcastGenerator.Application.Generation;
using PodcastGenerator.Application.Preferences;
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
}

public class EpisodeService(
    IEpisodeRepository episodes,
    IPreferencesService preferences,
    ICurrentUser currentUser,
    IOptions<EpisodeOptions> options,
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
            TargetWords = LengthEstimator.TargetWords(level, duration, o),
            VoiceName = prefs.VoiceName,
            VoiceModelFamily = prefs.VoiceModelFamily,
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
            episode.Sections.Count(s => s.Status == StepStatus.Completed));
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
