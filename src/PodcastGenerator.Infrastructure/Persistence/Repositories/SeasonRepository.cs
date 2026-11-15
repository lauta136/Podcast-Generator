using Microsoft.EntityFrameworkCore;
using PodcastGenerator.Application.Episodes;
using PodcastGenerator.Application.Seasons;
using PodcastGenerator.Domain.Entities;
using PodcastGenerator.Domain.Enums;

namespace PodcastGenerator.Infrastructure.Persistence.Repositories;

public class SeasonRepository(AppDbContext db) : ISeasonRepository
{
    public Task<Season?> GetAsync(Guid userId, Guid seasonId, CancellationToken ct) =>
        db.Seasons.Include(s => s.Episodes).SingleOrDefaultAsync(s => s.Id == seasonId && s.UserId == userId, ct);

    public async Task<IReadOnlyList<SeasonSummary>> ListAsync(Guid userId, CancellationToken ct)
    {
        var rows = await db.Seasons.AsNoTracking()
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new
            {
                Season = s,
                Created = s.Episodes.Count,
                Ready = s.Episodes.Count(e => e.Status == EpisodeStatus.Ready),
                Finished = s.Episodes.Count(e => db.ListeningProgress.Any(p => p.UserId == userId && p.EpisodeId == e.Id && p.Completed)),
            })
            .ToListAsync(ct);
        return [.. rows.Select(r => new SeasonSummary(r.Season, r.Created, r.Ready, r.Finished))];
    }

    public async Task<IReadOnlyList<EpisodeListItem>> EpisodesAsync(Guid userId, Guid seasonId, CancellationToken ct)
    {
        var rows = await db.Episodes.AsNoTracking()
            .Where(e => e.SeasonId == seasonId && e.UserId == userId)
            .OrderBy(e => e.SeasonOrder)
            .Select(e => new
            {
                Episode = e,
                Progress = db.ListeningProgress.FirstOrDefault(p => p.UserId == userId && p.EpisodeId == e.Id),
                SeasonTitle = e.Season != null ? e.Season.Title : null,
            })
            .ToListAsync(ct);
        return [.. rows.Select(r => new EpisodeListItem(r.Episode, r.Progress, r.SeasonTitle))];
    }

    public Task<Season?> GetForDeleteAsync(Guid userId, Guid seasonId, CancellationToken ct) =>
        db.Seasons
            .Include(s => s.Episodes).ThenInclude(e => e.AudioSegments)
            .AsSplitQuery()
            .SingleOrDefaultAsync(s => s.Id == seasonId && s.UserId == userId, ct);

    public void Remove(Season season)
    {
        // Episodes would only be detached (SetNull) by the season delete; a season's episodes go with it.
        db.Episodes.RemoveRange(season.Episodes);
        db.Seasons.Remove(season);
    }

    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
