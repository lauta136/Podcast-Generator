using Microsoft.AspNetCore.Mvc;
using PodcastGenerator.Api.Dtos;
using PodcastGenerator.Application.Episodes;

namespace PodcastGenerator.Api.Controllers;

[ApiController]
[Route("api/episodes")]
public class EpisodesController(IEpisodeService episodes) : ControllerBase
{
    /// <summary>Queues generation and returns 202 right away; poll the status URL for progress.</summary>
    [HttpPost]
    [ProducesResponseType<EpisodeDto>(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Create(CreateEpisodeRequest request, CancellationToken ct)
    {
        var episode = await episodes.CreateAsync(
            new CreateEpisode(request.Topic, request.SurpriseMe, request.Level, request.DurationMin, request.Style), ct);
        return AcceptedAtAction(nameof(GetStatus), new { id = episode.Id }, EpisodeDto.From(episode));
    }

    [HttpGet("{id:guid}")]
    public async Task<EpisodeDto> Get(Guid id, CancellationToken ct) =>
        EpisodeDto.From(await episodes.GetAsync(id, ct));

    [HttpGet("{id:guid}/status")]
    public async Task<EpisodeStatusDto> GetStatus(Guid id, CancellationToken ct) =>
        EpisodeStatusDto.From(await episodes.GetProgressAsync(id, ct));

    [HttpGet("{id:guid}/transcript")]
    public async Task<TranscriptDto> GetTranscript(Guid id, CancellationToken ct) =>
        TranscriptDto.From(await episodes.GetTranscriptAsync(id, ct));

    /// <summary>Re-queues only the failed or missing steps.</summary>
    [HttpPost("{id:guid}/retry")]
    public async Task<IActionResult> Retry(Guid id, CancellationToken ct)
    {
        await episodes.RetryAsync(id, ct);
        return AcceptedAtAction(nameof(GetStatus), new { id }, null);
    }

    [HttpPut("{id:guid}/rating")]
    public async Task<IActionResult> Rate(Guid id, RateEpisodeRequest request, CancellationToken ct)
    {
        await episodes.RateAsync(id, request.Rating!.Value, ct);
        return NoContent();
    }
}
