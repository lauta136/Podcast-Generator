using Microsoft.AspNetCore.Mvc;
using PodcastGenerator.Application.Audio;
using PodcastGenerator.Application.Tts;
using PodcastGenerator.Domain.Enums;

namespace PodcastGenerator.Api.Controllers;

public sealed record VoiceDto(string Name, string LanguageCode, string Gender, TtsModelFamily ModelFamily);

public sealed record BudgetDto(TtsModelFamily ModelFamily, long Used, long MonthlyBudget, long Remaining);

[ApiController]
public class TtsController(ITtsProvider tts, TtsBudget budget) : ControllerBase
{
    /// <summary>Voices the app can use (WaveNet and Chirp 3: HD) for a language, e.g. ?language=de.</summary>
    [HttpGet("api/voices")]
    public async Task<IEnumerable<VoiceDto>> GetVoices([FromQuery] string language = "de", CancellationToken ct = default) =>
        (await tts.GetVoicesAsync(AudioGenerator.LanguageTag(language), ct))
            .Select(v => new VoiceDto(v.Name, v.LanguageCode, v.Gender, v.ModelFamily));

    /// <summary>This month's TTS usage against the free-tier budget, per voice family.</summary>
    [HttpGet("api/tts/budget")]
    public async Task<IEnumerable<BudgetDto>> GetBudget(CancellationToken ct) =>
        (await budget.GetStatusAsync(ct)).Select(s => new BudgetDto(s.Family, s.Used, s.Budget, s.Remaining));
}
