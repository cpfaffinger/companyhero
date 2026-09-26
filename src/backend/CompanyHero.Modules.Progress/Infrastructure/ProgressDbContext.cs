using CompanyHero.Modules.Progress.Domain;
using CompanyHero.Platform.Data;
using CompanyHero.Platform.Modules;
using Microsoft.EntityFrameworkCore;

namespace CompanyHero.Modules.Progress.Infrastructure;

/// <summary>Schema <c>progress</c>: Aktivitätsereignisse, persönlicher Stand, Abzeichen, Check-ins, Fachereignisse.</summary>
public sealed class ProgressDbContext(DbContextOptions<ProgressDbContext> options) : ModuleDbContext(options)
{
    public DbSet<ActivityEvent> ActivityEvents => Set<ActivityEvent>();

    public DbSet<PersonProgress> PersonProgress => Set<PersonProgress>();

    public DbSet<BadgeAward> Badges => Set<BadgeAward>();

    public DbSet<CheckIn> CheckIns => Set<CheckIn>();

    public DbSet<ProgressEvent> Events => Set<ProgressEvent>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        IdentifierConventions.Apply(configurationBuilder);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(ModuleSchemas.Progress);

        modelBuilder.Entity<ActivityEvent>(b =>
        {
            b.ToTable("activity_event");
            b.HasKey(e => new { e.TenantId, e.Id });
            b.Property(e => e.TenantId).HasColumnName("tenant_id");
            b.Property(e => e.Id).HasColumnName("id");
            b.Property(e => e.PersonId).HasColumnName("person_id");
            b.Property(e => e.Kind).HasColumnName("kind").HasMaxLength(60).IsRequired();
            b.Property(e => e.Source).HasColumnName("source").HasConversion<short>();
            b.Property(e => e.OccurredAt).HasColumnName("occurred_at");
            b.Property(e => e.Day).HasColumnName("day");
            b.Property(e => e.Points).HasColumnName("points");
            b.Property(e => e.ReversalOf).HasColumnName("reversal_of");
            b.Property(e => e.Reversed).HasColumnName("reversed");
            b.Ignore(e => e.IsReversal);
            b.HasIndex(e => new { e.TenantId, e.PersonId, e.OccurredAt });
            b.HasIndex(e => new { e.TenantId, e.PersonId, e.Day });
        });

        modelBuilder.Entity<PersonProgress>(b =>
        {
            b.ToTable("person_progress");
            b.HasKey(p => new { p.TenantId, p.PersonId });
            b.Property(p => p.TenantId).HasColumnName("tenant_id");
            b.Property(p => p.PersonId).HasColumnName("person_id");
            b.Property(p => p.PointsBalance).HasColumnName("points_balance");
            b.Property(p => p.Level).HasColumnName("level");
            b.Property(p => p.CurrentStreak).HasColumnName("current_streak");
            b.Property(p => p.LongestStreak).HasColumnName("longest_streak");
            b.Property(p => p.LastActiveDay).HasColumnName("last_active_day");
            b.Property(p => p.ProtectionUsedIn).HasColumnName("protection_used_in");
            b.Property(p => p.DailyGoal).HasColumnName("daily_goal");
        });

        modelBuilder.Entity<BadgeAward>(b =>
        {
            b.ToTable("badge_award");
            b.HasKey(a => new { a.TenantId, a.PersonId, a.BadgeKey });
            b.Property(a => a.TenantId).HasColumnName("tenant_id");
            b.Property(a => a.PersonId).HasColumnName("person_id");
            b.Property(a => a.BadgeKey).HasColumnName("badge_key").HasMaxLength(60);
            b.Property(a => a.AwardedAt).HasColumnName("awarded_at");
            b.HasIndex(a => new { a.TenantId, a.AwardedAt });
        });

        modelBuilder.Entity<CheckIn>(b =>
        {
            b.ToTable("check_in");
            b.HasKey(c => new { c.TenantId, c.PersonId, c.Day });
            b.Property(c => c.TenantId).HasColumnName("tenant_id");
            b.Property(c => c.PersonId).HasColumnName("person_id");
            b.Property(c => c.Day).HasColumnName("day");
            b.Property(c => c.Tiles).HasColumnName("tiles").HasConversion<short>();
            b.Property(c => c.RecordedAt).HasColumnName("recorded_at");
            b.Property(c => c.ActivityId).HasColumnName("activity_id");
        });

        modelBuilder.Entity<ProgressEvent>(b =>
        {
            b.ToTable("domain_event");
            b.HasKey(e => new { e.TenantId, e.Id });
            b.Property(e => e.TenantId).HasColumnName("tenant_id");
            b.Property(e => e.Id).HasColumnName("id");
            b.Property(e => e.Type).HasColumnName("type").HasMaxLength(100).IsRequired();
            b.Property(e => e.Version).HasColumnName("version");
            b.Property(e => e.OccurredAt).HasColumnName("occurred_at");
            b.Property(e => e.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
            b.Property(e => e.CausedBy).HasColumnName("caused_by");
            b.HasIndex(e => new { e.TenantId, e.Type, e.OccurredAt });
        });
    }
}
