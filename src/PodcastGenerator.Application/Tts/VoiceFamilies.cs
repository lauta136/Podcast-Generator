using PodcastGenerator.Domain.Enums;

namespace PodcastGenerator.Application.Tts;

/// <summary>
/// The voice name decides what Google bills, so the budget family is derived from it, never trusted separately.
/// Only the families the app budgets for are recognized; Standard, Neural2, Studio etc. return null.
/// </summary>
public static class VoiceFamilies
{
    public static TtsModelFamily? Of(string voiceName) =>
        voiceName.Contains("-Wavenet-", StringComparison.OrdinalIgnoreCase) ? TtsModelFamily.WaveNet
        : voiceName.Contains("-Chirp3-HD-", StringComparison.OrdinalIgnoreCase) ? TtsModelFamily.Chirp3HD
        : null;
}
