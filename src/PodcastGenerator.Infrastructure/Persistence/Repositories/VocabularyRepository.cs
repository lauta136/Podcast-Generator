using Microsoft.EntityFrameworkCore;
using PodcastGenerator.Application.Vocabulary;
using PodcastGenerator.Domain.Entities;

namespace PodcastGenerator.Infrastructure.Persistence.Repositories;

public class VocabularyRepository(AppDbContext db) : IVocabularyRepository
{
    public Task<SourceSentence?> GetSentenceAsync(Guid userId, Guid segmentId, CancellationToken ct) =>
        (from t in db.TranscriptSegments
         join s in db.EpisodeSections on t.SectionId equals s.Id
         join e in db.Episodes on s.EpisodeId equals e.Id
         where t.Id == segmentId && e.UserId == userId
         select new SourceSentence(t.Id, e.Id, t.Text, e.Level))
        .SingleOrDefaultAsync(ct);

    public Task<VocabularyItem?> FindByBaseFormAsync(Guid userId, string baseForm, CancellationToken ct)
    {
        var exact = Escape(baseForm); // ILIKE without wildcards: an exact, case-insensitive match
        return db.VocabularyItems.FirstOrDefaultAsync(v => v.UserId == userId && EF.Functions.ILike(v.BaseForm, exact, @"\"), ct);
    }

    public Task<VocabularyItem?> GetAsync(Guid userId, Guid itemId, CancellationToken ct) =>
        db.VocabularyItems.SingleOrDefaultAsync(v => v.Id == itemId && v.UserId == userId, ct);

    public async Task<VocabularyPage> ListAsync(Guid userId, string? search, int page, int pageSize, DateTimeOffset now, CancellationToken ct)
    {
        var mine = db.VocabularyItems.AsNoTracking().Where(v => v.UserId == userId);
        var due = await mine.CountAsync(v => v.DueAt <= now, ct);
        if (!string.IsNullOrEmpty(search))
        {
            var pattern = "%" + Escape(search) + "%";
            mine = mine.Where(v => EF.Functions.ILike(v.BaseForm, pattern, @"\")
                || EF.Functions.ILike(v.Text, pattern, @"\")
                || EF.Functions.ILike(v.Translation, pattern, @"\"));
        }

        var total = await mine.CountAsync(ct);
        var items = await WithSource(mine.OrderByDescending(v => v.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize)).ToListAsync(ct);
        return new VocabularyPage(items, total, due);
    }

    public async Task<IReadOnlyList<VocabularyEntry>> DueAsync(Guid userId, DateTimeOffset now, int take, CancellationToken ct) =>
        await WithSource(db.VocabularyItems.AsNoTracking()
                .Where(v => v.UserId == userId && v.DueAt <= now)
                .OrderBy(v => v.DueAt)
                .Take(take))
            .ToListAsync(ct);

    public void Add(VocabularyItem item) => db.VocabularyItems.Add(item);

    public void Remove(VocabularyItem item) => db.VocabularyItems.Remove(item);

    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);

    /// <summary>Makes LIKE wildcards in user text literal.</summary>
    private static string Escape(string text) => text.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");

    /// <summary>Adds the episode title and where the sentence starts, for "hear it in the episode".</summary>
    private IQueryable<VocabularyEntry> WithSource(IQueryable<VocabularyItem> items) =>
        from v in items
        select new VocabularyEntry(
            v,
            db.Episodes.Where(e => e.Id == v.EpisodeId).Select(e => e.Title).FirstOrDefault(),
            db.TranscriptSegments.Where(t => t.Id == v.SegmentId).Select(t => t.StartSec).FirstOrDefault());
}
