using PodcastGenerator.Domain.Enums;

namespace PodcastGenerator.Application;

/// <summary>Bound from the "Episodes" configuration section.</summary>
public class EpisodeOptions
{
    public const string SectionName = "Episodes";

    public int MinDurationMin { get; set; } = 5;
    public int MaxDurationMin { get; set; } = 60;

    /// <summary>Speaking pace used to turn minutes into words. Slower for lower levels.</summary>
    public Dictionary<CefrLevel, int> WordsPerMinute { get; set; } = new()
    {
        [CefrLevel.A1] = 110,
        [CefrLevel.A2] = 115,
        [CefrLevel.B1] = 125,
        [CefrLevel.B2] = 140,
        [CefrLevel.C1] = 145,
        [CefrLevel.C2] = 150,
    };

    public int SectionTargetWords { get; set; } = 900;
    public int SectionMinWords { get; set; } = 600;
    public int SectionMaxWords { get; set; } = 1200;

    /// <summary>Below this share of the target length, one continuation section is added.</summary>
    public double MinLengthRatio { get; set; } = 0.85;

    /// <summary>Run a proofreading LLM pass on every section (one extra request per section).</summary>
    public bool Proofread { get; set; } = true;

    /// <summary>Paragraphs are split to at most this many characters (one TTS request each).</summary>
    public int MaxParagraphChars { get; set; } = 1000;
}
