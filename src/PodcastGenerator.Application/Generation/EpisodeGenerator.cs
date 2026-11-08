using Microsoft.Extensions.Options;
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

public class EpisodeGenerator(
    IEpisodeRepository episodes,
    ILlmProvider llm,
    IOptions<EpisodeOptions> options,
    TimeProvider clock) : IEpisodeGenerator
{
    public const int RecentRatingsConsidered = 5;
    private const int MaxRecapLength = 2000;

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

        // Audio synthesis is added as the next pipeline step in the TTS slice.
        var recaps = string.Join(" ", Ordered(episode).Select(s => s.Recap));
        episode.Summary = recaps.Length <= MaxRecapLength ? recaps : recaps[..MaxRecapLength];
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
                Summary = Limit(s.Summary.Trim(), 2000),
                Focus = s.Focus is null ? null : Limit(s.Focus.Trim(), 300),
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
            var all = Ordered(episode).ToList();
            var previous = all.Where(s => s.Order < section.Order && s.Status == StepStatus.Completed).ToList();

            var response = await ValidatedJson.GenerateAsync<SectionResponse>(
                llm, PromptBuilder.Section(episode, all, section, previous), r => r.Validate(section.TargetWords), ct);

            var paragraphs = TextSegmenter.FitParagraphs(
                response.Paragraphs.Select(TextSegmenter.CleanForSpeech).Where(p => p.Length > 0),
                options.Value.MaxParagraphChars);

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
            section.Recap = Limit(response.Recap.Trim(), MaxRecapLength);
            section.Status = StepStatus.Completed;
            section.LastError = null;
            await episodes.SaveChangesAsync(ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            section.Status = StepStatus.Failed;
            section.LastError = GenerationState.Truncate(e.Message);
            await episodes.SaveChangesAsync(CancellationToken.None);
            throw;
        }
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

        var closing = Ordered(episode).Last();
        var insertAt = closing.Order;
        closing.Order = insertAt + 1;
        await episodes.SaveChangesAsync(ct); // free the slot first: (EpisodeId, Order) is unique

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
}
