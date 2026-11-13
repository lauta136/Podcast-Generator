namespace PodcastGenerator.Application.Tts;

public interface IAudioStorage
{
    Task<string> SaveAsync(string key, Stream audio, string contentType, CancellationToken ct);

    /// <summary>Opens a seekable stream (needed for HTTP Range requests).</summary>
    Task<Stream> OpenReadAsync(string key, CancellationToken ct);

    Task<bool> ExistsAsync(string key, CancellationToken ct);

    Task DeleteAsync(string key, CancellationToken ct);
}
