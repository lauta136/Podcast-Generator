using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using PodcastGenerator.Application.Llm;
using PodcastGenerator.Application.Prompts;
using PodcastGenerator.Domain.Entities;
using PodcastGenerator.Domain.Enums;

namespace PodcastGenerator.Application.Generation;

/// <summary>Builds LLM requests. Only episode settings and generated text go in: never personal data.</summary>
public static class PromptBuilder
{
    /// <summary>Recaps of at most this many earlier sections are sent, keeping context bounded.</summary>
    public const int MaxPreviousRecaps = 6;

    public const int PreviousEndingSentences = 3;

    public static LlmRequest Outline(Episode episode, int sectionCount, string? difficultyFeedback) => new(
        "outline",
        System(episode.Level),
        PromptLibrary.Render(PromptLibrary.OutlineTemplate, new Dictionary<string, string>
        {
            ["topic"] = episode.Topic,
            ["style"] = StyleDescription(episode.Style),
            ["target_words"] = episode.TargetWords.ToString(CultureInfo.InvariantCulture),
            ["section_count"] = sectionCount.ToString(CultureInfo.InvariantCulture),
            ["section_words"] = (episode.TargetWords / sectionCount).ToString(CultureInfo.InvariantCulture),
            ["feedback"] = difficultyFeedback ?? "",
            ["season"] = SeasonContext(episode),
        }));

    /// <summary>Earlier episodes whose summaries are sent in full; older ones appear only as arc lines.</summary>
    public const int SeasonSummariesSent = 3;

    public static LlmRequest SeasonArc(Season season) => new(
        "season-arc",
        $"You plan series for a German-learning podcast at CEFR level {season.Level}. Always reply with a single JSON object exactly as requested.",
        PromptLibrary.Render(PromptLibrary.SeasonArcTemplate, new Dictionary<string, string>
        {
            ["count"] = season.EpisodeCount.ToString(CultureInfo.InvariantCulture),
            ["level"] = season.Level.ToString(),
            ["topic"] = season.Topic,
            ["style"] = StyleDescription(season.Style),
            ["minutes"] = season.DurationMin.ToString(CultureInfo.InvariantCulture),
        }),
        Temperature: 0.7);

    /// <summary>
    /// For a season episode: the series plan, what earlier episodes said, and this episode's line. Empty otherwise.
    /// Needs <c>episode.Season</c> with its episodes loaded.
    /// </summary>
    public static string SeasonContext(Episode episode)
    {
        if (episode is not { Season: { } season, SeasonOrder: { } order } || SeasonArcs.Read(season) is not { } arc)
        {
            return "";
        }

        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"This is episode {order} of {arc.Episodes.Count} of the series \"{arc.Title}\" (series topic: {season.Topic}).");
        text.AppendLine("Series plan:");
        for (var i = 0; i < arc.Episodes.Count; i++)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"{i + 1}. {arc.Episodes[i].Title}: {arc.Episodes[i].Summary}");
        }

        var earlier = season.Episodes
            .Where(e => e.SeasonOrder < order && !string.IsNullOrWhiteSpace(e.Summary))
            .OrderBy(e => e.SeasonOrder)
            .TakeLast(SeasonSummariesSent)
            .ToList();
        if (earlier.Count > 0)
        {
            text.AppendLine("What the most recent earlier episodes actually said (build on it, don't repeat it):");
            foreach (var e in earlier)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"- Episode {e.SeasonOrder}: {e.Summary}");
            }
        }

        text.Append(CultureInfo.InvariantCulture, $"Plan only episode {order}: {arc.Episodes[order - 1].Summary}");
        return text.ToString();
    }


    /// <param name="previous">Earlier completed sections, in order. Only their recaps and the last sentences are used.</param>
    public static LlmRequest Section(Episode episode, IReadOnlyList<EpisodeSection> allSections, EpisodeSection section, IReadOnlyList<EpisodeSection> previous)
    {
        var outline = new StringBuilder();
        foreach (var s in allSections.OrderBy(s => s.Order))
        {
            outline.AppendLine(CultureInfo.InvariantCulture, $"{s.Order + 1}. {s.Title}: {s.Summary}");
        }

        var recaps = previous
            .TakeLast(MaxPreviousRecaps)
            .Select(s => $"- {s.Order + 1}. {s.Recap}")
            .ToList();

        var lastSection = previous.Count > 0 ? previous[^1] : null;
        var ending = lastSection is null
            ? "(This is the first section.)"
            : string.Join(" ", lastSection.Segments.OrderBy(t => t.Order).TakeLast(PreviousEndingSentences).Select(t => t.Text));

        var isFirst = section.Order == 0 && !section.IsContinuation;
        var isLast = section.Order == allSections.Count - 1 && !section.IsContinuation;
        var position = SeasonPosition(episode, isFirst, isLast) ?? (isFirst, isLast) switch
        {
            (true, true) => "Open the episode with a short greeting and introduction, and end with a short goodbye.",
            (true, false) => "Open the episode with a short greeting and introduce the topic. Do not say goodbye.",
            (false, true) => "Continue smoothly from the previous section and end the episode with a short wrap-up and goodbye.",
            _ => "Continue smoothly from the previous section. No greeting and no goodbye.",
        };

        return new LlmRequest(
            "section",
            System(episode.Level),
            PromptLibrary.Render(PromptLibrary.SectionTemplate, new Dictionary<string, string>
            {
                ["section_number"] = (section.Order + 1).ToString(CultureInfo.InvariantCulture),
                ["section_count"] = allSections.Count.ToString(CultureInfo.InvariantCulture),
                ["episode_title"] = episode.Title ?? episode.Topic,
                ["outline"] = outline.ToString().TrimEnd(),
                ["section_title"] = section.Title,
                ["section_summary"] = section.Summary,
                ["section_focus"] = section.Focus ?? "none in particular",
                ["target_words"] = section.TargetWords.ToString(CultureInfo.InvariantCulture),
                ["paragraph_count"] = Math.Max(2, (int)Math.Round(section.TargetWords / 100.0)).ToString(CultureInfo.InvariantCulture),
                ["position_instructions"] = position,
                ["previous_recaps"] = recaps.Count > 0 ? string.Join("\n", recaps) : "(nothing yet)",
                ["previous_ending"] = ending,
            }));
    }

    /// <summary>Greeting and goodbye for season episodes: name the part, recall the last one, tease the next.</summary>
    private static string? SeasonPosition(Episode episode, bool isFirst, bool isLast)
    {
        if (episode is not { Season: { } season, SeasonOrder: { } order } || SeasonArcs.Read(season) is not { } arc || !(isFirst || isLast))
        {
            return null;
        }

        var opening = order == 1
            ? $"Open the episode with a short greeting and introduce the series \"{arc.Title}\" and this first part."
            : $"Open the episode with a short greeting, say this is part {order} of the series \"{arc.Title}\", and recall in one or two sentences where the last part ended.";
        var closing = order < arc.Episodes.Count
            ? $"End with a short wrap-up, a one-sentence teaser of the next part (\"{arc.Episodes[order].Title}\") and a goodbye."
            : "End with a short wrap-up of the whole series and a goodbye.";
        return (isFirst, isLast) switch
        {
            (true, true) => $"{opening} {closing}",
            (true, false) => $"{opening} Do not say goodbye.",
            _ => $"Continue smoothly from the previous section. {closing}",
        };
    }

    public static LlmRequest Proofread(Episode episode, IReadOnlyList<string> paragraphs) => new(
        "proofread",
        $"You are a careful German copy editor for language-learning material at CEFR level {episode.Level}. Always reply with a single JSON object exactly as requested.",
        PromptLibrary.Render(PromptLibrary.ProofreadTemplate, new Dictionary<string, string>
        {
            ["level"] = episode.Level.ToString(),
            ["paragraph_count"] = paragraphs.Count.ToString(CultureInfo.InvariantCulture),
            ["paragraphs_json"] = JsonSerializer.Serialize(paragraphs, ProofreadJson),
        }),
        Temperature: 0.1);

    public static LlmRequest Translate(CefrLevel level, IReadOnlyList<string> sentences, string languageName) => new(
        "translate",
        $"You translate German language-learning material into {languageName} for learners. Always reply with a single JSON object exactly as requested.",
        PromptLibrary.Render(PromptLibrary.TranslateTemplate, new Dictionary<string, string>
        {
            ["level"] = level.ToString(),
            ["language"] = languageName,
            ["count"] = sentences.Count.ToString(CultureInfo.InvariantCulture),
            ["sentences_json"] = JsonSerializer.Serialize(sentences, ProofreadJson),
        }),
        Temperature: 0.2,
        Interactive: true);

    // Keep umlauts and quotes readable for the model instead of \u escapes.
    private static readonly JsonSerializerOptions ProofreadJson = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
    };

    /// <summary>Turns recent ratings at the same level into a nudge that stays within the level.</summary>
    public static string? DifficultyFeedback(CefrLevel level, IReadOnlyList<DifficultyRating> recentRatings)
    {
        if (recentRatings.Count == 0)
        {
            return null;
        }

        var tooHard = recentRatings.Count(r => r == DifficultyRating.TooHard);
        var tooEasy = recentRatings.Count(r => r == DifficultyRating.TooEasy);
        var half = recentRatings.Count / 2.0;

        if (tooHard > half)
        {
            return $"Listener feedback: recent {level} episodes felt too hard. Stay at {level}, but toward its easier end: shorter sentences, more common words, more context for new words.";
        }

        if (tooEasy > half)
        {
            return $"Listener feedback: recent {level} episodes felt too easy. Stay at {level}, but toward its harder end: more varied sentence structure and richer topic vocabulary.";
        }

        return null;
    }

    private static string System(CefrLevel level) => PromptLibrary.Render(PromptLibrary.SystemTemplate, new Dictionary<string, string>
    {
        ["level"] = level.ToString(),
        ["cefr_guidelines"] = PromptLibrary.CefrGuidelines(level),
    });

    private static string StyleDescription(EpisodeStyle style) => style switch
    {
        EpisodeStyle.Storytelling => "storytelling: tell it as a story with people, places and a narrative arc",
        EpisodeStyle.Explainer => "explainer: like a clear news or science explainer, structured and informative",
        _ => "conversational monologue: relaxed, personal, like chatting with a friend",
    };
}
