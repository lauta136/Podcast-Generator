using System.Text;
using Microsoft.Extensions.Options;
using PodcastGenerator.Domain.Enums;

namespace PodcastGenerator.Application.Tts;

public sealed record BudgetStatus(TtsModelFamily Family, long Used, long Budget)
{
    public long Remaining => Math.Max(0, Budget - Used);
}

/// <summary>
/// Free-tier guard. Characters are reserved before every request and never refunded, failures included,
/// so the count can only overestimate what the provider bills.
/// </summary>
public class TtsBudget(ITtsUsageRepository usage, IOptions<TtsOptions> options, TimeProvider clock)
{
    /// <summary>Google bills WaveNet/Standard by bytes; ä, ö, ü and ß are 2 bytes in UTF-8. Counting bytes is never lower.</summary>
    public static int BillableChars(string text) => Encoding.UTF8.GetByteCount(text);

    /// <summary>
    /// The billing month as the provider counts it. Google Cloud billing months follow Pacific time, so on the
    /// 1st before 08:00 UTC Google is still counting the previous month; a UTC month would reset too early.
    /// </summary>
    public DateOnly CurrentMonth()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(options.Value.BillingTimeZone);
        var now = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone);
        return new DateOnly(now.Year, now.Month, 1);
    }

    public async Task ReserveAsync(TtsModelFamily family, int chars, CancellationToken ct)
    {
        var budget = Budget(family);
        if (!await usage.TryReserveAsync(family, CurrentMonth(), chars, budget, ct))
        {
            var used = (await GetStatusAsync(ct)).Single(s => s.Family == family).Used;
            throw new TtsBudgetExceededException(family, used, budget);
        }
    }

    public async Task<IReadOnlyList<BudgetStatus>> GetStatusAsync(CancellationToken ct)
    {
        var rows = await usage.GetMonthAsync(CurrentMonth(), ct);
        return Enum.GetValues<TtsModelFamily>()
            .Select(f => new BudgetStatus(f, rows.FirstOrDefault(r => r.ModelFamily == f)?.Characters ?? 0, Budget(f)))
            .ToList();
    }

    private long Budget(TtsModelFamily family) =>
        options.Value.Budgets.TryGetValue(family, out var b) ? b.MonthlyChars : 0; // unknown family: nothing allowed
}
