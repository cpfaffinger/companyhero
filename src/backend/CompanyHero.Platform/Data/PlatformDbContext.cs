using CompanyHero.Platform.Jobs;
using CompanyHero.Platform.Modules;
using Microsoft.EntityFrameworkCore;

namespace CompanyHero.Platform.Data;

/// <summary>
/// Plattformbereich ohne Tenant-RLS (Backend 5.3, 6.2): Release-Stand, Job-Queue und Zeitpläne. Das Modell dient den
/// Migrationen und den Gesundheitsprüfungen; die Queue selbst arbeitet mit SQL auf der Kontexttransaktion (Einreihung)
/// beziehungsweise auf eigenen Verbindungen (Beanspruchung), weil <c>FOR UPDATE SKIP LOCKED</c> kein EF-Ausdruck ist.
/// </summary>
public sealed class PlatformDbContext(DbContextOptions<PlatformDbContext> options) : DbContext(options)
{
    public DbSet<Release> Releases => Set<Release>();

    public DbSet<Job> Jobs => Set<Job>();

    public DbSet<JobSchedule> JobSchedules => Set<JobSchedule>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(ModuleSchemas.Platform);

        modelBuilder.Entity<Release>(b =>
        {
            b.ToTable("release");
            b.HasKey(r => r.Id);
            b.Property(r => r.Id).HasColumnName("id");
            b.Property(r => r.Version).HasColumnName("version").HasMaxLength(200).IsRequired();
            b.Property(r => r.ImageDigest).HasColumnName("image_digest").HasMaxLength(200);
            b.Property(r => r.AppliedAt).HasColumnName("applied_at").IsRequired();
        });

        modelBuilder.Entity<Job>(b =>
        {
            b.ToTable("job");
            b.HasKey(j => j.Id);
            b.Property(j => j.Id).HasColumnName("id");
            b.Property(j => j.TenantId).HasColumnName("tenant_id");
            b.Property(j => j.JobType).HasColumnName("job_type").HasMaxLength(100).IsRequired();
            b.Property(j => j.Reference).HasColumnName("reference").HasMaxLength(200).IsRequired();
            b.Property(j => j.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(200).IsRequired();
            b.Property(j => j.Status).HasColumnName("status").HasConversion<short>();
            b.Property(j => j.RunAt).HasColumnName("run_at");
            b.Property(j => j.LeaseUntil).HasColumnName("lease_until");
            b.Property(j => j.Attempts).HasColumnName("attempts");
            b.Property(j => j.MaxAttempts).HasColumnName("max_attempts");
            b.Property(j => j.WorkerId).HasColumnName("worker_id").HasMaxLength(100);
            b.Property(j => j.LastError).HasColumnName("last_error").HasMaxLength(500);
            b.Property(j => j.CreatedAt).HasColumnName("created_at");
            b.Property(j => j.CompletedAt).HasColumnName("completed_at");
            b.Property(j => j.ReplayedAt).HasColumnName("replayed_at");
            // Ein Namensraum je Tenant und Jobtyp; Plattformjobs (tenant_id NULL) bilden einen eigenen Namensraum.
            b.HasIndex(j => new { j.JobType, j.IdempotencyKey, j.TenantId }).IsUnique().AreNullsDistinct(false);
            b.HasIndex(j => new { j.Status, j.RunAt });
            b.HasIndex(j => new { j.TenantId, j.Status, j.CompletedAt });
        });

        modelBuilder.Entity<JobSchedule>(b =>
        {
            b.ToTable("job_schedule");
            b.HasKey(s => s.Name);
            b.Property(s => s.Name).HasColumnName("name").HasMaxLength(100);
            b.Property(s => s.IntervalSeconds).HasColumnName("interval_seconds");
            b.Property(s => s.NextRunAt).HasColumnName("next_run_at");
            b.Property(s => s.LastRunAt).HasColumnName("last_run_at");
            b.Property(s => s.LastDurationMs).HasColumnName("last_duration_ms");
            b.Property(s => s.LastError).HasColumnName("last_error").HasMaxLength(500);
        });
    }
}

/// <summary>Ein erfolgreich abgeschlossener Migrationslauf (Betrieb 4: Migrations-Container vor dem Anwendungsstart).</summary>
public sealed class Release
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required string Version { get; init; }

    public string? ImageDigest { get; init; }

    public DateTimeOffset AppliedAt { get; init; }
}

/// <summary>Zeile der logischen Queue (Backend 6.2): nur Routinginformationen, keine fachlichen Nutzdaten, kein Personenbezug.</summary>
public sealed class Job
{
    public Guid Id { get; init; }

    public Guid? TenantId { get; init; }

    public required string JobType { get; init; }

    public required string Reference { get; init; }

    public required string IdempotencyKey { get; init; }

    public JobStatus Status { get; init; }

    public DateTimeOffset RunAt { get; init; }

    public DateTimeOffset? LeaseUntil { get; init; }

    public int Attempts { get; init; }

    public int MaxAttempts { get; init; }

    public string? WorkerId { get; init; }

    public string? LastError { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    public DateTimeOffset? ReplayedAt { get; init; }
}

/// <summary>Zeitgesteuerte Aufgabe mit letzter und nächster Ausführung (Backend 6.2); die Zeile ist zugleich die Sperre.</summary>
public sealed class JobSchedule
{
    public required string Name { get; init; }

    public int IntervalSeconds { get; init; }

    public DateTimeOffset NextRunAt { get; init; }

    public DateTimeOffset? LastRunAt { get; init; }

    public int? LastDurationMs { get; init; }

    public string? LastError { get; init; }
}
