using PodcastGenerator.Domain.Enums;

namespace PodcastGenerator.Application.Generation;

public static class LengthEstimator
{
    /// <summary>Words that fill <paramref name="durationMin"/> minutes at the voice's pace and speed.</summary>
    public static int TargetWords(int durationMin, double wordsPerMinute) =>
        (int)Math.Round(durationMin * wordsPerMinute);

    /// <summary>Number of sections so each lands near the target size and within min/max where possible.</summary>
    public static int SectionCount(int targetWords, EpisodeOptions options)
    {
        var count = (int)Math.Ceiling(targetWords / (double)options.SectionTargetWords);
        if (count > 1 && targetWords / count < options.SectionMinWords)
        {
            count = Math.Max(1, targetWords / options.SectionMinWords);
        }

        // Short episodes too get an opening and a closing section, so a continuation can go in between if needed.
        if (count == 1 && targetWords >= 2 * options.ShortSectionMinWords)
        {
            count = 2;
        }

        return Math.Max(1, count);
    }

    /// <summary>Words to ask a section for, so that what the model actually writes comes out at <paramref name="words"/>.</summary>
    public static int SectionAsk(int words, EpisodeOptions options) =>
        words < options.WrittenShareFromWords ? words : (int)Math.Round(words / Math.Clamp(options.WrittenShare, 0.5, 1.0));

    public static int CountWords(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
}
