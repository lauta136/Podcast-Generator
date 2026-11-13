using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using PodcastGenerator.Application.Audio;
using PodcastGenerator.Application.Tts;
using PodcastGenerator.Domain.Enums;

namespace PodcastGenerator.Infrastructure.Tts;

/// <summary>
/// Google Cloud Text-to-Speech over REST with an API key. Exactly one HTTP attempt per call: the pipeline reserves
/// budget before each attempt and owns retries, so no retried request can bypass the budget.
/// </summary>
public sealed class GoogleCloudTtsProvider(HttpClient http, IOptions<GoogleTtsOptions> options) : ITtsProvider
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string Name => "google";

    public async Task<TtsResult> SynthesizeAsync(TtsRequest request, CancellationToken ct)
    {
        var body = new SynthesizeRequest(
            new SynthesisInput(request.Text),
            new VoiceSelection(request.LanguageCode, request.VoiceName),
            new AudioConfig("MP3", options.Value.SampleRateHertz, request.SpeakingRate));

        // "./" matters: a bare "text:synthesize" is an absolute URI with the scheme "text", not a path.
        using var message = new HttpRequestMessage(HttpMethod.Post, "./text:synthesize") { Content = JsonContent.Create(body, options: Json) };
        var response = await SendAsync(message, ct);
        var result = await response.Content.ReadFromJsonAsync<SynthesizeResponse>(Json, ct);
        if (string.IsNullOrEmpty(result?.AudioContent))
        {
            throw new TtsException("Text-to-Speech returned no audio.");
        }

        var mp3 = Convert.FromBase64String(result.AudioContent);
        var duration = Mp3.DurationSeconds(mp3);
        return duration > 0 ? new TtsResult(mp3, duration) : throw new TtsException("Text-to-Speech returned audio that is not a readable MP3.");
    }

    public async Task<IReadOnlyList<VoiceInfo>> GetVoicesAsync(string languageCode, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, $"voices?languageCode={Uri.EscapeDataString(languageCode)}");
        var response = await SendAsync(message, ct);
        var result = await response.Content.ReadFromJsonAsync<VoicesResponse>(Json, ct);
        return (result?.Voices ?? [])
            .Select(v => (Voice: v, Family: FamilyOf(v.Name)))
            .Where(x => x.Family is not null)
            .Select(x => new VoiceInfo(x.Voice.Name, x.Voice.LanguageCodes is [var code, ..] ? code : languageCode, x.Voice.SsmlGender ?? "", x.Family!.Value))
            .OrderBy(v => v.ModelFamily).ThenBy(v => v.Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Only the families the app budgets for; Standard, Neural2, Studio etc. are not offered.</summary>
    public static TtsModelFamily? FamilyOf(string voiceName) =>
        voiceName.Contains("-Wavenet-", StringComparison.OrdinalIgnoreCase) ? TtsModelFamily.WaveNet
        : voiceName.Contains("-Chirp3-HD-", StringComparison.OrdinalIgnoreCase) ? TtsModelFamily.Chirp3HD
        : null;

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage message, CancellationToken ct)
    {
        var key = options.Value.ApiKey;
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new TtsException("Text-to-Speech is not configured: set Tts:Google:ApiKey (see README).");
        }

        message.Headers.Add("X-Goog-Api-Key", key); // header, never the URL: URLs end up in logs

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(message, ct);
        }
        catch (HttpRequestException e)
        {
            throw new TtsTransientException($"Text-to-Speech unreachable: {e.Message}", e);
        }
        catch (TaskCanceledException e) when (!ct.IsCancellationRequested)
        {
            throw new TtsTransientException("Text-to-Speech timed out.", e);
        }

        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        var error = await response.Content.ReadAsStringAsync(ct);
        response.Dispose();
        var text = $"Text-to-Speech failed with {(int)response.StatusCode}: {(error.Length > 400 ? error[..400] : error)}";
        throw response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500
            ? new TtsTransientException(text)
            : new TtsException(text);
    }

    private sealed record SynthesizeRequest(SynthesisInput Input, VoiceSelection Voice, AudioConfig AudioConfig);

    private sealed record SynthesisInput(string Text);

    private sealed record VoiceSelection(string LanguageCode, string Name);

    private sealed record AudioConfig(string AudioEncoding, int SampleRateHertz, decimal SpeakingRate);

    private sealed record SynthesizeResponse(string? AudioContent);

    private sealed record VoicesResponse(IReadOnlyList<GoogleVoice>? Voices);

    private sealed record GoogleVoice(string Name, IReadOnlyList<string> LanguageCodes, string? SsmlGender);
}
