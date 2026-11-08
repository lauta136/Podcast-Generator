namespace PodcastGenerator.Domain.Entities;

public class ListeningProgress
{
    public Guid UserId { get; set; }
    public Guid EpisodeId { get; set; }
    public double PositionSec { get; set; }
    public bool Completed { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
