using Microsoft.EntityFrameworkCore;
using PodcastGenerator.Application.Episodes;
using PodcastGenerator.Domain.Entities;
using PodcastGenerator.Domain.Enums;

namespace PodcastGenerator.Infrastructure.Persistence.Repositories;

public class EpisodeRepository(AppDbContext db) : IEpisodeRepository
{
    public void Add(Episode episode, GenerationJob job)
    {
        db.Episodes.Add(episode);
        db.GenerationJobs.Add(job);
    }

    public Task<Episode?> GetAsync(Guid userId, Guid episodeId, CancellationToken ct) =>
        db.Episodes.SingleOrDefaultAsync(e => e.Id == episodeId && e.UserId == userId, ct);

    public Task<Episode?> GetWithTranscriptAsync(Guid userId, Guid episodeId, CancellationToken ct) =>
        WithTranscript().SingleOrDefaultAsync(e => e.Id == episodeId && e.UserId == userId, ct);

    public Task<Episode?> GetForGenerationAsync(Guid episodeId, CancellationToken ct) =>
        WithTranscript().SingleOrDefaultAsync(e => e.Id == episodeId, ct);

    public Task<GenerationJob?> GetJobAsync(Guid episodeId, CancellationToken ct) =>
        db.GenerationJobs.SingleOrDefaultAsync(j => j.EpisodeId == episodeId, ct);

    public async Task<IReadOnlyList<DifficultyRating>> RecentRatingsAsync(Guid userId, CefrLevel level, int take, CancellationToken ct) =>
        await db.Episodes
            .Where(e => e.UserId == userId && e.Level == level && e.DifficultyRating != null)
            .OrderByDescending(e => e.UpdatedAt)
            .Take(take)
            .Select(e => e.DifficultyRating!.Value)
            .ToListAsync(ct);

    public async Task<EpisodePage> ListAsync(Guid userId, EpisodeQuery query, CancellationToken ct)
    {
        var episodes = db.Episodes.AsNoTracking().Where(e => e.UserId == userId);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // Escape LIKE wildcards: the user's text is matched literally.
            var pattern = "%" + query.Search.Trim().Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_") + "%";
            episodes = episodes.Where(e =>
                EF.Functions.ILike(e.Title ?? "", pattern, @"\") || EF.Functions.ILike(e.Topic, pattern, @"\"));
        }

        if (query.Level is { } level)
        {
            episodes = episodes.Where(e => e.Level == level);
        }

        if (query.Status is { } status)
        {
            episodes = episodes.Where(e => e.Status == status);
        }

        var total = await episodes.CountAsync(ct);
        var rows = await episodes
            .OrderByDescending(e => e.CreatedAt)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(e => new
            {
                Episode = e,
                Progress = db.ListeningProgress.FirstOrDefault(p => p.UserId == userId && p.EpisodeId == e.Id),
            })
            .ToListAsync(ct);

        return new EpisodePage([.. rows.Select(r => new EpisodeListItem(r.Episode, r.Progress))], total, query.Page, query.PageSize);
    }

    public Task<ListeningProgress?> GetProgressAsync(Guid userId, Guid episodeId, CancellationToken ct) =>
        db.ListeningProgress.SingleOrDefaultAsync(p => p.UserId == userId && p.EpisodeId == episodeId, ct);

    public void AddProgress(ListeningProgress progress) => db.ListeningProgress.Add(progress);

    public void Remove(Episode episode) => db.Episodes.Remove(episode);

    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);

    private IQueryable<Episode> WithTranscript() =>
        db.Episodes
            .Include(e => e.Sections).ThenInclude(s => s.Segments)
            .Include(e => e.AudioSegments)
            .AsSplitQuery();
}
