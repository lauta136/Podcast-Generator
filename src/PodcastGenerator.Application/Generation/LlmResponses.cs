namespace PodcastGenerator.Application.Generation;

public sealed class OutlineResponse
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public List<OutlineSection> Sections { get; set; } = [];

    public IReadOnlyList<string> Validate(int expectedSections)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(Title))
        {
            errors.Add("\"title\" is required");
        }

        if (Sections.Count != expectedSections)
        {
            errors.Add($"\"sections\" must contain exactly {expectedSections} items, got {Sections.Count}");
        }

        for (var i = 0; i < Sections.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(Sections[i].Title) || string.IsNullOrWhiteSpace(Sections[i].Summary))
            {
                errors.Add($"section {i + 1} needs a \"title\" and a \"summary\"");
            }
        }

        return errors;
    }
}

public sealed class OutlineSection
{
    public string Title { get; set; } = "";
    public string Summary { get; set; } = "";
    public string? Focus { get; set; }
}

public sealed class SectionResponse
{
    public List<string> Paragraphs { get; set; } = [];
    public string Recap { get; set; } = "";

    public IReadOnlyList<string> Validate(int targetWords)
    {
        var errors = new List<string>();
        var words = Paragraphs.Sum(LengthEstimator.CountWords);

        // Well under target usually means the model summarized instead of writing the script.
        if (words < targetWords * 0.5)
        {
            errors.Add($"the section is far too short ({words} words, target about {targetWords}); write the full spoken text");
        }

        if (string.IsNullOrWhiteSpace(Recap))
        {
            errors.Add("\"recap\" is required");
        }

        return errors;
    }
}

public sealed class ProofreadResponse
{
    /// <summary>Allowed drift in words per paragraph: fixes change a word or two, never whole sentences.</summary>
    public const double MaxLengthChange = 0.1;

    public List<string> Paragraphs { get; set; } = [];
    public List<ProofreadCorrection> Corrections { get; set; } = [];

    public IReadOnlyList<string> Validate(IReadOnlyList<string> original)
    {
        if (Paragraphs.Count != original.Count)
        {
            return [$"\"paragraphs\" must contain exactly {original.Count} items, got {Paragraphs.Count}; keep the paragraph breaks"];
        }

        var errors = new List<string>();
        for (var i = 0; i < original.Count; i++)
        {
            var before = LengthEstimator.CountWords(original[i]);
            var after = LengthEstimator.CountWords(Paragraphs[i]);
            if (Math.Abs(after - before) > Math.Max(3, before * MaxLengthChange))
            {
                errors.Add($"paragraph {i + 1} went from {before} to {after} words; only fix errors, do not rewrite, shorten or extend");
            }
        }

        return errors;
    }
}

public sealed class ProofreadCorrection
{
    public string Original { get; set; } = "";
    public string Corrected { get; set; } = "";
}

/// <summary>Generated section text saved before proofreading, so a proofreading failure never regenerates it.</summary>
public sealed record SectionDraft(IReadOnlyList<string> Paragraphs, string Recap);
