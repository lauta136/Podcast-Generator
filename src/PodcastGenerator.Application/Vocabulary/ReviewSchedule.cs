using PodcastGenerator.Domain.Entities;

namespace PodcastGenerator.Application.Vocabulary;

public enum ReviewResult
{
    Again,
    GotIt,
}

/// <summary>
/// Leitner boxes: each "Got it" moves a card one box up, to a longer gap; "Again" sends it back to box 0, due in a
/// few minutes. Simple and predictable, which suits a handful of new words per episode.
/// </summary>
public static class ReviewSchedule
{
    /// <summary>Gap after a "Got it" that lands the card in box i (box 0 is the learning box).</summary>
    public static readonly TimeSpan[] Gaps =
    [
        TimeSpan.Zero,
        TimeSpan.FromDays(1),
        TimeSpan.FromDays(3),
        TimeSpan.FromDays(7),
        TimeSpan.FromDays(14),
        TimeSpan.FromDays(30),
        TimeSpan.FromDays(60),
    ];

    public static readonly TimeSpan AgainGap = TimeSpan.FromMinutes(10);

    public static int TopBox => Gaps.Length - 1;

    public static void Apply(VocabularyItem item, ReviewResult result, DateTimeOffset now)
    {
        item.Box = result == ReviewResult.GotIt ? Math.Min(item.Box + 1, TopBox) : 0;
        item.DueAt = now + (result == ReviewResult.GotIt ? Gaps[item.Box] : AgainGap);
        item.LastReviewedAt = now;
        item.ReviewCount++;
    }
}
