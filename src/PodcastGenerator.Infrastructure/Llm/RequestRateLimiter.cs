namespace PodcastGenerator.Infrastructure.Llm;

/// <summary>Sliding one-minute window shared by all LLM calls in the process.</summary>
public sealed class RequestRateLimiter(int requestsPerMinute, TimeProvider clock) : IDisposable
{
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private readonly Queue<DateTimeOffset> _sent = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task WaitAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            while (TryAcquire(clock.GetUtcNow()) is { } wait)
            {
                await Task.Delay(wait, clock, ct);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Records a request and returns null, or returns how long to wait before trying again.</summary>
    public TimeSpan? TryAcquire(DateTimeOffset now)
    {
        while (_sent.Count > 0 && now - _sent.Peek() >= Window)
        {
            _sent.Dequeue();
        }

        if (_sent.Count < requestsPerMinute)
        {
            _sent.Enqueue(now);
            return null;
        }

        return _sent.Peek() + Window - now;
    }

    public void Dispose() => _gate.Dispose();
}
