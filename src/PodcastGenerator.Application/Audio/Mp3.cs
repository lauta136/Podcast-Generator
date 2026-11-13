namespace PodcastGenerator.Application.Audio;

/// <summary>
/// Minimal MPEG audio (Layer III) frame walker: measures duration and joins MP3 chunks into one file.
/// Enough for the CBR MP3 that TTS providers return, without ffmpeg or extra packages.
/// </summary>
public static class Mp3
{
    // Bitrates in kbps for Layer III, indexed [isMpeg1][bitrateIndex].
    private static readonly int[][] Bitrates =
    [
        [0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160, 0], // MPEG-2 / 2.5
        [0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 0], // MPEG-1
    ];

    // Sample rates in Hz, indexed [versionBits][sampleRateIndex]; versionBits: 0=2.5, 2=2, 3=1.
    private static readonly int[][] SampleRates =
    [
        [11025, 12000, 8000],
        [0, 0, 0],
        [22050, 24000, 16000],
        [44100, 48000, 32000],
    ];

    public readonly record struct Frame(int Offset, int Length, int SampleRate, int Samples);

    /// <summary>Total playing time in seconds.</summary>
    public static double DurationSeconds(ReadOnlySpan<byte> mp3)
    {
        // Xing/Info/VBRI frames carry metadata, not audio, and are dropped when joining: don't count them.
        double seconds = 0;
        foreach (var frame in Frames(mp3))
        {
            if (!IsInfoFrame(mp3.Slice(frame.Offset, frame.Length)))
            {
                seconds += frame.Samples / (double)frame.SampleRate;
            }
        }

        return seconds;
    }

    /// <summary>
    /// Joins chunks into one stream of plain audio frames. ID3 tags and Xing/Info/VBRI header frames are dropped:
    /// a header frame from the first chunk would make players report the first chunk's length as the whole file.
    /// </summary>
    public static byte[] Concatenate(IEnumerable<byte[]> chunks)
    {
        using var output = new MemoryStream();
        foreach (var chunk in chunks)
        {
            foreach (var frame in Frames(chunk))
            {
                if (!IsInfoFrame(chunk.AsSpan(frame.Offset, frame.Length)))
                {
                    output.Write(chunk, frame.Offset, frame.Length);
                }
            }
        }

        return output.ToArray();
    }

    public static List<Frame> Frames(ReadOnlySpan<byte> data)
    {
        var frames = new List<Frame>();
        var i = SkipId3v2(data);
        while (i + 4 <= data.Length)
        {
            if (TryParseHeader(data[i..], out var length, out var sampleRate, out var samples) && i + length <= data.Length)
            {
                frames.Add(new Frame(i, length, sampleRate, samples));
                i += length;
            }
            else
            {
                i++; // resync (stray bytes, ID3v1 "TAG" trailer)
            }
        }

        return frames;
    }

    private static bool TryParseHeader(ReadOnlySpan<byte> h, out int length, out int sampleRate, out int samples)
    {
        length = sampleRate = samples = 0;
        if (h[0] != 0xFF || (h[1] & 0xE0) != 0xE0)
        {
            return false;
        }

        var versionBits = (h[1] >> 3) & 0x03;
        var layerBits = (h[1] >> 1) & 0x03;
        var bitrateIndex = (h[2] >> 4) & 0x0F;
        var sampleRateIndex = (h[2] >> 2) & 0x03;
        var padding = (h[2] >> 1) & 0x01;
        if (versionBits == 1 || layerBits != 1 || bitrateIndex is 0 or 15 || sampleRateIndex == 3)
        {
            return false; // reserved values, or not Layer III
        }

        var isMpeg1 = versionBits == 3;
        var bitrate = Bitrates[isMpeg1 ? 1 : 0][bitrateIndex] * 1000;
        sampleRate = SampleRates[versionBits][sampleRateIndex];
        samples = isMpeg1 ? 1152 : 576;
        length = ((isMpeg1 ? 144 : 72) * bitrate / sampleRate) + padding;
        return length > 4;
    }

    private static int SkipId3v2(ReadOnlySpan<byte> data)
    {
        if (data.Length < 10 || data[0] != 'I' || data[1] != 'D' || data[2] != '3')
        {
            return 0;
        }

        // Size is "syncsafe": 4 bytes of 7 bits each.
        var size = (data[6] << 21) | (data[7] << 14) | (data[8] << 7) | data[9];
        return 10 + size;
    }

    private static bool IsInfoFrame(ReadOnlySpan<byte> frame)
    {
        var head = frame[..Math.Min(frame.Length, 64)];
        return head.IndexOf("Xing"u8) >= 0 || head.IndexOf("Info"u8) >= 0 || head.IndexOf("VBRI"u8) >= 0;
    }
}
