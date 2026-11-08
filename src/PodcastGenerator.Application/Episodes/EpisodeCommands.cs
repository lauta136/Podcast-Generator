using PodcastGenerator.Domain.Enums;

namespace PodcastGenerator.Application.Episodes;

/// <param name="Level">Null uses the default from preferences.</param>
/// <param name="DurationMin">Null uses the default from preferences.</param>
public sealed record CreateEpisode(string? Topic, bool SurpriseMe, CefrLevel? Level, int? DurationMin, EpisodeStyle Style);

public sealed record EpisodeProgress(
    Guid EpisodeId,
    EpisodeStatus Status,
    string? Title,
    string? LastError,
    int SectionsTotal,
    int SectionsCompleted);
