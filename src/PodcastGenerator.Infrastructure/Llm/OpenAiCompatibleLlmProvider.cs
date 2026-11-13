using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PodcastGenerator.Application.Llm;

namespace PodcastGenerator.Infrastructure.Llm;

/// <summary>
/// Chat-completions client for any OpenAI-compatible API (Gemini, Groq). "OpenAI-compatible" is only
/// the wire format; no OpenAI models are involved.
/// </summary>
public sealed partial class OpenAiCompatibleLlmProvider(
    HttpClient http,
    IOptions<LlmOptions> options,
    RequestRateLimiter limiter,
    TimeProvider clock,
    ILogger<OpenAiCompatibleLlmProvider> logger) : ILlmProvider
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public Task<string> GenerateTextAsync(LlmRequest request, CancellationToken ct) => SendAsync(request, json: false, ct);

    public async Task<T> GenerateJsonAsync<T>(LlmRequest request, CancellationToken ct)
    {
        var content = await SendAsync(request, json: true, ct);
        var cleaned = StripCodeFence(content);
        try
        {
            return JsonSerializer.Deserialize<T>(cleaned, Json)
                ?? throw new LlmInvalidResponseException("The reply was JSON null.", content);
        }
        catch (JsonException e)
        {
            throw new LlmInvalidResponseException($"The reply was not valid JSON: {e.Message}", content);
        }
    }

    private async Task<string> SendAsync(LlmRequest request, bool json, CancellationToken ct)
    {
        var o = options.Value;
        if (string.IsNullOrWhiteSpace(o.ApiKey) || string.IsNullOrWhiteSpace(o.Model))
        {
            throw new LlmException("LLM is not configured: set Llm:ApiKey and Llm:Model (see README).");
        }

        try
        {
            return await SendToModelAsync(o.Model, request, json, ct);
        }
        catch (LlmUnavailableException e) when (!string.IsNullOrWhiteSpace(o.FallbackModel) && o.FallbackModel != o.Model)
        {
            // Overload and free-tier quotas are per model, so a smaller model is often still available.
            LogFallback(logger, request.Purpose, o.Model, o.FallbackModel, e.Message);
            return await SendToModelAsync(o.FallbackModel, request, json, ct);
        }
    }

    private async Task<string> SendToModelAsync(string model, LlmRequest request, bool json, CancellationToken ct)
    {
        var o = options.Value;
        var body = new ChatRequest(
            model,
            [new("system", request.SystemPrompt), new("user", request.UserPrompt)],
            request.Temperature,
            json ? new ResponseFormat("json_object") : null,
            string.IsNullOrWhiteSpace(o.ReasoningEffort) ? null : o.ReasoningEffort);

        Exception? lastError = null;
        var timeouts = 0;
        for (var attempt = 0; attempt <= o.MaxRetries; attempt++)
        {
            await limiter.WaitAsync(ct);

            TimeSpan? retryAfter = null;
            var overloaded = false;
            try
            {
                using var message = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
                {
                    Content = JsonContent.Create(body, options: Json),
                };
                message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", o.ApiKey);

                using var response = await http.SendAsync(message, ct);
                if (response.IsSuccessStatusCode)
                {
                    ChatResponse? result;
                    try
                    {
                        result = await response.Content.ReadFromJsonAsync<ChatResponse>(Json, ct);
                    }
                    catch (JsonException e)
                    {
                        throw new LlmInvalidResponseException($"The reply was not valid JSON: {e.Message}", "");
                    }

                    return (result?.Choices is [{ Message: { } reply }, ..] ? reply.Content : null)
                        ?? throw new LlmInvalidResponseException("The reply had no message content.", "");
                }

                var errorBody = await response.Content.ReadAsStringAsync(ct);
                if (response.StatusCode != HttpStatusCode.TooManyRequests && (int)response.StatusCode < 500)
                {
                    // 400/401/403/404: retrying won't help. Never log the request (it has the key header).
                    throw new LlmException($"LLM request to {model} failed with {(int)response.StatusCode}: {Truncate(errorBody, 500)}");
                }

                retryAfter = ParseRetryAfter(response.Headers.RetryAfter, clock.GetUtcNow());
                overloaded = (int)response.StatusCode >= 500;
                lastError = new LlmUnavailableException($"{model} returned {(int)response.StatusCode}: {Truncate(errorBody, 300)}");
            }
            catch (HttpRequestException e)
            {
                lastError = e;
                overloaded = true;
            }
            catch (TaskCanceledException e) when (!ct.IsCancellationRequested)
            {
                // HttpClient timeout. Each one may still count against the provider quota, so retry sparingly.
                lastError = e;
                overloaded = true;
                if (++timeouts > o.MaxTimeoutRetries)
                {
                    break;
                }
            }

            if (attempt == o.MaxRetries)
            {
                break;
            }

            if (retryAfter > TimeSpan.FromSeconds(o.MaxDelaySeconds))
            {
                throw new LlmUnavailableException(
                    $"{model} quota exhausted; the provider asks to wait {retryAfter.Value.TotalMinutes:F0} min. Retry the episode later.",
                    lastError);
            }

            var baseDelay = TimeSpan.FromMilliseconds(overloaded ? o.OverloadBaseDelayMs : o.BaseDelayMs);
            var delay = RetryDelay.Compute(attempt, retryAfter, baseDelay, TimeSpan.FromSeconds(o.MaxDelaySeconds), Random.Shared);
            LogRetry(logger, request.Purpose, attempt + 1, delay.TotalSeconds, lastError?.Message);
            await Task.Delay(delay, clock, ct);
        }

        throw new LlmUnavailableException($"{model} unavailable after retries: {lastError?.Message}", lastError);
    }

    internal static TimeSpan? ParseRetryAfter(RetryConditionHeaderValue? header, DateTimeOffset now) => header switch
    {
        { Delta: { } delta } => delta,
        { Date: { } date } => date > now ? date - now : TimeSpan.Zero,
        _ => null,
    };

    internal static string StripCodeFence(string content)
    {
        var s = content.Trim();
        if (!s.StartsWith("```", StringComparison.Ordinal))
        {
            return s;
        }

        var firstNewline = s.IndexOf('\n', StringComparison.Ordinal);
        var lastFence = s.LastIndexOf("```", StringComparison.Ordinal);
        return firstNewline > 0 && lastFence > firstNewline ? s[(firstNewline + 1)..lastFence].Trim() : s;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    [LoggerMessage(Level = LogLevel.Warning, Message = "LLM {Purpose}: {Model} unavailable, switching to {FallbackModel}: {Error}")]
    private static partial void LogFallback(ILogger logger, string purpose, string model, string fallbackModel, string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "LLM {Purpose} attempt {Attempt} failed, retrying in {DelaySeconds:F1}s: {Error}")]
    private static partial void LogRetry(ILogger logger, string purpose, int attempt, double delaySeconds, string? error);

    private sealed record ChatRequest(
        string Model,
        IReadOnlyList<ChatMessage> Messages,
        double Temperature,
        [property: JsonPropertyName("response_format")] ResponseFormat? ResponseFormat,
        [property: JsonPropertyName("reasoning_effort")] string? ReasoningEffort);

    private sealed record ChatMessage(string Role, string Content);

    private sealed record ResponseFormat(string Type);

    private sealed record ChatResponse(IReadOnlyList<ChatChoice>? Choices);

    private sealed record ChatChoice(ChatMessage? Message);
}
