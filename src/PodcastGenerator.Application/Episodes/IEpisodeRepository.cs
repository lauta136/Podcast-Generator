using PodcastGenerator.Domain.Entities;
using PodcastGenerator.Domain.Enums;

namespace PodcastGenerator.Application.Episodes;

public interface IEpisodeRepository
{
    void Add(Episode episode, GenerationJob job);

    Task<Episode?> GetAsync(Guid userId, Guid episodeId, CancellationToken ct);

    /// <summary>Episode with sections and transcript segments, owned by the user.</summary>
    Task<Episode?> GetWithTranscriptAsync(Guid userId, Guid episodeId, CancellationToken ct);

    /// <summary>For the background pipeline (no user scope).</summary>
    Task<Episode?> GetForGenerationAsync(Guid episodeId, CancellationToken ct);

    Task<GenerationJob?> GetJobAsync(Guid episodeId, CancellationToken ct);

    Task<IReadOnlyList<DifficultyRating>> RecentRatingsAsync(Guid userId, CefrLevel level, int take, CancellationToken ct);

    /// <summary>Newest first, with the user's listening progress. Total counts all matches, not just the page.</summary>
    Task<EpisodePage> ListAsync(Guid userId, EpisodeQuery query, CancellationToken ct);

    Task<ListeningProgress?> GetProgressAsync(Guid userId, Guid episodeId, CancellationToken ct);

    void AddProgress(ListeningProgress progress);

    void Remove(Episode episode);

    Task SaveChangesAsync(CancellationToken ct);
}
