using System.Text;
using System.Text.RegularExpressions;

namespace PodcastGenerator.Application.Generation;

/// <summary>Cleans LLM text for speech and splits it into sentences and TTS-sized paragraphs.</summary>
public static partial class TextSegmenter
{
    // Common German abbreviations that end in a period but don't end a sentence.
    private static readonly HashSet<string> Abbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        "z", "b", "d", "h", "u", "a", "s", "o", "bzw", "usw", "ca", "dr", "nr", "etc", "vgl", "inkl",
        "evtl", "ggf", "prof", "str", "bspw", "jh", "mio", "mrd", "chr", "v", "n",
    };

    /// <summary>Removes control characters (except whitespace). PostgreSQL rejects \u0000 in text and jsonb.</summary>
    public static string StripControlChars(string text) => ControlChars().Replace(text, "");

    public static string CleanForSpeech(string text)
    {
        var s = StageDirection().Replace(StripControlChars(text), " ");
        s = MarkdownChars().Replace(s, "");
        s = Whitespace().Replace(s, " ");
        return s.Trim();
    }

    public static IReadOnlyList<string> SplitSentences(string paragraph)
    {
        var text = paragraph.Trim();
        var sentences = new List<string>();
        var start = 0;

        foreach (Match m in SentenceEnd().Matches(text))
        {
            var end = m.Index + m.Length;
            if (end >= text.Length || IsAbbreviationOrOrdinal(text, m.Index))
            {
                continue;
            }

            var sentence = text[start..end].Trim();
            if (sentence.Length > 0)
            {
                sentences.Add(sentence);
            }

            start = end;
        }

        var rest = text[start..].Trim();
        if (rest.Length > 0)
        {
            sentences.Add(rest);
        }

        return sentences;
    }

    /// <summary>Splits paragraphs longer than <paramref name="maxChars"/> at sentence boundaries.</summary>
    public static IReadOnlyList<string> FitParagraphs(IEnumerable<string> paragraphs, int maxChars)
    {
        var result = new List<string>();
        foreach (var paragraph in paragraphs)
        {
            var current = new StringBuilder();
            foreach (var sentence in SplitSentences(paragraph))
            {
                if (current.Length > 0 && current.Length + 1 + sentence.Length > maxChars)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }

                if (current.Length > 0)
                {
                    current.Append(' ');
                }

                current.Append(sentence);
            }

            if (current.Length > 0)
            {
                result.Add(current.ToString());
            }
        }

        return result;
    }

    private static bool IsAbbreviationOrOrdinal(string text, int punctuationIndex)
    {
        if (text[punctuationIndex] != '.')
        {
            return false;
        }

        var wordStart = punctuationIndex;
        while (wordStart > 0 && char.IsLetterOrDigit(text[wordStart - 1]))
        {
            wordStart--;
        }

        var word = text[wordStart..punctuationIndex];

        // "am 3. Oktober" is an ordinal; "im Jahr 1492." still ends a sentence.
        return Abbreviations.Contains(word) || (word.Length is 1 or 2 && word.All(char.IsDigit));
    }

    // Sentence-ending punctuation, optional closing quote, then whitespace.
    [GeneratedRegex(@"[.!?…]+[""»«“”']?(?=\s)")]
    private static partial Regex SentenceEnd();

    // "(Musik)", "[Pause]", "*lacht*": stage directions that would be read aloud.
    [GeneratedRegex(@"\([^)]{0,40}\)|\[[^\]]{0,40}\]|\*[^*]{0,40}\*")]
    private static partial Regex StageDirection();

    [GeneratedRegex(@"[#*_`>]")]
    private static partial Regex MarkdownChars();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]")]
    private static partial Regex ControlChars();
}
