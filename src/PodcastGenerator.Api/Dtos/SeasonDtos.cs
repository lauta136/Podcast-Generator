using System.ComponentModel.DataAnnotations;
using PodcastGenerator.Application.Seasons;
using PodcastGenerator.Domain.Enums;

namespace PodcastGenerator.Api.Dtos;

public sealed class CreateSeasonRequest
{
    [StringLength(500)]
    public string? Topic { get; init; }

    [EnumDataType(typeof(CefrLevel))]
    public CefrLevel? Level { get; init; }

    public int EpisodeCount { get; init; } = 5;

    public int? DurationMin { get; init; }

    [EnumDataType(typeof(EpisodeStyle))]
    public EpisodeStyle Style { get; init; } = EpisodeStyle.Storytelling;

    public decimal? SpeakingRate { get; init; }
}

public sealed record SeasonSummaryDto(
    Guid Id,
    string Title,
    string? Description,
    string Topic,
    CefrLevel Level,
    int EpisodeCount,
    int EpisodesCreated,
    int EpisodesReady,
    int EpisodesFinished,
    bool Planned,
    DateTimeOffset CreatedAt)
{
    public static SeasonSummaryDto From(SeasonSummary s) => new(
        s.Season.Id, s.Season.Title, s.Season.Description, s.Season.Topic, s.Season.Level, s.Season.EpisodeCount,
        s.EpisodesCreated, s.EpisodesReady, s.EpisodesFinished, s.Season.ArcJson is not null, s.Season.CreatedAt);
}

/// <param name="Episode">Null until this part has been generated.</param>
public sealed record SeasonPartDto(int Order, string Title, string Summary, EpisodeListItemDto? Episode);

/// <param name="Parts">The planned arc, one entry per episode; empty while the plan is being made.</param>
public sealed record SeasonDetailDto(
    Guid Id,
    string Title,
    string? Description,
    string Topic,
    CefrLevel Level,
    EpisodeStyle Style,
    int EpisodeCount,
    int DurationMin,
    decimal? SpeakingRate,
    bool Planned,
    IReadOnlyList<SeasonPartDto> Parts,
    IReadOnlyList<EpisodeListItemDto> Episodes,
    DateTimeOffset CreatedAt)
{
    public static SeasonDetailDto From(SeasonDetail d)
    {
        var s = d.Season;
        var episodes = d.Episodes.Select(EpisodeListItemDto.From).ToList();
        var parts = d.Arc is null
            ? []
            : d.Arc.Episodes
                .Select((p, i) => new SeasonPartDto(i + 1, p.Title, p.Summary, episodes.FirstOrDefault(e => e.SeasonOrder == i + 1)))
                .ToList();
        return new(s.Id, s.Title, s.Description, s.Topic, s.Level, s.Style, s.EpisodeCount, s.DurationMin, s.SpeakingRate,
            d.Arc is not null, parts, episodes, s.CreatedAt);
    }
}
