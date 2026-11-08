namespace PodcastGenerator.Application;

/// <summary>Bound from the "Episodes" configuration section.</summary>
public class EpisodeOptions
{
    public const string SectionName = "Episodes";

    public int MinDurationMin { get; set; } = 5;
    public int MaxDurationMin { get; set; } = 60;
}
