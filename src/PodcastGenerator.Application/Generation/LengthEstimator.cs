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

        return Math.Max(1, count);
    }

    public static int CountWords(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
}
