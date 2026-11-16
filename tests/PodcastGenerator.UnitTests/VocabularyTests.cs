using PodcastGenerator.Application.Generation;
using PodcastGenerator.Application.Vocabulary;
using PodcastGenerator.Domain.Entities;
using PodcastGenerator.Domain.Enums;

namespace PodcastGenerator.UnitTests;

public class ReviewScheduleTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    private static VocabularyItem NewCard() => new() { Text = "x", BaseForm = "x", Translation = "x", TranslationLanguage = "en", Context = "x", DueAt = Now };

    [Fact]
    public void Each_got_it_moves_the_card_to_a_longer_gap_up_to_60_days()
    {
        var card = NewCard();
        var gaps = new List<double>();
        for (var i = 0; i < 8; i++)
        {
            ReviewSchedule.Apply(card, ReviewResult.GotIt, Now);
            gaps.Add((card.DueAt - Now).TotalDays);
        }

        Assert.Equal([1, 3, 7, 14, 30, 60, 60, 60], gaps);
        Assert.Equal(8, card.ReviewCount);
    }

    [Fact]
    public void Again_sends_the_card_back_to_the_start_for_ten_minutes()
    {
        var card = NewCard();
        ReviewSchedule.Apply(card, ReviewResult.GotIt, Now);
        ReviewSchedule.Apply(card, ReviewResult.GotIt, Now);

        ReviewSchedule.Apply(card, ReviewResult.Again, Now);

        Assert.Equal(0, card.Box);
        Assert.Equal(Now.AddMinutes(10), card.DueAt);
        ReviewSchedule.Apply(card, ReviewResult.GotIt, Now);
        Assert.Equal(Now.AddDays(1), card.DueAt); // starts climbing again
    }
}

public class VocabCardPromptTests
{
    [Fact]
    public void The_prompt_has_the_sentence_the_expression_and_the_language()
    {
        var request = PromptBuilder.VocabCard(CefrLevel.B1, "Die Kaufleute handelten mit Hering.", "Kaufleute", "Spanish");

        Assert.Equal("vocab-card", request.Purpose);
        Assert.True(request.Interactive); // someone is waiting for the card
        Assert.Contains("Sentence: Die Kaufleute handelten mit Hering.", request.UserPrompt, StringComparison.Ordinal);
        Assert.Contains("Saved expression: Kaufleute", request.UserPrompt, StringComparison.Ordinal);
        Assert.Contains("in Spanish", request.UserPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public void A_card_reply_needs_both_fields()
    {
        Assert.Empty(new VocabCardResponse { BaseForm = "der Kaufmann, die Kaufleute", Translation = "merchants" }.Validate());
        Assert.NotEmpty(new VocabCardResponse { BaseForm = "", Translation = "merchants" }.Validate());
        Assert.NotEmpty(new VocabCardResponse { BaseForm = "der Kaufmann", Translation = " " }.Validate());
    }
}
