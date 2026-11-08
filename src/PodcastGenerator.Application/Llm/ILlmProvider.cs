namespace PodcastGenerator.Application.Llm;

public interface ILlmProvider
{
    /// <summary>Requests a JSON object and deserializes it. Throws <see cref="LlmInvalidResponseException"/> if it doesn't parse.</summary>
    Task<T> GenerateJsonAsync<T>(LlmRequest request, CancellationToken ct);

    Task<string> GenerateTextAsync(LlmRequest request, CancellationToken ct);
}

/// <param name="Purpose">Short label ("outline", "section") for logs and test fakes. Never sent to the provider.</param>
public sealed record LlmRequest(string Purpose, string SystemPrompt, string UserPrompt, double Temperature = 0.8);
