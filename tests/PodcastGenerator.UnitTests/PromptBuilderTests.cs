using PodcastGenerator.Application.Generation;
using PodcastGenerator.Domain.Entities;
using PodcastGenerator.Domain.Enums;

namespace PodcastGenerator.UnitTests;

public class PromptBuilderTests
{
    private static Episode NewEpisode() => new()
    {
        Topic = "Das spanische Weltreich",
        Title = "Ein Reich, in dem die Sonne nie unterging",
        Level = CefrLevel.B1,
        Style = EpisodeStyle.Storytelling,
        TargetWords = 1250,
        DurationMin = 10,
    };

    [Fact]
    public void Outline_prompt_includes_level_rules_topic_and_section_count()
    {
        var request = PromptBuilder.Outline(NewEpisode(), 2, null);

        Assert.Equal("outline", request.Purpose);
        Assert.Contains("B1", request.SystemPrompt);
        Assert.Contains("Konjunktiv II", request.SystemPrompt); // from cefr.v1.json
        Assert.Contains("No markdown", request.SystemPrompt);
        Assert.Contains("Das spanische Weltreich", request.UserPrompt);
        Assert.Contains("exactly 2 sections", request.UserPrompt);
        Assert.DoesNotContain("{{", request.UserPrompt);
    }

    [Fact]
    public void Section_prompt_sends_bounded_context_not_all_previous_text()
    {
        var episode = NewEpisode();
        var sections = Enumerable.Range(0, 10).Select(i => new EpisodeSection
        {
            Order = i,
            Title = $"Teil {i + 1}",
            Summary = $"summary {i + 1}",
            Recap = $"recap {i + 1}",
            Status = StepStatus.Completed,
            Segments = [.. Enumerable.Range(0, 20).Select(j => new TranscriptSegment { Order = j, Text = $"S{i}-{j}." })],
        }).ToList();
        var current = sections[9];

        var request = PromptBuilder.Section(episode, sections, current, sections[..9]);

        Assert.DoesNotContain("recap 1\n", request.UserPrompt + "\n"); // only the last 6 recaps
        Assert.Contains("recap 9", request.UserPrompt);
        Assert.Contains("S8-19.", request.UserPrompt); // last sentences of the previous section
        Assert.DoesNotContain("S8-10.", request.UserPrompt);
        Assert.DoesNotContain("S0-0.", request.UserPrompt);
        Assert.Contains("goodbye", request.UserPrompt); // last section wraps up
    }

    [Fact]
    public void Continuation_sections_never_greet_or_say_goodbye()
    {
        var episode = NewEpisode();
        var sections = new List<EpisodeSection>
        {
            new() { Order = 0, Title = "A", Summary = "a", Status = StepStatus.Completed, Recap = "a" },
            new() { Order = 1, Title = "B", Summary = "b", IsContinuation = true },
            new() { Order = 2, Title = "C", Summary = "c", Status = StepStatus.Completed, Recap = "c" },
        };

        var request = PromptBuilder.Section(episode, sections, sections[1], sections[..1]);

        Assert.Contains("No greeting and no goodbye", request.UserPrompt);
    }

    [Fact]
    public void Proofread_prompt_sends_paragraphs_as_readable_json()
    {
        var request = PromptBuilder.Proofread(NewEpisode(), ["Die Mauer fiel am neunten November.", "Schöne Grüße aus Köln."]);

        Assert.Equal("proofread", request.Purpose);
        Assert.Contains("\"Schöne Grüße aus Köln.\"", request.UserPrompt); // umlauts not \u-escaped
        Assert.Contains("exactly 2 paragraphs", request.UserPrompt);
        Assert.Contains("Do not change anything else", request.UserPrompt);
        Assert.True(request.Temperature <= 0.2);
    }

    [Theory]
    [InlineData(new[] { DifficultyRating.TooHard, DifficultyRating.TooHard, DifficultyRating.JustRight }, "easier end")]
    [InlineData(new[] { DifficultyRating.TooEasy, DifficultyRating.TooEasy }, "harder end")]
    public void Feedback_nudges_within_the_level(DifficultyRating[] ratings, string expected)
    {
        var feedback = PromptBuilder.DifficultyFeedback(CefrLevel.B1, ratings);

        Assert.NotNull(feedback);
        Assert.Contains(expected, feedback);
        Assert.Contains("Stay at B1", feedback);
    }

    [Fact]
    public void Mixed_or_missing_feedback_adds_nothing()
    {
        Assert.Null(PromptBuilder.DifficultyFeedback(CefrLevel.B1, []));
        Assert.Null(PromptBuilder.DifficultyFeedback(CefrLevel.B1, [DifficultyRating.TooHard, DifficultyRating.TooEasy]));
    }
}

public class SeasonPromptTests
{
    private static Season SeasonWithArc(int count) => new()
    {
        Title = "Die Hanse",
        Topic = "Die Hanse",
        Level = CefrLevel.B1,
        EpisodeCount = count,
        DurationMin = 10,
        ArcJson = SeasonArcs.Write(new SeasonArcResponse
        {
            Title = "Die Hanse",
            Episodes = [.. Enumerable.Range(1, count).Select(i => new SeasonArcEpisode { Title = $"Teil {i}", Summary = $"Covers {i}." })],
        }),
    };

    private static Episode EpisodeOf(Season season, int order) => new()
    {
        Topic = $"Teil {order}",
        Title = $"Teil {order}",
        Level = CefrLevel.B1,
        TargetWords = 1360,
        Season = season,
        SeasonOrder = order,
    };

    [Fact]
    public void Standalone_episodes_get_no_season_context() =>
        Assert.Equal("", PromptBuilder.SeasonContext(new Episode { Topic = "x", Level = CefrLevel.B1 }));

    [Fact]
    public void Only_the_latest_earlier_summaries_are_sent_in_full()
    {
        var season = SeasonWithArc(6);
        for (var i = 1; i <= 5; i++)
        {
            season.Episodes.Add(new Episode { Topic = $"Teil {i}", SeasonOrder = i, Summary = $"Summary of {i}." });
        }

        var context = PromptBuilder.SeasonContext(EpisodeOf(season, 6));

        Assert.Contains("episode 6 of 6", context, StringComparison.Ordinal);
        Assert.Contains("1. Teil 1: Covers 1.", context, StringComparison.Ordinal); // the arc is always complete
        Assert.DoesNotContain("Summary of 2.", context, StringComparison.Ordinal);
        Assert.Contains("- Episode 3: Summary of 3.", context, StringComparison.Ordinal);
        Assert.Contains("- Episode 5: Summary of 5.", context, StringComparison.Ordinal);
        Assert.EndsWith("Plan only episode 6: Covers 6.", context, StringComparison.Ordinal);
    }

    [Fact]
    public void Season_episodes_name_their_part_and_tease_the_next()
    {
        var season = SeasonWithArc(3);
        var sections = new List<EpisodeSection>
        {
            new() { Order = 0, Title = "a", Summary = "a" },
            new() { Order = 1, Title = "b", Summary = "b" },
        };

        var opening = PromptBuilder.Section(EpisodeOf(season, 2), sections, sections[0], []).UserPrompt;
        var closing = PromptBuilder.Section(EpisodeOf(season, 2), sections, sections[1], sections[..1]).UserPrompt;
        var finale = PromptBuilder.Section(EpisodeOf(season, 3), sections, sections[1], sections[..1]).UserPrompt;

        Assert.Contains("this is part 2 of the series \"Die Hanse\"", opening, StringComparison.Ordinal);
        Assert.Contains("teaser of the next part (\"Teil 3\")", closing, StringComparison.Ordinal);
        Assert.Contains("wrap-up of the whole series", finale, StringComparison.Ordinal);
    }

    [Fact]
    public void Arc_replies_need_the_right_number_of_complete_episodes()
    {
        Assert.Empty(new SeasonArcResponse { Title = "T", Episodes = [new() { Title = "a", Summary = "b" }, new() { Title = "c", Summary = "d" }] }.Validate(2));
        Assert.NotEmpty(new SeasonArcResponse { Title = "T", Episodes = [new() { Title = "a", Summary = "b" }] }.Validate(2));
        Assert.NotEmpty(new SeasonArcResponse { Title = "T", Episodes = [new() { Title = "a", Summary = " " }, new() { Title = "c", Summary = "d" }] }.Validate(2));
        Assert.NotEmpty(new SeasonArcResponse { Title = "", Episodes = [new() { Title = "a", Summary = "b" }, new() { Title = "c", Summary = "d" }] }.Validate(2));
    }

    [Fact]
    public void The_arc_prompt_asks_for_the_season_size_in_order() =>
        Assert.Contains("exactly 4 items, in listening order", PromptBuilder.SeasonArc(SeasonWithArc(4)).UserPrompt, StringComparison.Ordinal);
}
