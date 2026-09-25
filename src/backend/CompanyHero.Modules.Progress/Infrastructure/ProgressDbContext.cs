using CompanyHero.Modules.Progress.Domain;
using CompanyHero.Platform.Data;
using CompanyHero.Platform.Modules;
using Microsoft.EntityFrameworkCore;

namespace CompanyHero.Modules.Progress.Infrastructure;

/// <summary>Schema <c>progress</c>: Aktivitätsereignisse; Punkte, Serien, Abzeichen und Stufen folgen mit dem Fachpfad.</summary>
public sealed class ProgressDbContext(DbContextOptions<ProgressDbContext> options) : DbContext(options)
{
    public DbSet<ActivityEvent> ActivityEvents => Set<ActivityEvent>();

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
            b.HasIndex(e => new { e.TenantId, e.PersonId, e.OccurredAt });
        });
    }
}
