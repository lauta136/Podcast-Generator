using Microsoft.EntityFrameworkCore;
using PodcastGenerator.Application.Tts;
using PodcastGenerator.Domain.Entities;
using PodcastGenerator.Domain.Enums;

namespace PodcastGenerator.Infrastructure.Persistence.Repositories;

public class TtsUsageRepository(AppDbContext db) : ITtsUsageRepository
{
    public async Task<bool> TryReserveAsync(TtsModelFamily family, DateOnly month, long chars, long budget, CancellationToken ct)
    {
        var f = family.ToString();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""INSERT INTO "TtsUsage" ("ModelFamily", "Month", "Characters") VALUES ({f}, {month}, 0) ON CONFLICT DO NOTHING""", ct);

        // Single conditional UPDATE: concurrent reservations can't both squeeze past the budget.
        var updated = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE "TtsUsage" SET "Characters" = "Characters" + {chars}
            WHERE "ModelFamily" = {f} AND "Month" = {month} AND "Characters" + {chars} <= {budget}
            """,
            ct);
        return updated == 1;
    }

    public async Task<IReadOnlyList<TtsUsage>> GetMonthAsync(DateOnly month, CancellationToken ct) =>
        await db.TtsUsage.AsNoTracking().Where(u => u.Month == month).ToListAsync(ct);
}
