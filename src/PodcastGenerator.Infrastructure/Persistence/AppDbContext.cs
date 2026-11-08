using Microsoft.EntityFrameworkCore;
using PodcastGenerator.Domain.Entities;

namespace PodcastGenerator.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<UserPreferences> UserPreferences => Set<UserPreferences>();
    public DbSet<Season> Seasons => Set<Season>();
    public DbSet<Episode> Episodes => Set<Episode>();
    public DbSet<EpisodeSection> EpisodeSections => Set<EpisodeSection>();
    public DbSet<TranscriptSegment> TranscriptSegments => Set<TranscriptSegment>();
    public DbSet<AudioSegment> AudioSegments => Set<AudioSegment>();
    public DbSet<ListeningProgress> ListeningProgress => Set<ListeningProgress>();
    public DbSet<GenerationJob> GenerationJobs => Set<GenerationJob>();
    public DbSet<TtsUsage> TtsUsage => Set<TtsUsage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Enums as readable strings: safer to reorder and easier to inspect in psql.
        configurationBuilder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(32);
    }
}
