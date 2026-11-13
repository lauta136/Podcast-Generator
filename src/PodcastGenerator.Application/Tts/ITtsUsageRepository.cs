using PodcastGenerator.Domain.Entities;
using PodcastGenerator.Domain.Enums;

namespace PodcastGenerator.Application.Tts;

public interface ITtsUsageRepository
{
    /// <summary>
    /// Atomically adds <paramref name="chars"/> to the month's usage if the total stays within <paramref name="budget"/>.
    /// Returns false (and changes nothing) otherwise. Safe under concurrent callers.
    /// </summary>
    Task<bool> TryReserveAsync(TtsModelFamily family, DateOnly month, long chars, long budget, CancellationToken ct);

    Task<IReadOnlyList<TtsUsage>> GetMonthAsync(DateOnly month, CancellationToken ct);
}
