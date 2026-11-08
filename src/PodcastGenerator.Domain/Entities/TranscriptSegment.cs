namespace PodcastGenerator.Domain.Entities;

/// <summary>One sentence of the transcript, grouped into paragraphs inside a section.</summary>
public class TranscriptSegment
{
    public Guid Id { get; set; }
    public Guid SectionId { get; set; }

    /// <summary>Position of the sentence within its section.</summary>
    public int Order { get; set; }

    /// <summary>Paragraph within the section; one paragraph maps to one audio chunk.</summary>
    public int ParagraphIndex { get; set; }

    public required string Text { get; set; }

    /// <summary>Native-language translation, generated on demand (post-MVP).</summary>
    public string? Translation { get; set; }

    public double? StartSec { get; set; }
    public double? EndSec { get; set; }
}
