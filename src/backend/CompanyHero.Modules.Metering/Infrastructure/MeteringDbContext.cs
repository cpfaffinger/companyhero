using CompanyHero.Modules.Metering.Domain;
using CompanyHero.Platform.Data;
using CompanyHero.Platform.Modules;
using Microsoft.EntityFrameworkCore;

namespace CompanyHero.Modules.Metering.Infrastructure;

/// <summary>Schema <c>metering</c>: Ledger-Ereignisse; Perioden, Slots, Preisregeln und Rechnungsentwürfe folgen mit der Stufe Geld.</summary>
public sealed class MeteringDbContext(DbContextOptions<MeteringDbContext> options) : ModuleDbContext(options)
{
    public DbSet<LedgerEvent> LedgerEvents => Set<LedgerEvent>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        IdentifierConventions.Apply(configurationBuilder);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(ModuleSchemas.Metering);

        modelBuilder.Entity<LedgerEvent>(b =>
        {
            b.ToTable("ledger_event");
            b.HasKey(e => new { e.TenantId, e.Id });
            b.Property(e => e.TenantId).HasColumnName("tenant_id");
            b.Property(e => e.Id).HasColumnName("id");
            b.Property(e => e.Module).HasColumnName("module").HasMaxLength(20).IsRequired();
            b.Property(e => e.Metric).HasColumnName("metric").HasMaxLength(60).IsRequired();
            b.Property(e => e.SubjectRef).HasColumnName("subject_ref").HasMaxLength(200).IsRequired();
            b.Property(e => e.Quantity).HasColumnName("quantity").HasPrecision(18, 4);
            b.Property(e => e.Source).HasColumnName("source").HasConversion<short>();
            b.Property(e => e.OccurredAt).HasColumnName("occurred_at");
            b.Property(e => e.Period).HasColumnName("period").HasMaxLength(7).IsRequired();
            b.Property(e => e.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(200).IsRequired();
            b.Property(e => e.Late).HasColumnName("late");
            b.Property(e => e.ReversalOf).HasColumnName("reversal_of");
            b.Property(e => e.RecordedAt).HasColumnName("recorded_at");
            // Doppelzählung über den Idempotenzschlüssel ausgeschlossen (Metering 2.1, 3.3).
            b.HasIndex(e => new { e.TenantId, e.IdempotencyKey }).IsUnique();
            b.HasIndex(e => new { e.TenantId, e.Period, e.Metric });
        });
    }
}
