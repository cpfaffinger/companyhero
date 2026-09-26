using CompanyHero.Modules.Notifications.Application;
using CompanyHero.Modules.Notifications.Domain;
using CompanyHero.Platform.Data;
using CompanyHero.Platform.Modules;
using Microsoft.EntityFrameworkCore;

namespace CompanyHero.Modules.Notifications.Infrastructure;

/// <summary>Schema <c>notifications</c>: Einträge, Push-Abonnements, Einstellungen je Person und Tenant, Zustellungen, Aushänge.</summary>
public sealed class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options) : ModuleDbContext(options)
{
    public DbSet<NotificationEntry> Entries => Set<NotificationEntry>();

    public DbSet<PushSubscription> Subscriptions => Set<PushSubscription>();

    public DbSet<PersonNotificationSettings> PersonSettings => Set<PersonNotificationSettings>();

    public DbSet<TenantNotificationSettings> TenantSettings => Set<TenantNotificationSettings>();

    public DbSet<Delivery> Deliveries => Set<Delivery>();

    public DbSet<Aushang> Aushaenge => Set<Aushang>();

    public DbSet<ChallengeSnapshot> ChallengeSnapshots => Set<ChallengeSnapshot>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        IdentifierConventions.Apply(configurationBuilder);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(ModuleSchemas.Notifications);

        modelBuilder.Entity<NotificationEntry>(b =>
        {
            b.ToTable("notification_entry");
            b.HasKey(e => new { e.TenantId, e.Id });
            b.Property(e => e.TenantId).HasColumnName("tenant_id");
            b.Property(e => e.Id).HasColumnName("id");
            b.Property(e => e.PersonId).HasColumnName("person_id");
            b.Property(e => e.Category).HasColumnName("category").HasConversion<short>();
            b.Property(e => e.EventKey).HasColumnName("event_key").HasMaxLength(120).IsRequired();
            b.Property(e => e.TextKey).HasColumnName("text_key").HasMaxLength(80).IsRequired();
            b.Property(e => e.ParamsJson).HasColumnName("params").HasColumnType("jsonb").IsRequired();
            b.Property(e => e.Target).HasColumnName("target").HasMaxLength(200).IsRequired();
            b.Property(e => e.OccurredAt).HasColumnName("occurred_at");
            b.Property(e => e.ReadAt).HasColumnName("read_at");
            b.HasIndex(e => new { e.TenantId, e.PersonId, e.EventKey }).IsUnique();
            b.HasIndex(e => new { e.TenantId, e.PersonId, e.OccurredAt });
        });

        modelBuilder.Entity<PushSubscription>(b =>
        {
            b.ToTable("push_subscription");
            b.HasKey(s => new { s.TenantId, s.Id });
            b.Property(s => s.TenantId).HasColumnName("tenant_id");
            b.Property(s => s.Id).HasColumnName("id");
            b.Property(s => s.PersonId).HasColumnName("person_id");
            b.Property(s => s.Endpoint).HasColumnName("endpoint").HasMaxLength(2000).IsRequired();
            b.Property(s => s.P256dh).HasColumnName("p256dh").HasMaxLength(200).IsRequired();
            b.Property(s => s.Auth).HasColumnName("auth").HasMaxLength(100).IsRequired();
            b.Property(s => s.DeviceLabel).HasColumnName("device_label").HasMaxLength(60).IsRequired();
            b.Property(s => s.CreatedAt).HasColumnName("created_at");
            b.Property(s => s.LastSuccessAt).HasColumnName("last_success_at");
            b.Property(s => s.FailureCount).HasColumnName("failure_count");
            b.Property(s => s.PausedAt).HasColumnName("paused_at");
            b.Ignore(s => s.Active);
            b.HasIndex(s => new { s.TenantId, s.PersonId });
        });

        modelBuilder.Entity<PersonNotificationSettings>(b =>
        {
            b.ToTable("person_settings");
            b.HasKey(s => new { s.TenantId, s.PersonId });
            b.Property(s => s.TenantId).HasColumnName("tenant_id");
            b.Property(s => s.PersonId).HasColumnName("person_id");
            b.Property(s => s.PushChallenge).HasColumnName("push_challenge");
            b.Property(s => s.PushProgress).HasColumnName("push_progress");
            b.Property(s => s.EmailChallenge).HasColumnName("email_challenge");
            b.Property(s => s.EmailProgress).HasColumnName("email_progress");
            b.Property(s => s.QuietStart).HasColumnName("quiet_start");
            b.Property(s => s.QuietEnd).HasColumnName("quiet_end");
            b.Property(s => s.EmailFailureCount).HasColumnName("email_failure_count");
            b.Property(s => s.EmailPausedAt).HasColumnName("email_paused_at");
            b.Property(s => s.UpdatedAt).HasColumnName("updated_at");
        });

        modelBuilder.Entity<TenantNotificationSettings>(b =>
        {
            b.ToTable("tenant_settings");
            b.HasKey(s => s.TenantId);
            b.Property(s => s.TenantId).HasColumnName("tenant_id");
            b.Property(s => s.PushEnabled).HasColumnName("push_enabled");
            b.Property(s => s.EmailEnabled).HasColumnName("email_enabled");
            b.Property(s => s.AushangEnabled).HasColumnName("aushang_enabled");
            b.Property(s => s.QuietStart).HasColumnName("quiet_start");
            b.Property(s => s.QuietEnd).HasColumnName("quiet_end");
            b.Property(s => s.UpdatedAt).HasColumnName("updated_at");
        });

        modelBuilder.Entity<Delivery>(b =>
        {
            b.ToTable("delivery");
            b.HasKey(d => new { d.TenantId, d.Id });
            b.Property(d => d.TenantId).HasColumnName("tenant_id");
            b.Property(d => d.Id).HasColumnName("id");
            b.Property(d => d.EntryId).HasColumnName("entry_id");
            b.Property(d => d.PersonId).HasColumnName("person_id");
            b.Property(d => d.Channel).HasColumnName("channel").HasConversion<short>();
            b.Property(d => d.DedupeKey).HasColumnName("dedupe_key").HasMaxLength(200).IsRequired();
            b.Property(d => d.Status).HasColumnName("status").HasConversion<short>();
            b.Property(d => d.CreatedAt).HasColumnName("created_at");
            b.Property(d => d.Day).HasColumnName("day");
            b.Property(d => d.HeldUntil).HasColumnName("held_until");
            b.Property(d => d.FinishedAt).HasColumnName("finished_at");
            b.Property(d => d.Error).HasColumnName("error").HasMaxLength(120);
            b.HasIndex(d => new { d.TenantId, d.DedupeKey }).IsUnique();
            b.HasIndex(d => new { d.TenantId, d.PersonId, d.Channel, d.Day });
        });

        modelBuilder.Entity<ChallengeSnapshot>(b =>
        {
            b.ToTable("challenge_snapshot");
            b.HasKey(s => new { s.TenantId, s.ChallengeId });
            b.Property(s => s.TenantId).HasColumnName("tenant_id");
            b.Property(s => s.ChallengeId).HasColumnName("challenge_id");
            b.Property(s => s.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
            b.Property(s => s.Milestone).HasColumnName("milestone");
            b.Property(s => s.Ended).HasColumnName("ended");
            b.Property(s => s.UpdatedAt).HasColumnName("updated_at");
            b.Ignore(s => s.NextMilestone);
        });

        modelBuilder.Entity<Aushang>(b =>
        {
            b.ToTable("aushang");
            b.HasKey(a => new { a.TenantId, a.Id });
            b.Property(a => a.TenantId).HasColumnName("tenant_id");
            b.Property(a => a.Id).HasColumnName("id");
            b.Property(a => a.Week).HasColumnName("week").HasMaxLength(10).IsRequired();
            b.Property(a => a.GeneratedAt).HasColumnName("generated_at");
            b.Property(a => a.Pdf).HasColumnName("pdf").IsRequired();
            b.HasIndex(a => new { a.TenantId, a.GeneratedAt });
        });
    }
}
