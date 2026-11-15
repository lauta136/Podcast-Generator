using Microsoft.AspNetCore.Mvc;
using PodcastGenerator.Application.Translation;

namespace PodcastGenerator.Api.Controllers;

public sealed record SentenceTranslationDto(Guid Id, string Translation);

public sealed record ParagraphTranslationDto(string Language, IReadOnlyList<SentenceTranslationDto> Sentences);

[ApiController]
[Route("api/episodes/{episodeId:guid}/segments/{segmentId:guid}/translation")]
public class TranslationsController(ITranslationService translations) : ControllerBase
{
    /// <summary>
    /// Translates the sentence's whole paragraph into the user's native language and returns every sentence of it.
    /// POST because the first call costs an LLM request; later calls are answered from the cache.
    /// </summary>
    [HttpPost]
    public async Task<ParagraphTranslationDto> Translate(Guid episodeId, Guid segmentId, CancellationToken ct)
    {
        var result = await translations.TranslateParagraphAsync(episodeId, segmentId, ct);
        return new ParagraphTranslationDto(result.Language, [.. result.Sentences.Select(s => new SentenceTranslationDto(s.SegmentId, s.Translation))]);
    }
}
