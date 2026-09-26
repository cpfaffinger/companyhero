using CompanyHero.Modules.Branding.Domain.Theme;
using CompanyHero.Platform.Data;
using CompanyHero.Platform.Modules;
using Microsoft.EntityFrameworkCore;

namespace CompanyHero.Modules.Branding.Infrastructure;

/// <summary>Schema <c>branding</c>: veröffentlichte Theme-Versionen je Tenant (RLS).</summary>
public sealed class BrandingDbContext(DbContextOptions<BrandingDbContext> options) : ModuleDbContext(options)
{
    public DbSet<TenantTheme> Themes => Set<TenantTheme>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        IdentifierConventions.Apply(configurationBuilder);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(ModuleSchemas.Branding);

        modelBuilder.Entity<TenantTheme>(b =>
        {
            b.ToTable("tenant_theme");
            b.HasKey(t => new { t.TenantId, t.Version });
            b.Property(t => t.TenantId).HasColumnName("tenant_id");
            b.Property(t => t.Version).HasColumnName("version");
            b.Property(t => t.Document).HasColumnName("document").HasColumnType("jsonb").IsRequired();
            b.Property(t => t.Tokens).HasColumnName("tokens").HasColumnType("jsonb").IsRequired();
            b.Property(t => t.PublishedAt).HasColumnName("published_at");
            b.Property(t => t.PublishedBy).HasColumnName("published_by");
        });
    }
}
