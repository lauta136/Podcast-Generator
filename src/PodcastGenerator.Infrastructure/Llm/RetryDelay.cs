namespace PodcastGenerator.Infrastructure.Llm;

public static class RetryDelay
{
    /// <summary>
    /// Honors Retry-After when the server sends it; otherwise exponential backoff with "equal jitter"
    /// (half fixed, half random) capped at <paramref name="max"/>.
    /// </summary>
    public static TimeSpan Compute(int attempt, TimeSpan? retryAfter, TimeSpan baseDelay, TimeSpan max, Random random)
    {
        if (retryAfter is { } serverDelay)
        {
            return serverDelay;
        }

        var exponential = baseDelay.TotalMilliseconds * Math.Pow(2, attempt);
        var capped = Math.Min(exponential, max.TotalMilliseconds);
        return TimeSpan.FromMilliseconds((capped / 2) + (random.NextDouble() * capped / 2));
    }
}
