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
