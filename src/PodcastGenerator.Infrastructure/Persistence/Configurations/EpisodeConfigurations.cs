using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PodcastGenerator.Domain.Entities;

namespace PodcastGenerator.Infrastructure.Persistence.Configurations;

public class SeasonConfiguration : IEntityTypeConfiguration<Season>
{
    public void Configure(EntityTypeBuilder<Season> builder)
    {
        builder.Property(s => s.Title).HasMaxLength(200);
        builder.Property(s => s.Topic).HasMaxLength(500);
        builder.Property(s => s.ArcJson).HasColumnType("jsonb");
        builder.HasOne<User>().WithMany().HasForeignKey(s => s.UserId);
        builder.HasMany(s => s.Episodes).WithOne(e => e.Season).HasForeignKey(e => e.SeasonId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(s => s.UserId);
    }
}

public class EpisodeConfiguration : IEntityTypeConfiguration<Episode>
{
    public void Configure(EntityTypeBuilder<Episode> builder)
    {
        builder.Property(e => e.Topic).HasMaxLength(500);
        builder.Property(e => e.LanguageCode).HasMaxLength(8);
        builder.Property(e => e.Title).HasMaxLength(200);
        builder.Property(e => e.Description).HasMaxLength(2000);
        builder.Property(e => e.Summary).HasMaxLength(2000);
        builder.Property(e => e.VoiceName).HasMaxLength(100);
        builder.Property(e => e.AudioStorageKey).HasMaxLength(300);
        builder.HasOne<User>().WithMany().HasForeignKey(e => e.UserId);
        builder.HasMany(e => e.Sections).WithOne().HasForeignKey(s => s.EpisodeId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(e => e.AudioSegments).WithOne().HasForeignKey(a => a.EpisodeId).OnDelete(DeleteBehavior.Cascade);

        // Library: newest first per user, filtered by level/status/language, searched by title/topic.
        builder.HasIndex(e => new { e.UserId, e.CreatedAt });
        builder.HasIndex(e => new { e.UserId, e.Status });
        builder.HasIndex(e => new { e.UserId, e.Level });
        builder.HasIndex(e => new { e.UserId, e.LanguageCode });
        builder.HasIndex(e => new { e.SeasonId, e.SeasonOrder });
        builder.HasIndex(e => e.Title);
        builder.HasIndex(e => e.Topic);
    }
}

public class EpisodeSectionConfiguration : IEntityTypeConfiguration<EpisodeSection>
{
    public void Configure(EntityTypeBuilder<EpisodeSection> builder)
    {
        builder.Property(s => s.Title).HasMaxLength(200);
        builder.Property(s => s.Summary).HasMaxLength(2000);
        builder.Property(s => s.Focus).HasMaxLength(300);
        builder.HasIndex(s => new { s.EpisodeId, s.Order }).IsUnique();
        builder.HasMany(s => s.Segments).WithOne().HasForeignKey(t => t.SectionId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class TranscriptSegmentConfiguration : IEntityTypeConfiguration<TranscriptSegment>
{
    public void Configure(EntityTypeBuilder<TranscriptSegment> builder) =>
        builder.HasIndex(t => new { t.SectionId, t.Order }).IsUnique();
}

public class AudioSegmentConfiguration : IEntityTypeConfiguration<AudioSegment>
{
    public void Configure(EntityTypeBuilder<AudioSegment> builder)
    {
        builder.Property(a => a.CacheKey).HasMaxLength(64);
        builder.Property(a => a.Provider).HasMaxLength(50);
        builder.Property(a => a.VoiceName).HasMaxLength(100);
        builder.Property(a => a.StorageKey).HasMaxLength(300);
        builder.HasIndex(a => new { a.EpisodeId, a.Order }).IsUnique();
        builder.HasIndex(a => a.CacheKey);
        builder.HasOne<EpisodeSection>().WithMany().HasForeignKey(a => a.SectionId).OnDelete(DeleteBehavior.NoAction);
    }
}

public class ListeningProgressConfiguration : IEntityTypeConfiguration<ListeningProgress>
{
    public void Configure(EntityTypeBuilder<ListeningProgress> builder)
    {
        builder.HasKey(p => new { p.UserId, p.EpisodeId });
        builder.HasOne<User>().WithMany().HasForeignKey(p => p.UserId);
        builder.HasOne<Episode>().WithMany().HasForeignKey(p => p.EpisodeId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class GenerationJobConfiguration : IEntityTypeConfiguration<GenerationJob>
{
    public void Configure(EntityTypeBuilder<GenerationJob> builder)
    {
        builder.HasOne<Episode>().WithMany().HasForeignKey(j => j.EpisodeId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(j => new { j.Status, j.LockedUntil });
        builder.HasIndex(j => j.EpisodeId).IsUnique();
    }
}

public class TtsUsageConfiguration : IEntityTypeConfiguration<TtsUsage>
{
    public void Configure(EntityTypeBuilder<TtsUsage> builder) =>
        builder.HasKey(u => new { u.ModelFamily, u.Month });
}
