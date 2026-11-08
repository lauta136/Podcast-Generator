using PodcastGenerator.Domain.Enums;

namespace PodcastGenerator.Domain.Entities;

public class EpisodeSection
{
    public Guid Id { get; set; }
    public Guid EpisodeId { get; set; }
    public int Order { get; set; }

    public required string Title { get; set; }
    public required string Summary { get; set; }
    public int TargetWords { get; set; }

    /// <summary>Vocabulary field or grammar point this section uses naturally.</summary>
    public string? Focus { get; set; }

    /// <summary>Actual word count of the generated text.</summary>
    public int? WordCount { get; set; }

    public StepStatus Status { get; set; } = StepStatus.Pending;
    public int Attempts { get; set; }
    public string? LastError { get; set; }

    public List<TranscriptSegment> Segments { get; set; } = [];
}
