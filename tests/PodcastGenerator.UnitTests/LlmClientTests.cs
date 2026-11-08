using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PodcastGenerator.Application.Llm;
using PodcastGenerator.Infrastructure.Llm;

namespace PodcastGenerator.UnitTests;

public class RequestRateLimiterTests
{
    [Fact]
    public void Allows_the_configured_requests_per_minute_then_reports_the_wait()
    {
        using var limiter = new RequestRateLimiter(2, TimeProvider.System);
        var t0 = DateTimeOffset.UnixEpoch;

        Assert.Null(limiter.TryAcquire(t0));
        Assert.Null(limiter.TryAcquire(t0.AddSeconds(10)));
        Assert.Equal(TimeSpan.FromSeconds(30), limiter.TryAcquire(t0.AddSeconds(30)));
        Assert.Null(limiter.TryAcquire(t0.AddSeconds(60))); // first slot expired
    }
}

public class RetryDelayTests
{
    private static readonly TimeSpan Base = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan Max = TimeSpan.FromSeconds(60);

    [Fact]
    public void Retry_after_from_the_server_wins() =>
        Assert.Equal(TimeSpan.FromSeconds(7), RetryDelay.Compute(3, TimeSpan.FromSeconds(7), Base, Max, new Random(1)));

    [Theory]
    [InlineData(0, 1, 2)]
    [InlineData(2, 4, 8)]
    [InlineData(10, 30, 60)] // capped
    public void Backoff_grows_exponentially_with_jitter_and_a_cap(int attempt, double minSeconds, double maxSeconds)
    {
        for (var seed = 0; seed < 20; seed++)
        {
            var delay = RetryDelay.Compute(attempt, null, Base, Max, new Random(seed)).TotalSeconds;
            Assert.InRange(delay, minSeconds, maxSeconds);
        }
    }
}

public class OpenAiCompatibleLlmProviderTests
{
    private const string Ok = """{"choices":[{"message":{"role":"assistant","content":"```json\n{\"title\":\"Hallo\"}\n```"}}]}""";

    [Fact]
    public async Task Retries_429_honoring_retry_after_then_succeeds()
    {
        var handler = new ScriptedHandler(TooMany(TimeSpan.Zero), Json(Ok));

        var result = await Create(handler).GenerateJsonAsync<TitleOnly>(Request(), CancellationToken.None);

        Assert.Equal("Hallo", result.Title);
        Assert.Equal(2, handler.Calls);
        Assert.Equal("Bearer", handler.LastAuthorization?.Scheme);
    }

    [Fact]
    public async Task Client_errors_fail_without_retrying()
    {
        var handler = new ScriptedHandler(new HttpResponseMessage(HttpStatusCode.Unauthorized));

        await Assert.ThrowsAsync<LlmException>(() => Create(handler).GenerateTextAsync(Request(), CancellationToken.None));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task A_long_retry_after_means_quota_exhausted_and_fails_fast()
    {
        var handler = new ScriptedHandler(TooMany(TimeSpan.FromHours(3)));

        var ex = await Assert.ThrowsAsync<LlmUnavailableException>(() => Create(handler).GenerateTextAsync(Request(), CancellationToken.None));
        Assert.Contains("quota", ex.Message);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Gives_up_after_max_retries()
    {
        var handler = new ScriptedHandler(Enumerable.Range(0, 3).Select(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)).ToArray());

        await Assert.ThrowsAsync<LlmUnavailableException>(() => Create(handler, maxRetries: 2).GenerateTextAsync(Request(), CancellationToken.None));
        Assert.Equal(3, handler.Calls);
    }

    [Fact]
    public async Task Missing_configuration_fails_with_a_clear_message()
    {
        var provider = Create(new ScriptedHandler(), apiKey: "");

        var ex = await Assert.ThrowsAsync<LlmException>(() => provider.GenerateTextAsync(Request(), CancellationToken.None));
        Assert.Contains("Llm:ApiKey", ex.Message);
    }

    [Fact]
    public async Task Non_json_content_is_an_invalid_response()
    {
        var handler = new ScriptedHandler(Json("""{"choices":[{"message":{"role":"assistant","content":"Sorry, no."}}]}"""));

        var ex = await Assert.ThrowsAsync<LlmInvalidResponseException>(() => Create(handler).GenerateJsonAsync<TitleOnly>(Request(), CancellationToken.None));
        Assert.Equal("Sorry, no.", ex.RawContent);
    }

    private static LlmRequest Request() => new("test", "system", "user");

    private static OpenAiCompatibleLlmProvider Create(ScriptedHandler handler, int maxRetries = 3, string apiKey = "test-key")
    {
        var options = Options.Create(new LlmOptions
        {
            BaseUrl = "https://llm.test/v1/",
            Model = "test-model",
            ApiKey = apiKey,
            MaxRetries = maxRetries,
            BaseDelayMs = 1,
            MaxDelaySeconds = 60,
            RequestsPerMinute = 1000,
        });
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://llm.test/v1/") };
        return new OpenAiCompatibleLlmProvider(
            http, options, new RequestRateLimiter(1000, TimeProvider.System), TimeProvider.System, NullLogger<OpenAiCompatibleLlmProvider>.Instance);
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
    };

    private static HttpResponseMessage TooMany(TimeSpan retryAfter)
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(retryAfter);
        return response;
    }

    private sealed record TitleOnly(string Title);

    private sealed class ScriptedHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);

        public int Calls { get; private set; }

        public AuthenticationHeaderValue? LastAuthorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            LastAuthorization = request.Headers.Authorization;
            return Task.FromResult(_responses.Dequeue());
        }

        protected override void Dispose(bool disposing)
        {
            foreach (var r in _responses)
            {
                r.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
