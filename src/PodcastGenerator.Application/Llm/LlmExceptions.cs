namespace PodcastGenerator.Application.Llm;

public class LlmException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>The reply wasn't valid JSON or didn't match the expected shape.</summary>
public class LlmInvalidResponseException(string message, string rawContent) : LlmException(message)
{
    public string RawContent { get; } = rawContent;
}

/// <summary>Rate limited or unavailable after all retries. The step can be retried later.</summary>
public class LlmUnavailableException(string message, Exception? inner = null) : LlmException(message, inner);
