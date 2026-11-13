namespace PodcastGenerator.Infrastructure.Tts;

/// <summary>Bound from "Tts:Google".</summary>
public class GoogleTtsOptions
{
    public const string SectionName = "Tts:Google";

    public string BaseUrl { get; set; } = "https://texttospeech.googleapis.com/v1/";

    /// <summary>Secret: API key restricted to the Cloud Text-to-Speech API. Set with user-secrets (Tts:Google:ApiKey).</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>All chunks use one sample rate so they join into a single consistent MP3.</summary>
    public int SampleRateHertz { get; set; } = 24000;

    public int TimeoutSeconds { get; set; } = 60;
}
