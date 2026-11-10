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

    public void Remove(Episode episode) => db.Episodes.Remove(episode);

    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);

    private IQueryable<Episode> WithTranscript() =>
        db.Episodes
            .Include(e => e.Sections).ThenInclude(s => s.Segments)
            .Include(e => e.AudioSegments)
            .AsSplitQuery();
}
