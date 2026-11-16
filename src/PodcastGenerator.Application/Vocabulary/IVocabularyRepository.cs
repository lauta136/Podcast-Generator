using PodcastGenerator.Domain.Entities;
using PodcastGenerator.Domain.Enums;

namespace PodcastGenerator.Application.Vocabulary;

/// <summary>A transcript sentence the user owns, with what a flashcard needs from it.</summary>
public sealed record SourceSentence(Guid SegmentId, Guid EpisodeId, string Text, CefrLevel Level);

/// <param name="StartSec">Where the sentence starts in its episode's audio; null once the episode is gone.</param>
public sealed record VocabularyEntry(VocabularyItem Item, string? EpisodeTitle, double? StartSec);

public sealed record VocabularyPage(IReadOnlyList<VocabularyEntry> Items, int Total, int Due);

public interface IVocabularyRepository
{
    Task<SourceSentence?> GetSentenceAsync(Guid userId, Guid segmentId, CancellationToken ct);

    /// <summary>The user's card with the same dictionary form (case-insensitive), if any.</summary>
    Task<VocabularyItem?> FindByBaseFormAsync(Guid userId, string baseForm, CancellationToken ct);

    Task<VocabularyItem?> GetAsync(Guid userId, Guid itemId, CancellationToken ct);

    /// <summary>Newest first; Due counts every card due now, not just this page.</summary>
    Task<VocabularyPage> ListAsync(Guid userId, string? search, int page, int pageSize, DateTimeOffset now, CancellationToken ct);

    /// <summary>Cards due now, most overdue first.</summary>
    Task<IReadOnlyList<VocabularyEntry>> DueAsync(Guid userId, DateTimeOffset now, int take, CancellationToken ct);

    void Add(VocabularyItem item);

    void Remove(VocabularyItem item);

    Task SaveChangesAsync(CancellationToken ct);
}
