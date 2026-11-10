using System.ComponentModel.DataAnnotations;
using PodcastGenerator.Application.Episodes;
using PodcastGenerator.Domain.Entities;
using PodcastGenerator.Domain.Enums;

namespace PodcastGenerator.Api.Dtos;

public sealed class CreateEpisodeRequest
{
    [StringLength(500)]
    public string? Topic { get; init; }

    public bool SurpriseMe { get; init; }

    [EnumDataType(typeof(CefrLevel))]
    public CefrLevel? Level { get; init; }

    public int? DurationMin { get; init; }

    [EnumDataType(typeof(EpisodeStyle))]
    public EpisodeStyle Style { get; init; } = EpisodeStyle.Conversational;
}

public sealed class RateEpisodeRequest
{
    [Required, EnumDataType(typeof(DifficultyRating))]
    public DifficultyRating? Rating { get; init; }
}

public sealed record EpisodeDto(
    Guid Id,
    string? Title,
    string? Description,
    string Topic,
    string LanguageCode,
    CefrLevel Level,
    EpisodeStyle Style,
    int DurationMin,
    int TargetWords,
    EpisodeStatus Status,
    string? LastError,
    DifficultyRating? DifficultyRating,
    double? AudioDurationSec,
    DateTimeOffset CreatedAt)
{
    public static EpisodeDto From(Episode e) => new(
        e.Id, e.Title, e.Description, e.Topic, e.LanguageCode, e.Level, e.Style, e.DurationMin, e.TargetWords,
        e.Status, e.LastError, e.DifficultyRating, e.AudioDurationSec, e.CreatedAt);
}

public sealed record EpisodeStatusDto(
    Guid EpisodeId,
    EpisodeStatus Status,
    string? Title,
    string? LastError,
    int SectionsTotal,
    int SectionsCompleted,
    int AudioTotal,
    int AudioCompleted)
{
    public static EpisodeStatusDto From(EpisodeProgress p) =>
        new(p.EpisodeId, p.Status, p.Title, p.LastError, p.SectionsTotal, p.SectionsCompleted, p.AudioTotal, p.AudioCompleted);
}

public sealed record EpisodeEstimateDto(
    CefrLevel Level,
    int DurationMin,
    int TargetWords,
    TtsModelFamily VoiceModelFamily,
    long EstimatedChars,
    long BudgetRemaining,
    long MonthlyBudget,
    bool FitsBudget)
{
    public static EpisodeEstimateDto From(EpisodeEstimate e) =>
        new(e.Level, e.DurationMin, e.TargetWords, e.VoiceModelFamily, e.EstimatedChars, e.BudgetRemaining, e.MonthlyBudget, e.FitsBudget);
}

public sealed record TranscriptDto(Guid EpisodeId, string? Title, IReadOnlyList<TranscriptSectionDto> Sections)
{
    public static TranscriptDto From(Episode e) => new(
        e.Id,
        e.Title,
        e.Sections
            .Where(s => s.Status == StepStatus.Completed)
            .OrderBy(s => s.Order)
            .Select(s => new TranscriptSectionDto(
                s.Id,
                s.Order,
                s.Title,
                s.Segments
                    .OrderBy(t => t.Order)
                    .Select(t => new TranscriptSegmentDto(t.Id, t.ParagraphIndex, t.Text, t.StartSec, t.EndSec))
                    .ToList()))
            .ToList());
}

public sealed record TranscriptSectionDto(Guid Id, int Order, string Title, IReadOnlyList<TranscriptSegmentDto> Segments);

/// <param name="StartSec">Null until the audio is generated.</param>
public sealed record TranscriptSegmentDto(Guid Id, int Paragraph, string Text, double? StartSec, double? EndSec);
