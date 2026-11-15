using PodcastGenerator.Application.Episodes;
using PodcastGenerator.Domain.Entities;

namespace PodcastGenerator.Application.Seasons;

public sealed record SeasonSummary(Season Season, int EpisodesCreated, int EpisodesReady, int EpisodesFinished);

public interface ISeasonRepository
{
    /// <summary>The user's season with its episodes (no transcripts).</summary>
    Task<Season?> GetAsync(Guid userId, Guid seasonId, CancellationToken ct);

    /// <summary>Newest first, with episode counts.</summary>
    Task<IReadOnlyList<SeasonSummary>> ListAsync(Guid userId, CancellationToken ct);

    /// <summary>The season's episodes in order, with the user's listening progress.</summary>
    Task<IReadOnlyList<EpisodeListItem>> EpisodesAsync(Guid userId, Guid seasonId, CancellationToken ct);

    /// <summary>The season with episodes and their audio chunks, for deleting files.</summary>
    Task<Season?> GetForDeleteAsync(Guid userId, Guid seasonId, CancellationToken ct);

    void Remove(Season season);

    Task SaveChangesAsync(CancellationToken ct);
}
