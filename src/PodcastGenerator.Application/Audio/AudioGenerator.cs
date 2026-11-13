using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PodcastGenerator.Application.Episodes;
using PodcastGenerator.Application.Tts;
using PodcastGenerator.Domain.Entities;
using PodcastGenerator.Domain.Enums;

namespace PodcastGenerator.Application.Audio;

/// <summary>
/// Pipeline step after the text: one TTS request per transcript paragraph, then one stitched MP3 with
/// sentence timings. Finished chunks are never synthesized again; budget is reserved before every request.
/// </summary>
public partial class AudioGenerator(
    IEpisodeRepository episodes,
    ITtsProvider tts,
    IAudioStorage storage,
    TtsBudget budget,
    IOptions<TtsOptions> ttsOptions,
    ILogger<AudioGenerator> logger)
{
    public const int MaxAttemptsPerRun = 3;
    public const string ContentType = "audio/mpeg";

    public static string EpisodeAudioKey(Guid episodeId) => $"episodes/{episodeId:N}/episode.mp3";

    public static string ChunkKey(Guid episodeId, int order) => $"episodes/{episodeId:N}/chunks/{order:D4}.mp3";

    public async Task RunAsync(Episode episode, CancellationToken ct)
    {
        if (episode.AudioStorageKey is not null)
        {
            return;
        }

        await PlanChunksAsync(episode, ct);

        foreach (var chunk in episode.AudioSegments.OrderBy(a => a.Order).Where(a => a.Status != StepStatus.Completed).ToList())
        {
            await SynthesizeChunkAsync(episode, chunk, ct);
        }

        await StitchAsync(episode, ct);
    }

    /// <summary>Creates one chunk per paragraph; keeps existing chunks whose text, voice and model are unchanged.</summary>
    private async Task PlanChunksAsync(Episode episode, CancellationToken ct)
    {
        var voice = ttsOptions.Value.ResolveVoice(episode.VoiceName, episode.VoiceModelFamily);
        var wanted = Paragraphs(episode)
            .Select((p, order) => (p.Section, p.ParagraphIndex, p.Text, Order: order, Key: CacheKey(p.Text, voice, episode.VoiceModelFamily, tts.Name)))
            .ToList();

        var changed = false;
        foreach (var stale in episode.AudioSegments
                     .Where(a => a.Order >= wanted.Count || a.CacheKey != wanted[a.Order].Key).ToList())
        {
            if (stale.StorageKey is not null)
            {
                await storage.DeleteAsync(stale.StorageKey, ct);
            }

            episode.AudioSegments.Remove(stale);
            changed = true;
        }

        foreach (var w in wanted.Where(w => episode.AudioSegments.All(a => a.Order != w.Order)))
        {
            episode.AudioSegments.Add(new AudioSegment
            {
                SectionId = w.Section.Id,
                ParagraphIndex = w.ParagraphIndex,
                Order = w.Order,
                CacheKey = w.Key,
                Provider = tts.Name,
                VoiceName = voice,
                ModelFamily = episode.VoiceModelFamily,
                Characters = TtsBudget.BillableChars(w.Text),
            });
            changed = true;
        }

        if (changed)
        {
            await episodes.SaveChangesAsync(ct);
        }
    }

    private async Task SynthesizeChunkAsync(Episode episode, AudioSegment chunk, CancellationToken ct)
    {
        var text = Paragraphs(episode).ElementAt(chunk.Order).Text;
        for (var attempt = 1; ; attempt++)
        {
            // Reserve first: throws TtsBudgetExceededException (episode pauses) before anything is sent.
            await budget.ReserveAsync(chunk.ModelFamily, chunk.Characters, ct);
            chunk.Attempts++;
            try
            {
                var result = await tts.SynthesizeAsync(new TtsRequest(text, LanguageTag(episode.LanguageCode), chunk.VoiceName, chunk.ModelFamily), ct);
                var key = ChunkKey(episode.Id, chunk.Order);
                using (var audio = new MemoryStream(result.Mp3))
                {
                    await storage.SaveAsync(key, audio, ContentType, ct);
                }

                chunk.StorageKey = key;
                chunk.DurationSec = result.DurationSec;
                chunk.Status = StepStatus.Completed;
                chunk.LastError = null;
                await episodes.SaveChangesAsync(ct);
                return;
            }
            catch (TtsTransientException e) when (attempt < MaxAttemptsPerRun)
            {
                LogChunkRetry(logger, episode.Id, chunk.Order, attempt, e.Message);
                await episodes.SaveChangesAsync(ct); // keep the attempt count
                await Task.Delay(TimeSpan.FromMilliseconds(ttsOptions.Value.ChunkRetryDelayMs * attempt), ct);
            }
            catch (TtsException e)
            {
                chunk.Status = StepStatus.Failed;
                chunk.LastError = e.Message.Length <= 1000 ? e.Message : e.Message[..1000];
                await episodes.SaveChangesAsync(CancellationToken.None);
                throw;
            }
        }
    }

    /// <summary>Joins the chunks and gives every sentence a start/end time (split by character share within its paragraph).</summary>
    private async Task StitchAsync(Episode episode, CancellationToken ct)
    {
        var chunks = episode.AudioSegments.OrderBy(a => a.Order).ToList();
        var bytes = new List<byte[]>(chunks.Count);
        foreach (var chunk in chunks)
        {
            await using var stream = await storage.OpenReadAsync(chunk.StorageKey!, ct);
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, ct);
            bytes.Add(buffer.ToArray());
        }

        var paragraphs = Paragraphs(episode).ToList();
        double offset = 0;
        foreach (var chunk in chunks)
        {
            var sentences = paragraphs[chunk.Order].Sentences;
            var totalChars = Math.Max(1, sentences.Sum(s => s.Text.Length));
            var t = offset;
            foreach (var sentence in sentences)
            {
                var share = chunk.DurationSec!.Value * sentence.Text.Length / totalChars;
                sentence.StartSec = Math.Round(t, 3);
                sentence.EndSec = Math.Round(t + share, 3);
                t += share;
            }

            offset += chunk.DurationSec!.Value;
        }

        var stitched = Mp3.Concatenate(bytes);
        var key = EpisodeAudioKey(episode.Id);
        using (var audio = new MemoryStream(stitched))
        {
            await storage.SaveAsync(key, audio, ContentType, ct);
        }

        episode.AudioStorageKey = key;
        episode.AudioDurationSec = Math.Round(offset, 3);
        await episodes.SaveChangesAsync(ct);
    }

    /// <summary>Episode paragraphs in playback order (completed sections only).</summary>
    private static IEnumerable<(EpisodeSection Section, int ParagraphIndex, string Text, List<TranscriptSegment> Sentences)> Paragraphs(Episode episode) =>
        episode.Sections
            .Where(s => s.Status == StepStatus.Completed)
            .OrderBy(s => s.Order)
            .SelectMany(s => s.Segments
                .GroupBy(t => t.ParagraphIndex)
                .OrderBy(g => g.Key)
                .Select(g =>
                {
                    var sentences = g.OrderBy(t => t.Order).ToList();
                    return (s, g.Key, string.Join(" ", sentences.Select(t => t.Text)), sentences);
                }));

    public static string CacheKey(string text, string voice, TtsModelFamily family, string provider) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{provider}\n{family}\n{voice}\n{text}")));

    /// <summary>"de" → "de-DE". Episodes store ISO 639-1; TTS voices need a region.</summary>
    public static string LanguageTag(string languageCode) => languageCode switch
    {
        "de" => "de-DE",
        _ => languageCode,
    };

    [LoggerMessage(Level = LogLevel.Warning, Message = "Episode {EpisodeId} audio chunk {Chunk} attempt {Attempt} failed, retrying: {Error}")]
    private static partial void LogChunkRetry(ILogger logger, Guid episodeId, int chunk, int attempt, string error);
}
