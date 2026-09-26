using CompanyHero.Modules.Entitlements.Domain;
using CompanyHero.Platform.Data;
using CompanyHero.Platform.Modules;
using Microsoft.EntityFrameworkCore;

namespace CompanyHero.Modules.Entitlements.Infrastructure;

/// <summary>Schema <c>entitlements</c>: Entitlements je Tenant und Modul, append-only Historie, Grenzwerte (alle tenantbezogen, RLS).</summary>
public sealed class EntitlementsDbContext(DbContextOptions<EntitlementsDbContext> options) : ModuleDbContext(options)
{
    public DbSet<Entitlement> Entitlements => Set<Entitlement>();

    public DbSet<EntitlementHistoryEntry> History => Set<EntitlementHistoryEntry>();

    public DbSet<TenantLimit> Limits => Set<TenantLimit>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        IdentifierConventions.Apply(configurationBuilder);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(ModuleSchemas.Entitlements);

        modelBuilder.Entity<Entitlement>(b =>
        {
            b.ToTable("entitlement");
            b.HasKey(e => new { e.TenantId, e.Module });
            b.Property(e => e.TenantId).HasColumnName("tenant_id");
            b.Property(e => e.Module).HasColumnName("module").HasMaxLength(20).IsRequired();
            b.Property(e => e.State).HasColumnName("state").HasConversion<short>();
            b.Property(e => e.ActiveFrom).HasColumnName("active_from");
            b.Property(e => e.ActiveUntil).HasColumnName("active_until");
            b.Property(e => e.TrialUntil).HasColumnName("trial_until");
            b.Property(e => e.Source).HasColumnName("source").HasConversion<short>();
            b.Property(e => e.TrialUsed).HasColumnName("trial_used");
            b.Property(e => e.UpdatedAt).HasColumnName("updated_at");
        });

        modelBuilder.Entity<EntitlementHistoryEntry>(b =>
        {
            b.ToTable("entitlement_history");
            b.HasKey(h => new { h.TenantId, h.Id });
            b.Property(h => h.TenantId).HasColumnName("tenant_id");
            b.Property(h => h.Id).HasColumnName("id");
            b.Property(h => h.Module).HasColumnName("module").HasMaxLength(20).IsRequired();
            b.Property(h => h.OccurredAt).HasColumnName("occurred_at");
            b.Property(h => h.FromState).HasColumnName("from_state").HasConversion<short?>();
            b.Property(h => h.ToState).HasColumnName("to_state").HasConversion<short>();
            b.Property(h => h.ActorRoles).HasColumnName("actor_roles").HasMaxLength(200).IsRequired();
            b.Property(h => h.Source).HasColumnName("source").HasConversion<short>();
            b.Property(h => h.Reason).HasColumnName("reason").HasMaxLength(200).IsRequired();
            b.HasIndex(h => new { h.TenantId, h.Module, h.OccurredAt });
        });

        modelBuilder.Entity<TenantLimit>(b =>
        {
            b.ToTable("tenant_limit");
            b.HasKey(l => new { l.TenantId, l.Name });
            b.Property(l => l.TenantId).HasColumnName("tenant_id");
            b.Property(l => l.Name).HasColumnName("name").HasMaxLength(40).IsRequired();
            b.Property(l => l.Value).HasColumnName("value");
            b.Property(l => l.UpdatedAt).HasColumnName("updated_at");
        });
    }
}
