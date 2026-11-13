using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using PodcastGenerator.Application.Tts;

namespace PodcastGenerator.Infrastructure.Storage;

public class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>Folder for audio files; relative paths are resolved against the app's content root.</summary>
    public string LocalPath { get; set; } = "audio-storage";
}

/// <summary>Stores audio as files. Keys are relative paths like "episodes/{id}/episode.mp3".</summary>
public class LocalFileAudioStorage(IOptions<StorageOptions> options, IHostEnvironment env) : IAudioStorage
{
    // Trimmed: "/var/audio/" would otherwise become "/var/audio//" in the escape check and reject every key.
    private string Root => Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(env.ContentRootPath, options.Value.LocalPath)));

    public async Task<string> SaveAsync(string key, Stream audio, string contentType, CancellationToken ct)
    {
        var path = PathFor(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // Write to a temp file and move, so a crash never leaves a truncated file under the final key.
        var temp = path + ".tmp";
        await using (var file = File.Create(temp))
        {
            await audio.CopyToAsync(file, ct);
        }

        File.Move(temp, path, overwrite: true);
        return key;
    }

    public Task<Stream> OpenReadAsync(string key, CancellationToken ct)
    {
        var path = PathFor(key);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Audio '{key}' not found.");
        }

        return Task.FromResult<Stream>(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true));
    }

    public Task<bool> ExistsAsync(string key, CancellationToken ct) => Task.FromResult(File.Exists(PathFor(key)));

    public Task DeleteAsync(string key, CancellationToken ct)
    {
        var path = PathFor(key);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    private string PathFor(string key)
    {
        var path = Path.GetFullPath(Path.Combine(Root, key));
        if (!path.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new ArgumentException($"Invalid storage key '{key}'.", nameof(key)); // blocks "../" escapes
        }

        return path;
    }
}
