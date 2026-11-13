using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PodcastGenerator.Application.Audio;
using PodcastGenerator.Application.Episodes;
using PodcastGenerator.Application.Llm;
using PodcastGenerator.Domain.Entities;
using PodcastGenerator.Domain.Enums;

namespace PodcastGenerator.Application.Generation;

public interface IEpisodeGenerator
{
    /// <summary>Runs every unfinished step. Completed steps are skipped, so this is safe to call again after a crash.</summary>
    Task RunAsync(Guid episodeId, CancellationToken ct);
}

public partial class EpisodeGenerator(
    IEpisodeRepository episodes,
    ILlmProvider llm,
    IOptions<EpisodeOptions> options,
    AudioGenerator audio,
    TimeProvider clock,
    ILogger<EpisodeGenerator> logger) : IEpisodeGenerator
{
    public const int RecentRatingsConsidered = 5;
    private const int MaxRecapLength = 2000;

    private static readonly JsonSerializerOptions DraftJson = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public async Task RunAsync(Guid episodeId, CancellationToken ct)
    {
        var episode = await episodes.GetForGenerationAsync(episodeId, ct)
            ?? throw new InvalidOperationException($"Episode {episodeId} not found.");

        if (episode.Sections.Count == 0)
        {
            await SetStatusAsync(episode, EpisodeStatus.GeneratingOutline, ct);
            await GenerateOutlineAsync(episode, ct);
        }

        await SetStatusAsync(episode, EpisodeStatus.GeneratingSections, ct);
        await GeneratePendingSectionsAsync(episode, ct);

        if (await AddContinuationIfShortAsync(episode, ct))
        {
            await GeneratePendingSectionsAsync(episode, ct);
        }

        var recaps = string.Join(" ", Ordered(episode).Select(s => s.Recap));
        episode.Summary = recaps.Length <= MaxRecapLength ? recaps : recaps[..MaxRecapLength];

        await SetStatusAsync(episode, EpisodeStatus.GeneratingAudio, ct);
        await audio.RunAsync(episode, ct);

        await SetStatusAsync(episode, EpisodeStatus.Ready, ct);
    }

    private async Task GenerateOutlineAsync(Episode episode, CancellationToken ct)
    {
        var ratings = await episodes.RecentRatingsAsync(episode.UserId, episode.Level, RecentRatingsConsidered, ct);
        var feedback = PromptBuilder.DifficultyFeedback(episode.Level, ratings);
        var count = LengthEstimator.SectionCount(episode.TargetWords, options.Value);

        var outline = await ValidatedJson.GenerateAsync<OutlineResponse>(
            llm, PromptBuilder.Outline(episode, count, feedback), r => r.Validate(count), ct);

        episode.Title = Limit(TextSegmenter.CleanForSpeech(outline.Title), 200);
        episode.Description = Limit(TextSegmenter.CleanForSpeech(outline.Description), 2000);
        for (var i = 0; i < outline.Sections.Count; i++)
        {
            var s = outline.Sections[i];
            episode.Sections.Add(new EpisodeSection
            {
                Order = i,
                Title = Limit(TextSegmenter.CleanForSpeech(s.Title), 200),
                Summary = Limit(TextSegmenter.StripControlChars(s.Summary).Trim(), 2000),
                Focus = s.Focus is null ? null : Limit(TextSegmenter.StripControlChars(s.Focus).Trim(), 300),
                TargetWords = episode.TargetWords / count,
            });
        }

        await episodes.SaveChangesAsync(ct);
    }

    private async Task GeneratePendingSectionsAsync(Episode episode, CancellationToken ct)
    {
        foreach (var section in Ordered(episode).Where(s => s.Status != StepStatus.Completed).ToList())
        {
            await GenerateSectionAsync(episode, section, ct);
        }
    }

    private async Task GenerateSectionAsync(Episode episode, EpisodeSection section, CancellationToken ct)
    {
        section.Attempts++;
        try
        {
            // Step 1: write the text, unless a draft survived an earlier failed attempt.
            var draft = section.DraftJson is null
                ? await WriteDraftAsync(episode, section, ct)
                : JsonSerializer.Deserialize<SectionDraft>(section.DraftJson, DraftJson)!;

            // Step 2: proofread. Learners copy what they hear, so grammar slips are worth an extra request.
            var paragraphs = options.Value.Proofread ? await ProofreadAsync(episode, section, draft.Paragraphs, ct) : draft.Paragraphs;

            section.Segments.Clear();
            var order = 0;
            for (var p = 0; p < paragraphs.Count; p++)
            {
                foreach (var sentence in TextSegmenter.SplitSentences(paragraphs[p]))
                {
                    section.Segments.Add(new TranscriptSegment { Order = order++, ParagraphIndex = p, Text = sentence });
                }
            }

            section.WordCount = paragraphs.Sum(LengthEstimator.CountWords);
            section.Recap = Limit(draft.Recap.Trim(), MaxRecapLength);
            section.Status = StepStatus.Completed;
            section.LastError = null;
            await episodes.SaveChangesAsync(ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            section.Status = StepStatus.Failed;
            section.LastError = GenerationState.Truncate(e.Message);
            try
            {
                await episodes.SaveChangesAsync(CancellationToken.None);
            }
            catch (Exception saveError) when (saveError is not OperationCanceledException)
            {
                // If the failing call was itself a save, this one fails too. Keep the original error: the
                // worker records it on the episode through a fresh context.
                LogFailureNotSaved(logger, episode.Id, section.Order + 1, saveError.Message);
            }

            throw;
        }
    }

    /// <summary>
    /// Returns corrected paragraphs. If the proofreader keeps rewriting instead of correcting (invalid even after
    /// repair), the draft is kept: a few slips are better than an episode stuck forever. Provider outages still
    /// fail the step so a retry can proofread it later.
    /// </summary>
    private async Task<IReadOnlyList<string>> ProofreadAsync(
        Episode episode, EpisodeSection section, IReadOnlyList<string> paragraphs, CancellationToken ct)
    {
        try
        {
            var proofread = await ValidatedJson.GenerateAsync<ProofreadResponse>(
                llm, PromptBuilder.Proofread(episode, paragraphs), r => r.Validate(paragraphs), ct);
            section.CorrectionsJson = JsonSerializer.Serialize(
                proofread.Corrections
                    .Where(c => c.Original != c.Corrected)
                    .Select(c => new ProofreadCorrection
                    {
                        Original = TextSegmenter.StripControlChars(c.Original ?? ""),
                        Corrected = TextSegmenter.StripControlChars(c.Corrected ?? ""),
                    })
                    .ToList(),
                DraftJson);
            return TextSegmenter.FitParagraphs(
                proofread.Paragraphs.Select(TextSegmenter.CleanForSpeech).Where(p => p.Length > 0),
                options.Value.MaxParagraphChars);
        }
        catch (LlmInvalidResponseException e)
        {
            LogProofreadSkipped(logger, episode.Id, section.Order + 1, e.Message);
            section.CorrectionsJson = null;
            return paragraphs;
        }
    }

    private async Task<SectionDraft> WriteDraftAsync(Episode episode, EpisodeSection section, CancellationToken ct)
    {
        var all = Ordered(episode).ToList();
        var previous = all.Where(s => s.Order < section.Order && s.Status == StepStatus.Completed).ToList();

        var response = await ValidatedJson.GenerateAsync<SectionResponse>(
            llm, PromptBuilder.Section(episode, all, section, previous), r => r.Validate(section.TargetWords), ct);

        var paragraphs = TextSegmenter.FitParagraphs(
            response.Paragraphs.Select(TextSegmenter.CleanForSpeech).Where(p => p.Length > 0),
            options.Value.MaxParagraphChars);

        var draft = new SectionDraft(paragraphs, TextSegmenter.StripControlChars(response.Recap));
        section.DraftJson = JsonSerializer.Serialize(draft, DraftJson);
        await episodes.SaveChangesAsync(ct); // persisted before proofreading: never paid for twice
        return draft;
    }

    /// <summary>
    /// LLMs tend to undershoot. If the episode is clearly short, insert one continuation section before the
    /// closing section (so the goodbye stays last). Never more than one, and never by repeating content.
    /// </summary>
    private async Task<bool> AddContinuationIfShortAsync(Episode episode, CancellationToken ct)
    {
        var o = options.Value;
        var words = episode.Sections.Sum(s => s.WordCount ?? 0);
        // A single section both greets and says goodbye, so there is no middle to extend.
        if (episode.Sections.Count < 2
            || episode.Sections.Any(s => s.IsContinuation)
            || words >= episode.TargetWords * o.MinLengthRatio)
        {
            return false;
        }

        // Slot right after the last middle section. Computed from that section, not from the closing one, so a
        // crash between the two saves below (closing already moved) doesn't move it again and leave a gap.
        var ordered = Ordered(episode).ToList();
        var closing = ordered[^1];
        var insertAt = ordered[^2].Order + 1;
        if (closing.Order != insertAt + 1)
        {
            closing.Order = insertAt + 1;
            await episodes.SaveChangesAsync(ct); // free the slot first: (EpisodeId, Order) is unique
        }

        episode.Sections.Add(new EpisodeSection
        {
            Order = insertAt,
            IsContinuation = true,
            Title = "Noch mehr dazu",
            Summary = "Go deeper: new, related aspects, examples or stories about the topic that earlier sections did not cover yet.",
            Focus = null,
            TargetWords = Math.Max(o.SectionMinWords / 2, episode.TargetWords - words),
        });
        await episodes.SaveChangesAsync(ct);
        return true;
    }

    private async Task SetStatusAsync(Episode episode, EpisodeStatus status, CancellationToken ct)
    {
        if (episode.Status == status)
        {
            return;
        }

        episode.Status = status;
        episode.UpdatedAt = clock.GetUtcNow();
        await episodes.SaveChangesAsync(ct);
    }

    private static IEnumerable<EpisodeSection> Ordered(Episode episode) => episode.Sections.OrderBy(s => s.Order);

    private static string Limit(string s, int max) => s.Length <= max ? s : s[..max];

    [LoggerMessage(Level = LogLevel.Warning, Message = "Episode {EpisodeId} section {Section}: could not save the failure: {Reason}")]
    private static partial void LogFailureNotSaved(ILogger logger, Guid episodeId, int section, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Episode {EpisodeId} section {Section}: proofreading rejected, keeping the draft: {Reason}")]
    private static partial void LogProofreadSkipped(ILogger logger, Guid episodeId, int section, string reason);
}
