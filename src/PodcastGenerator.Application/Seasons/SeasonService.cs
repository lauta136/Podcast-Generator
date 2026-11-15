using PodcastGenerator.Application.Common;
using PodcastGenerator.Application.Episodes;
using PodcastGenerator.Application.Generation;
using PodcastGenerator.Application.Preferences;
using PodcastGenerator.Application.Tts;
using PodcastGenerator.Domain.Entities;
using PodcastGenerator.Domain.Enums;

namespace PodcastGenerator.Application.Seasons;

/// <param name="Level">Null uses the default from preferences, like a single episode.</param>
public sealed record CreateSeason(string? Topic, CefrLevel? Level, int EpisodeCount, int? DurationMin, EpisodeStyle Style, decimal? SpeakingRate);

public sealed record SeasonDetail(Season Season, SeasonArcResponse? Arc, IReadOnlyList<EpisodeListItem> Episodes);

public interface ISeasonService
{
    /// <summary>Saves the season and queues episode 1, whose job plans the arc first.</summary>
    Task<(Season Season, Episode First)> CreateAsync(CreateSeason request, CancellationToken ct);

    Task<IReadOnlyList<SeasonSummary>> ListAsync(CancellationToken ct);

    Task<SeasonDetail> GetAsync(Guid seasonId, CancellationToken ct);

    /// <summary>Queues the next episode. It builds on the earlier ones, so the latest must be Ready.</summary>
    Task<Episode> CreateNextEpisodeAsync(Guid seasonId, CancellationToken ct);

    /// <summary>Deletes the season, all of its episodes and their audio.</summary>
    Task DeleteAsync(Guid seasonId, CancellationToken ct);
}

public class SeasonService(
    ISeasonRepository seasons,
    IEpisodeService episodes,
    IPreferencesService preferences,
    IAudioStorage storage,
    ICurrentUser currentUser,
    TimeProvider clock) : ISeasonService
{
    public const int MinEpisodes = 2;
    public const int MaxEpisodes = 12;

    public async Task<(Season Season, Episode First)> CreateAsync(CreateSeason request, CancellationToken ct)
    {
        var topic = request.Topic?.Trim() ?? "";
        var errors = new Dictionary<string, string[]>();
        if (topic.Length == 0 || topic.Length > EpisodeService.MaxTopicLength)
        {
            errors[nameof(request.Topic)] = [$"Enter a topic of 1 to {EpisodeService.MaxTopicLength} characters."];
        }

        if (request.EpisodeCount is < MinEpisodes or > MaxEpisodes)
        {
            errors[nameof(request.EpisodeCount)] = [$"A season has {MinEpisodes} to {MaxEpisodes} episodes."];
        }

        if (errors.Count > 0)
        {
            throw new RequestValidationException(errors);
        }

        var prefs = await preferences.GetAsync(ct);
        var now = clock.GetUtcNow();
        var season = new Season
        {
            Id = Guid.NewGuid(),
            UserId = currentUser.UserId,
            Title = topic.Length <= 200 ? topic : topic[..200],
            Topic = topic,
            Level = request.Level ?? prefs.DefaultLevel,
            Style = request.Style,
            EpisodeCount = request.EpisodeCount,
            DurationMin = request.DurationMin ?? prefs.DefaultDurationMin,
            SpeakingRate = request.SpeakingRate,
            CreatedAt = now,
            UpdatedAt = now,
        };

        // Duration, speed and budget are checked for the first episode; each later one is checked when it is made.
        var first = await episodes.CreateInSeasonAsync(season, 1, topic, ct);
        return (season, first);
    }

    public Task<IReadOnlyList<SeasonSummary>> ListAsync(CancellationToken ct) => seasons.ListAsync(currentUser.UserId, ct);

    public async Task<SeasonDetail> GetAsync(Guid seasonId, CancellationToken ct)
    {
        var season = await seasons.GetAsync(currentUser.UserId, seasonId, ct) ?? throw NotFound(seasonId);
        var items = await seasons.EpisodesAsync(currentUser.UserId, seasonId, ct);
        return new SeasonDetail(season, SeasonArcs.Read(season), items);
    }

    public async Task<Episode> CreateNextEpisodeAsync(Guid seasonId, CancellationToken ct)
    {
        var season = await seasons.GetAsync(currentUser.UserId, seasonId, ct) ?? throw NotFound(seasonId);
        if (SeasonArcs.Read(season) is not { } arc)
        {
            throw new ConflictException("The season plan isn't ready yet: it is made together with episode 1.");
        }

        var made = season.Episodes.Count;
        if (made >= season.EpisodeCount)
        {
            throw new ConflictException($"All {season.EpisodeCount} episodes of this season exist already.");
        }

        var latest = season.Episodes.OrderBy(e => e.SeasonOrder).Last();
        if (latest.Status != EpisodeStatus.Ready)
        {
            throw new ConflictException($"Episode {latest.SeasonOrder} isn't ready yet, and the next one builds on it.");
        }

        var next = await episodes.CreateInSeasonAsync(season, made + 1, arc.Episodes[made].Title, ct);
        season.UpdatedAt = clock.GetUtcNow();
        await seasons.SaveChangesAsync(ct);
        return next;
    }

    public async Task DeleteAsync(Guid seasonId, CancellationToken ct)
    {
        var season = await seasons.GetForDeleteAsync(currentUser.UserId, seasonId, ct) ?? throw NotFound(seasonId);
        if (season.Episodes.Any(e => e.Status is EpisodeStatus.Pending or EpisodeStatus.GeneratingOutline
                or EpisodeStatus.GeneratingSections or EpisodeStatus.GeneratingAudio))
        {
            throw new ConflictException("An episode of this season is being generated right now; delete the season once it is ready or has failed.");
        }

        var keys = season.Episodes
            .SelectMany(e => e.AudioSegments.Select(a => a.StorageKey).Append(e.AudioStorageKey))
            .OfType<string>()
            .ToList();
        seasons.Remove(season);
        await seasons.SaveChangesAsync(ct);

        foreach (var key in keys)
        {
            await storage.DeleteAsync(key, ct); // after the DB delete: a crash leaves orphan files, never broken episodes
        }
    }

    private static NotFoundException NotFound(Guid id) => new($"Season {id} was not found.");
}
