namespace PodcastGenerator.Infrastructure.Llm;

/// <summary>Bound from "Llm". Any OpenAI-compatible chat-completions endpoint (Gemini, Groq).</summary>
public class LlmOptions
{
    public const string SectionName = "Llm";

    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta/openai/";
    public string Model { get; set; } = "";

    /// <summary>Used when <see cref="Model"/> stays overloaded or out of quota. Empty disables fallback.</summary>
    public string FallbackModel { get; set; } = "";

    /// <summary>Secret: set with dotnet user-secrets or the Llm__ApiKey env var.</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>Optional, e.g. "low" for thinking models. Omitted from the request when empty.</summary>
    public string? ReasoningEffort { get; set; }

    public int RequestsPerMinute { get; set; } = 10;
    public int MaxRetries { get; set; } = 5;
    public int BaseDelayMs { get; set; } = 2000;

    /// <summary>Longer base backoff for 5xx and timeouts: an overloaded model needs minutes, not seconds.</summary>
    public int OverloadBaseDelayMs { get; set; } = 10000;

    /// <summary>Timed-out requests may still use quota, so they get fewer retries than quick errors.</summary>
    public int MaxTimeoutRetries { get; set; } = 1;

    /// <summary>Longest wait between retries. A longer Retry-After (e.g. daily quota) fails the step instead.</summary>
    public int MaxDelaySeconds { get; set; } = 60;

    public int TimeoutSeconds { get; set; } = 120;
}
