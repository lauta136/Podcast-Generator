using PodcastGenerator.Domain.Enums;

namespace PodcastGenerator.Application;

/// <summary>Bound from the "Episodes" configuration section.</summary>
public class EpisodeOptions
{
    public const string SectionName = "Episodes";

    public int MinDurationMin { get; set; } = 5;
    public int MaxDurationMin { get; set; } = 60;

    /// <summary>
    /// Default voice speed per level (1.0 = the voice's natural pace). Level sets vocabulary and grammar; speed is
    /// separate, so learners can raise one without the other. Users override it in preferences or per episode.
    /// </summary>
    public Dictionary<CefrLevel, decimal> DefaultSpeakingRate { get; set; } = new()
    {
        [CefrLevel.A1] = 0.75m,
        [CefrLevel.A2] = 0.80m,
        [CefrLevel.B1] = 0.85m,
        [CefrLevel.B2] = 0.95m,
        [CefrLevel.C1] = 1.0m,
        [CefrLevel.C2] = 1.0m,
    };

    public decimal MinSpeakingRate { get; set; } = 0.6m;
    public decimal MaxSpeakingRate { get; set; } = 1.25m;

    public decimal SpeakingRateFor(CefrLevel level) => DefaultSpeakingRate.GetValueOrDefault(level, 1.0m);

    /// <summary>Null when valid. Two decimals at most, because that is what is stored and sent.</summary>
    public string? SpeakingRateError(decimal rate) =>
        rate < MinSpeakingRate || rate > MaxSpeakingRate ? $"Must be between {MinSpeakingRate} and {MaxSpeakingRate}."
        : decimal.Round(rate, 2) != rate ? "Use at most two decimals, e.g. 0.85."
        : null;

    public int SectionTargetWords { get; set; } = 900;
    public int SectionMinWords { get; set; } = 600;

    /// <summary>
    /// Episodes with room for two sections of at least this many words get two, never one. One long section varied
    /// a lot (67–97% of the ask in real runs) and, since it both greets and says goodbye, could never be topped up;
    /// sections of 300–370 words came out at 89–112%.
    /// </summary>
    public int ShortSectionMinWords { get; set; } = 300;

    /// <summary>
    /// Share of the requested words the model actually writes for a full-size section. Measured on 18 real sections
    /// of 600–900 words (2026-11): 80–103%, about 91% on average. Sections that size ask for their share divided by
    /// this, so episodes land on their target instead of 5–15% short.
    /// </summary>
    public double WrittenShare { get; set; } = 0.91;

    /// <summary>
    /// Smaller asks (continuations) are left as they are: at 300–370 words models wrote 95–112% of the ask, so
    /// inflating those would overshoot.
    /// </summary>
    public int WrittenShareFromWords { get; set; } = 500;
    public int SectionMaxWords { get; set; } = 1200;

    /// <summary>Below this share of the target length, one continuation section is added.</summary>
    public double MinLengthRatio { get; set; } = 0.85;

    /// <summary>Run a proofreading LLM pass on every section (one extra request per section).</summary>
    public bool Proofread { get; set; } = true;

    /// <summary>Paragraphs are split to at most this many characters (one TTS request each).</summary>
    public int MaxParagraphChars { get; set; } = 1000;
}
