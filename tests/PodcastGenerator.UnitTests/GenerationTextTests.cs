using System.Globalization;
using PodcastGenerator.Application;
using PodcastGenerator.Application.Generation;
using PodcastGenerator.Application.Tts;
using PodcastGenerator.Domain.Enums;

namespace PodcastGenerator.UnitTests;

public class LengthEstimatorTests
{
    private readonly EpisodeOptions _options = new();

    private readonly TtsOptions _tts = new();

    [Theory]
    [InlineData(TtsModelFamily.WaveNet, "1.0", 10, 1600)]
    [InlineData(TtsModelFamily.WaveNet, "0.85", 10, 1360)] // B1 default: slower voice, fewer words for the same minutes
    [InlineData(TtsModelFamily.WaveNet, "0.75", 60, 7200)]
    [InlineData(TtsModelFamily.Chirp3HD, "1.0", 10, 1500)]
    [InlineData(TtsModelFamily.Chirp3HD, "0.75", 10, 1241)] // Chirp reacts less to the rate: 1500 × 0.75^0.66
    public void Target_words_follow_voice_pace_and_speaking_rate(TtsModelFamily family, string rate, int minutes, int expected) =>
        Assert.Equal(expected, LengthEstimator.TargetWords(minutes, _tts.WordsPerMinute(family, decimal.Parse(rate, CultureInfo.InvariantCulture))));

    [Fact]
    public void Lower_levels_default_to_slower_speech()
    {
        var rates = Enum.GetValues<CefrLevel>().Select(_options.SpeakingRateFor).ToList();
        Assert.Equal(rates.Order(), rates);
        Assert.All(rates, r => Assert.InRange(r, _options.MinSpeakingRate, _options.MaxSpeakingRate));
    }

    [Theory]
    [InlineData(600, 1)] // 5 min B1
    [InlineData(1250, 2)] // 10 min B1: two sections of 625
    [InlineData(7500, 9)] // 60 min B1
    [InlineData(1000, 1)] // two sections would be under the 600-word minimum
    public void Section_count_keeps_sections_near_target_size(int words, int expected) =>
        Assert.Equal(expected, LengthEstimator.SectionCount(words, _options));
}

public class TextSegmenterTests
{
    [Fact]
    public void Splits_sentences_on_terminal_punctuation()
    {
        var sentences = TextSegmenter.SplitSentences("Hallo! Wie geht es dir? Mir geht es gut.");
        Assert.Equal(["Hallo!", "Wie geht es dir?", "Mir geht es gut."], sentences);
    }

    [Fact]
    public void Keeps_abbreviations_and_ordinals_inside_the_sentence()
    {
        var sentences = TextSegmenter.SplitSentences("Am 3. Oktober feiern wir z. B. den Tag der Einheit. Das ist schön.");
        Assert.Equal(["Am 3. Oktober feiern wir z. B. den Tag der Einheit.", "Das ist schön."], sentences);
    }

    [Fact]
    public void Years_still_end_a_sentence()
    {
        var sentences = TextSegmenter.SplitSentences("Kolumbus kam im Jahr 1492. Dann begann alles.");
        Assert.Equal(2, sentences.Count);
    }

    [Fact]
    public void Closing_quotes_stay_with_their_sentence()
    {
        var sentences = TextSegmenter.SplitSentences("Er sagte: „Komm mit!“ Dann ging er.");
        Assert.Equal(["Er sagte: „Komm mit!“", "Dann ging er."], sentences);
    }

    [Fact]
    public void Cleaning_removes_markdown_and_stage_directions()
    {
        var cleaned = TextSegmenter.CleanForSpeech("**Willkommen** (Musik) zum   Podcast! [Pause] *lacht* Los geht's.");
        Assert.Equal("Willkommen zum Podcast! Los geht's.", cleaned);
    }

    [Fact]
    public void Long_paragraphs_are_split_at_sentence_boundaries()
    {
        var sentence = new string('a', 300) + ".";
        var paragraph = string.Join(" ", Enumerable.Repeat(sentence, 5)); // ~1,505 chars

        var fitted = TextSegmenter.FitParagraphs([paragraph], maxChars: 1000);

        Assert.Equal(2, fitted.Count);
        Assert.All(fitted, p => Assert.True(p.Length <= 1000));
        Assert.Equal(paragraph, string.Join(" ", fitted));
    }
}

public class ProofreadResponseTests
{
    private static readonly string[] Original = ["Das ist eine dauerhafte Bauwerk aus Beton und Stahl.", "Klaus wohnt an der Sektoragrenze."];

    [Fact]
    public void Small_corrections_are_accepted()
    {
        var response = new ProofreadResponse { Paragraphs = ["Das ist ein dauerhaftes Bauwerk aus Beton und Stahl.", "Klaus wohnt an der Sektorengrenze."] };
        Assert.Empty(response.Validate(Original));
    }

    [Fact]
    public void Dropping_or_merging_paragraphs_is_rejected()
    {
        var response = new ProofreadResponse { Paragraphs = [string.Join(" ", Original)] };
        Assert.Contains(response.Validate(Original), e => e.Contains("exactly 2", StringComparison.Ordinal));
    }

    [Fact]
    public void Rewriting_a_paragraph_is_rejected()
    {
        var longer = string.Join(" ", Enumerable.Repeat("Klaus wohnt an der Sektorengrenze in Berlin.", 3));
        var original = new[] { Original[0], longer };
        var response = new ProofreadResponse { Paragraphs = [Original[0], "Klaus wohnt in Berlin."] };

        Assert.Contains(response.Validate(original), e => e.Contains("paragraph 2", StringComparison.Ordinal));
    }
}
