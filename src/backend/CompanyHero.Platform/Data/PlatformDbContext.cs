using CompanyHero.Platform.Modules;
using Microsoft.EntityFrameworkCore;

namespace CompanyHero.Platform.Data;

/// <summary>
/// Plattformbereich ohne Tenant-RLS (Backend 5.3): Release-Stand und später Job-Queue und Zeitpläne.
/// </summary>
public sealed class PlatformDbContext(DbContextOptions<PlatformDbContext> options) : DbContext(options)
{
    public DbSet<Release> Releases => Set<Release>();

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
