using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using PodcastGenerator.Application.Vocabulary;

namespace PodcastGenerator.Api.Controllers;

public sealed class SaveWordRequest
{
    [Required]
    public Guid? SegmentId { get; init; }

    [Required, StringLength(200)]
    public string Text { get; init; } = "";
}

public sealed class ReviewRequest
{
    [Required, EnumDataType(typeof(ReviewResult))]
    public ReviewResult? Result { get; init; }
}

/// <param name="StartSec">Where the sentence starts in the episode, for "hear it"; null once the episode is gone.</param>
public sealed record VocabularyItemDto(
    Guid Id,
    string Text,
    string BaseForm,
    string Translation,
    string Context,
    Guid? EpisodeId,
    string? EpisodeTitle,
    double? StartSec,
    int Box,
    DateTimeOffset DueAt,
    DateTimeOffset CreatedAt)
{
    public static VocabularyItemDto From(VocabularyEntry e) => new(
        e.Item.Id, e.Item.Text, e.Item.BaseForm, e.Item.Translation, e.Item.Context, e.Item.EpisodeId, e.EpisodeTitle,
        e.StartSec, e.Item.Box, e.Item.DueAt, e.Item.CreatedAt);
}

public sealed record VocabularyPageDto(IReadOnlyList<VocabularyItemDto> Items, int Total, int Due);

[ApiController]
[Route("api/vocabulary")]
public class VocabularyController(IVocabularyService vocabulary) : ControllerBase
{
    /// <summary>Saves a word or phrase from a transcript sentence. 201 for a new card, 200 if it was already saved.</summary>
    [HttpPost]
    public async Task<IActionResult> Save(SaveWordRequest request, CancellationToken ct)
    {
        var saved = await vocabulary.SaveAsync(request.SegmentId!.Value, request.Text, ct);
        var dto = VocabularyItemDto.From(saved.Entry);
        return saved.Created ? StatusCode(StatusCodes.Status201Created, dto) : Ok(dto);
    }

    [HttpGet]
    public async Task<VocabularyPageDto> List([FromQuery] string? search, CancellationToken ct, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var result = await vocabulary.ListAsync(search, page, pageSize, ct);
        return new VocabularyPageDto([.. result.Items.Select(VocabularyItemDto.From)], result.Total, result.Due);
    }

    /// <summary>Cards due for review now, most overdue first.</summary>
    [HttpGet("review")]
    public async Task<IEnumerable<VocabularyItemDto>> Due(CancellationToken ct, [FromQuery] int take = 20) =>
        (await vocabulary.DueAsync(take, ct)).Select(VocabularyItemDto.From);

    /// <summary>Records an answer: "GotIt" moves the card to a longer gap, "Again" brings it back in 10 minutes.</summary>
    [HttpPost("{id:guid}/review")]
    public async Task<IActionResult> Review(Guid id, ReviewRequest request, CancellationToken ct)
    {
        var item = await vocabulary.ReviewAsync(id, request.Result!.Value, ct);
        return Ok(new { item.Box, item.DueAt });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await vocabulary.DeleteAsync(id, ct);
        return NoContent();
    }
}
