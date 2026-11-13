using Microsoft.AspNetCore.Mvc;
using PodcastGenerator.Api.Dtos;
using PodcastGenerator.Application.Preferences;

namespace PodcastGenerator.Api.Controllers;

[ApiController]
[Route("api/preferences")]
public class PreferencesController(IPreferencesService preferences) : ControllerBase
{
    [HttpGet]
    public async Task<PreferencesDto> Get(CancellationToken ct) =>
        PreferencesDto.From(await preferences.GetAsync(ct));

    [HttpPut]
    public async Task<PreferencesDto> Update(UpdatePreferencesRequest request, CancellationToken ct)
    {
        var updated = await preferences.UpdateAsync(
            new UpdatePreferences(
                request.NativeLanguage,
                request.TargetLanguage,
                request.DefaultLevel,
                request.DefaultDurationMin,
                request.Interests,
                request.VoiceName,
                request.VoiceModelFamily,
                request.PlaybackSpeed,
                request.SpeakingRate),
            ct);
        return PreferencesDto.From(updated);
    }
}
