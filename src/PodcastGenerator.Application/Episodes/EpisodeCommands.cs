using PodcastGenerator.Domain.Entities;
using PodcastGenerator.Domain.Enums;

namespace PodcastGenerator.Application.Episodes;

/// <param name="Level">Null uses the default from preferences.</param>
/// <param name="DurationMin">Null uses the default from preferences.</param>
/// <param name="SpeakingRate">Null uses preferences, then the level's default.</param>
public sealed record CreateEpisode(string? Topic, bool SurpriseMe, CefrLevel? Level, int? DurationMin, EpisodeStyle Style, decimal? SpeakingRate = null);

public sealed record EpisodeProgress(
    Guid EpisodeId,
    EpisodeStatus Status,
    string? Title,
    string? LastError,
    int SectionsTotal,
    int SectionsCompleted,
    int AudioTotal,
    int AudioCompleted);

/// <summary>Library filters. Search matches title or topic, case-insensitively.</summary>
public sealed record EpisodeQuery(string? Search, CefrLevel? Level, EpisodeStatus? Status, int Page, int PageSize);

public sealed record EpisodeListItem(Episode Episode, ListeningProgress? Progress);

public sealed record EpisodePage(IReadOnlyList<EpisodeListItem> Items, int Total, int Page, int PageSize);

/// <summary>What an episode would cost before creating it.</summary>
public sealed record EpisodeEstimate(
    CefrLevel Level,
    int DurationMin,
    int TargetWords,
    decimal SpeakingRate,
    TtsModelFamily VoiceModelFamily,
    long EstimatedChars,
    long BudgetRemaining,
    long MonthlyBudget,
    bool FitsBudget);
