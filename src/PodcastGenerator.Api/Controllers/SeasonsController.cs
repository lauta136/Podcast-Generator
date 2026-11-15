using Microsoft.AspNetCore.Mvc;
using PodcastGenerator.Api.Dtos;
using PodcastGenerator.Application.Seasons;

namespace PodcastGenerator.Api.Controllers;

[ApiController]
[Route("api/seasons")]
public class SeasonsController(ISeasonService seasons) : ControllerBase
{
    /// <summary>Creates the season and queues episode 1, whose job plans the arc first. Returns 202 with the first episode.</summary>
    [HttpPost]
    [ProducesResponseType<EpisodeDto>(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Create(CreateSeasonRequest request, CancellationToken ct)
    {
        var (season, first) = await seasons.CreateAsync(
            new CreateSeason(request.Topic, request.Level, request.EpisodeCount, request.DurationMin, request.Style, request.SpeakingRate), ct);
        return AcceptedAtAction(nameof(Get), new { id = season.Id }, EpisodeDto.From(first));
    }

    [HttpGet]
    public async Task<IEnumerable<SeasonSummaryDto>> List(CancellationToken ct) =>
        (await seasons.ListAsync(ct)).Select(SeasonSummaryDto.From);

    [HttpGet("{id:guid}")]
    public async Task<SeasonDetailDto> Get(Guid id, CancellationToken ct) => SeasonDetailDto.From(await seasons.GetAsync(id, ct));

    /// <summary>Queues the next episode of the season (the latest one must be Ready).</summary>
    [HttpPost("{id:guid}/episodes")]
    [ProducesResponseType<EpisodeDto>(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> CreateNextEpisode(Guid id, CancellationToken ct)
    {
        var episode = await seasons.CreateNextEpisodeAsync(id, ct);
        return AcceptedAtAction(nameof(Get), new { id }, EpisodeDto.From(episode));
    }

    /// <summary>Deletes the season with all of its episodes and audio.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await seasons.DeleteAsync(id, ct);
        return NoContent();
    }
}
