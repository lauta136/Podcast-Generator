using PodcastGenerator.Application.Common;
using PodcastGenerator.Application.Episodes;
using PodcastGenerator.Application.Generation;
using PodcastGenerator.Application.Llm;
using PodcastGenerator.Application.Preferences;

namespace PodcastGenerator.Application.Translation;

public sealed record SentenceTranslation(Guid SegmentId, string Translation);

public sealed record ParagraphTranslation(string Language, IReadOnlyList<SentenceTranslation> Sentences);

public interface ITranslationService
{
    /// <summary>
    /// Translates the paragraph that contains <paramref name="segmentId"/> into the user's native language.
    /// One LLM request per paragraph: the model gets context, and the other sentences are then free. Cached.
    /// </summary>
    Task<ParagraphTranslation> TranslateParagraphAsync(Guid episodeId, Guid segmentId, CancellationToken ct);
}

public class TranslationService(
    IEpisodeRepository episodes,
    IPreferencesService preferences,
    ICurrentUser currentUser,
    ILlmProvider llm) : ITranslationService
{
    public const int MaxTranslationLength = 2000;

    public async Task<ParagraphTranslation> TranslateParagraphAsync(Guid episodeId, Guid segmentId, CancellationToken ct)
    {
        var episode = await episodes.GetWithTranscriptAsync(currentUser.UserId, episodeId, ct)
            ?? throw new NotFoundException($"Episode {episodeId} was not found.");
        var section = episode.Sections.FirstOrDefault(s => s.Segments.Any(t => t.Id == segmentId))
            ?? throw new NotFoundException($"Sentence {segmentId} is not part of episode {episodeId}.");
        var paragraphIndex = section.Segments.Single(t => t.Id == segmentId).ParagraphIndex;
        var paragraph = section.Segments.Where(t => t.ParagraphIndex == paragraphIndex).OrderBy(t => t.Order).ToList();
        var language = (await preferences.GetAsync(ct)).NativeLanguage;

        if (paragraph.Any(t => t.Translation is null || t.TranslationLanguage != language))
        {
            var reply = await ValidatedJson.GenerateAsync<TranslationResponse>(
                llm,
                PromptBuilder.Translate(episode.Level, [.. paragraph.Select(t => t.Text)], SupportedLanguages.NativeName(language)),
                r => r.Validate(paragraph.Count),
                ct);

            for (var i = 0; i < paragraph.Count; i++)
            {
                var text = TextSegmenter.StripControlChars(reply.Translations[i]).Trim();
                paragraph[i].Translation = text.Length <= MaxTranslationLength ? text : text[..MaxTranslationLength];
                paragraph[i].TranslationLanguage = language;
            }

            await episodes.SaveChangesAsync(ct);
        }

        return new ParagraphTranslation(language, [.. paragraph.Select(t => new SentenceTranslation(t.Id, t.Translation!))]);
    }
}
