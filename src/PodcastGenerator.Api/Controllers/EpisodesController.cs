using Microsoft.AspNetCore.Mvc;
using PodcastGenerator.Api.Dtos;
using PodcastGenerator.Application.Episodes;
using PodcastGenerator.Domain.Enums;

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
            new CreateEpisode(request.Topic, request.SurpriseMe, request.Level, request.DurationMin, request.Style, request.SpeakingRate), ct);
        return AcceptedAtAction(nameof(GetStatus), new { id = episode.Id }, EpisodeDto.From(episode));
    }

    /// <summary>The library: newest first, filtered and paged, with listening progress.</summary>
    [HttpGet]
    public async Task<EpisodePageDto> List(
        [FromQuery] string? search,
        [FromQuery] CefrLevel? level,
        [FromQuery] EpisodeStatus? status,
        CancellationToken ct,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20) =>
        EpisodePageDto.From(await episodes.ListAsync(new EpisodeQuery(search, level, status, page, pageSize), ct));

    [HttpGet("{id:guid}/progress")]
    public async Task<ProgressDto> GetProgress(Guid id, CancellationToken ct) =>
        ProgressDto.From(await episodes.GetListeningProgressAsync(id, ct));

    /// <summary>Saves the resume position. The player calls it every few seconds and on pause.</summary>
    [HttpPut("{id:guid}/progress")]
    public async Task<ProgressDto> SaveProgress(Guid id, SaveProgressRequest request, CancellationToken ct) =>
        ProgressDto.From(await episodes.SaveListeningProgressAsync(id, request.PositionSec!.Value, request.Completed, ct));

    /// <summary>Words, TTS characters and budget impact for an episode before creating it.</summary>
    [HttpGet("estimate")]
    public async Task<EpisodeEstimateDto> Estimate(
        [FromQuery] CefrLevel? level, [FromQuery] int? durationMin, [FromQuery] decimal? speakingRate, CancellationToken ct) =>
        EpisodeEstimateDto.From(await episodes.EstimateAsync(level, durationMin, speakingRate, ct));

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

    /// <summary>The stitched episode MP3. Supports HTTP Range requests, which the player needs for seeking.</summary>
    [HttpGet("{id:guid}/audio")]
    public async Task<IActionResult> GetAudio(Guid id, CancellationToken ct) =>
        File(await episodes.OpenAudioAsync(id, ct), "audio/mpeg", enableRangeProcessing: true);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await episodes.DeleteAsync(id, ct);
        return NoContent();
    }

    [HttpPut("{id:guid}/rating")]
    public async Task<IActionResult> Rate(Guid id, RateEpisodeRequest request, CancellationToken ct)
    {
        await episodes.RateAsync(id, request.Rating!.Value, ct);
        return NoContent();
    }
}
