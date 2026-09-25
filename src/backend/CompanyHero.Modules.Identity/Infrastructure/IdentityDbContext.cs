using CompanyHero.Modules.Identity.Domain;
using CompanyHero.Platform.Data;
using CompanyHero.Platform.Modules;
using Microsoft.EntityFrameworkCore;

namespace CompanyHero.Modules.Identity.Infrastructure;

/// <summary>Schema <c>identity</c>: Personen; Anmeldewege, Sitzungen und Codes folgen in Stufe 4.</summary>
public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options)
{
    public DbSet<Person> Persons => Set<Person>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        IdentifierConventions.Apply(configurationBuilder);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(ModuleSchemas.Identity);

        modelBuilder.Entity<Person>(b =>
        {
            b.ToTable("person");
            b.HasKey(p => new { p.TenantId, p.Id });
            b.Property(p => p.TenantId).HasColumnName("tenant_id");
            b.Property(p => p.Id).HasColumnName("id");
            b.Property(p => p.DisplayName).HasColumnName("display_name").HasMaxLength(80).IsRequired();
            b.Property(p => p.CreatedAt).HasColumnName("created_at");
            // Anzeigenamen sind je Tenant eindeutig (Zugang 2.2), nie plattformweit.
            b.HasIndex(p => new { p.TenantId, p.DisplayName }).IsUnique();
        });
    }
}
