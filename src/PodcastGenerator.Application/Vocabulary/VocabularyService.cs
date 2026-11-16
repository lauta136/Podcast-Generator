using PodcastGenerator.Application.Common;
using PodcastGenerator.Application.Generation;
using PodcastGenerator.Application.Llm;
using PodcastGenerator.Application.Preferences;
using PodcastGenerator.Domain.Entities;

namespace PodcastGenerator.Application.Vocabulary;

/// <param name="Created">False when the word was already saved: the existing card is returned.</param>
public sealed record SavedWord(VocabularyEntry Entry, bool Created);

public interface IVocabularyService
{
    /// <summary>Saves an expression selected in a transcript sentence, with its dictionary form and meaning there.</summary>
    Task<SavedWord> SaveAsync(Guid segmentId, string text, CancellationToken ct);

    Task<VocabularyPage> ListAsync(string? search, int page, int pageSize, CancellationToken ct);

    Task<IReadOnlyList<VocabularyEntry>> DueAsync(int take, CancellationToken ct);

    Task<VocabularyItem> ReviewAsync(Guid itemId, ReviewResult result, CancellationToken ct);

    Task DeleteAsync(Guid itemId, CancellationToken ct);
}

public class VocabularyService(
    IVocabularyRepository vocabulary,
    IPreferencesService preferences,
    ICurrentUser currentUser,
    ILlmProvider llm,
    TimeProvider clock) : IVocabularyService
{
    public const int MaxTextLength = 80;
    public const int MaxWords = 6;
    public const int MaxPageSize = 100;
    public const int MaxReviewBatch = 50;

    private static readonly char[] Trim = [' ', '.', ',', ';', ':', '!', '?', '"', '\'', '„', '“', '”', '«', '»', '(', ')', '–', '-'];

    public async Task<SavedWord> SaveAsync(Guid segmentId, string text, CancellationToken ct)
    {
        var sentence = await vocabulary.GetSentenceAsync(currentUser.UserId, segmentId, ct)
            ?? throw new NotFoundException($"Sentence {segmentId} was not found.");

        // Only text that is really in the sentence: the selection is what goes into the prompt.
        var expression = TextSegmenter.StripControlChars(text).Trim(Trim);
        if (expression.Length == 0 || expression.Length > MaxTextLength || LengthEstimator.CountWords(expression) > MaxWords)
        {
            throw new RequestValidationException("Text", $"Select a word or a phrase of up to {MaxWords} words.");
        }

        if (!sentence.Text.Contains(expression, StringComparison.OrdinalIgnoreCase))
        {
            throw new RequestValidationException("Text", "Select the word inside the sentence.");
        }

        var language = (await preferences.GetAsync(ct)).NativeLanguage;
        var card = await ValidatedJson.GenerateAsync<VocabCardResponse>(
            llm,
            PromptBuilder.VocabCard(sentence.Level, sentence.Text, expression, SupportedLanguages.NativeName(language)),
            r => r.Validate(),
            ct);
        var baseForm = TextSegmenter.CleanForSpeech(card.BaseForm);

        if (await vocabulary.FindByBaseFormAsync(currentUser.UserId, baseForm, ct) is { } existing)
        {
            return new SavedWord(new VocabularyEntry(existing, null, null), Created: false);
        }

        var now = clock.GetUtcNow();
        var item = new VocabularyItem
        {
            Id = Guid.NewGuid(),
            UserId = currentUser.UserId,
            EpisodeId = sentence.EpisodeId,
            SegmentId = sentence.SegmentId,
            Text = expression,
            BaseForm = baseForm,
            Translation = TextSegmenter.StripControlChars(card.Translation).Trim(),
            TranslationLanguage = language,
            Context = sentence.Text,
            DueAt = now, // new cards are due right away
            CreatedAt = now,
        };
        vocabulary.Add(item);
        await vocabulary.SaveChangesAsync(ct);
        return new SavedWord(new VocabularyEntry(item, null, null), Created: true);
    }

    public Task<VocabularyPage> ListAsync(string? search, int page, int pageSize, CancellationToken ct)
    {
        if (page < 1 || pageSize is < 1 or > MaxPageSize)
        {
            throw new RequestValidationException("Page", $"Pages start at 1 and hold 1 to {MaxPageSize} words.");
        }

        return vocabulary.ListAsync(currentUser.UserId, search?.Trim(), page, pageSize, clock.GetUtcNow(), ct);
    }

    public Task<IReadOnlyList<VocabularyEntry>> DueAsync(int take, CancellationToken ct) =>
        vocabulary.DueAsync(currentUser.UserId, clock.GetUtcNow(), Math.Clamp(take, 1, MaxReviewBatch), ct);

    public async Task<VocabularyItem> ReviewAsync(Guid itemId, ReviewResult result, CancellationToken ct)
    {
        var item = await vocabulary.GetAsync(currentUser.UserId, itemId, ct) ?? throw NotFound(itemId);
        ReviewSchedule.Apply(item, result, clock.GetUtcNow());
        await vocabulary.SaveChangesAsync(ct);
        return item;
    }

    public async Task DeleteAsync(Guid itemId, CancellationToken ct)
    {
        var item = await vocabulary.GetAsync(currentUser.UserId, itemId, ct) ?? throw NotFound(itemId);
        vocabulary.Remove(item);
        await vocabulary.SaveChangesAsync(ct);
    }

    private static NotFoundException NotFound(Guid id) => new($"Word {id} was not found.");
}
