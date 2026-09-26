using CompanyHero.Modules.Metering.Domain;
using CompanyHero.Platform.Data;
using CompanyHero.Platform.Modules;
using Microsoft.EntityFrameworkCore;

namespace CompanyHero.Modules.Metering.Infrastructure;

/// <summary>Schema <c>metering</c>: Ledger-Ereignisse, Abrechnungsperioden mit Salz, Tagesaggregate, Preisplanversionen, Rechnungsentwürfe (alle tenantbezogen, RLS).</summary>
public sealed class MeteringDbContext(DbContextOptions<MeteringDbContext> options) : ModuleDbContext(options)
{
    public DbSet<LedgerEvent> LedgerEvents => Set<LedgerEvent>();

    public DbSet<BillingPeriod> Periods => Set<BillingPeriod>();

    public DbSet<DailyAggregate> Aggregates => Set<DailyAggregate>();

    public DbSet<PricePlanVersion> PricePlans => Set<PricePlanVersion>();

    public DbSet<InvoiceDraft> InvoiceDrafts => Set<InvoiceDraft>();

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
            b.Property(e => e.Rated).HasColumnName("rated").HasDefaultValue(true);
            b.Property(e => e.ReversalOf).HasColumnName("reversal_of");
            b.Property(e => e.RecordedAt).HasColumnName("recorded_at");
            // Doppelzählung über den Idempotenzschlüssel ausgeschlossen (Metering 2.1, 3.3).
            b.HasIndex(e => new { e.TenantId, e.IdempotencyKey }).IsUnique();
            b.HasIndex(e => new { e.TenantId, e.Period, e.Metric });
        });

        modelBuilder.Entity<BillingPeriod>(b =>
        {
            b.ToTable("billing_period");
            b.HasKey(p => new { p.TenantId, p.Period });
            b.Property(p => p.TenantId).HasColumnName("tenant_id");
            b.Property(p => p.Period).HasColumnName("period").HasMaxLength(7).IsRequired();
            b.Property(p => p.OpenedAt).HasColumnName("opened_at");
            b.Property(p => p.SealedAt).HasColumnName("sealed_at");
            b.Property(p => p.ProtectedSalt).HasColumnName("protected_salt");
            b.Property(p => p.SaltDestroyedAt).HasColumnName("salt_destroyed_at");
            b.Ignore(p => p.IsSealed);
            b.Ignore(p => p.SaltDestroyed);
        });

        modelBuilder.Entity<DailyAggregate>(b =>
        {
            b.ToTable("daily_aggregate");
            b.HasKey(a => new { a.TenantId, a.Period, a.Day, a.Module, a.Metric });
            b.Property(a => a.TenantId).HasColumnName("tenant_id");
            b.Property(a => a.Period).HasColumnName("period").HasMaxLength(7).IsRequired();
            b.Property(a => a.Day).HasColumnName("day");
            b.Property(a => a.Module).HasColumnName("module").HasMaxLength(20).IsRequired();
            b.Property(a => a.Metric).HasColumnName("metric").HasMaxLength(60).IsRequired();
            b.Property(a => a.RatedQuantity).HasColumnName("rated_quantity").HasPrecision(18, 4);
            b.Property(a => a.UnratedQuantity).HasColumnName("unrated_quantity").HasPrecision(18, 4);
            b.Property(a => a.EventCount).HasColumnName("event_count");
            b.Property(a => a.UpdatedAt).HasColumnName("updated_at");
        });

        modelBuilder.Entity<PricePlanVersion>(b =>
        {
            b.ToTable("price_plan_version");
            b.HasKey(p => new { p.TenantId, p.Id });
            b.Property(p => p.TenantId).HasColumnName("tenant_id");
            b.Property(p => p.Id).HasColumnName("id");
            b.Property(p => p.ValidFromPeriod).HasColumnName("valid_from_period").HasMaxLength(7).IsRequired();
            b.Property(p => p.PlanJson).HasColumnName("plan_json").HasColumnType("jsonb").IsRequired();
            b.Property(p => p.CreatedAt).HasColumnName("created_at");
            b.Ignore(p => p.Plan);
            b.HasIndex(p => new { p.TenantId, p.ValidFromPeriod });
        });

        modelBuilder.Entity<InvoiceDraft>(b =>
        {
            b.ToTable("invoice_draft");
            b.HasKey(i => new { i.TenantId, i.Id });
            b.Property(i => i.TenantId).HasColumnName("tenant_id");
            b.Property(i => i.Id).HasColumnName("id");
            b.Property(i => i.Period).HasColumnName("period").HasMaxLength(7).IsRequired();
            b.Property(i => i.CreatedAt).HasColumnName("created_at");
            b.Property(i => i.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
            b.Property(i => i.TaxRate).HasColumnName("tax_rate").HasPrecision(9, 4);
            b.Property(i => i.Net).HasColumnName("net").HasPrecision(18, 2);
            b.Property(i => i.Tax).HasColumnName("tax").HasPrecision(18, 2);
            b.Property(i => i.Gross).HasColumnName("gross").HasPrecision(18, 2);
            b.Property(i => i.WouldHaveBeen).HasColumnName("would_have_been").HasPrecision(18, 2);
            b.Property(i => i.LinesJson).HasColumnName("lines_json").HasColumnType("jsonb").IsRequired();
            b.Property(i => i.PlanName).HasColumnName("plan_name").HasMaxLength(60).IsRequired();
            b.Ignore(i => i.Lines);
            b.HasIndex(i => new { i.TenantId, i.Period }).IsUnique();
        });
    }
}
