using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PodcastGenerator.Domain.Entities;

namespace PodcastGenerator.Infrastructure.Persistence.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.Property(u => u.DisplayName).HasMaxLength(100);
        builder.Property(u => u.Email).HasMaxLength(254);
        builder.Property(u => u.PasswordHash).HasMaxLength(500);
        builder.HasIndex(u => u.Email).IsUnique(); // PostgreSQL allows many NULLs in a unique index
        builder.HasOne(u => u.Preferences).WithOne().HasForeignKey<UserPreferences>(p => p.UserId);
        builder.HasData(new User
        {
            Id = SeedData.LocalUserId,
            DisplayName = "Local learner",
            CreatedAt = new DateTimeOffset(2026, 11, 8, 0, 0, 0, TimeSpan.Zero),
        });
    }
}

public class UserPreferencesConfiguration : IEntityTypeConfiguration<UserPreferences>
{
    public void Configure(EntityTypeBuilder<UserPreferences> builder)
    {
        builder.HasKey(p => p.UserId);
        builder.Property(p => p.NativeLanguage).HasMaxLength(8);
        builder.Property(p => p.TargetLanguage).HasMaxLength(8);
        builder.Property(p => p.VoiceName).HasMaxLength(100);
        builder.Property(p => p.PlaybackSpeed).HasPrecision(3, 2);
        builder.Property(p => p.SpeakingRate).HasPrecision(3, 2);
    }
}
