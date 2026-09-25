using CompanyHero.Modules.Privacy.Domain;
using CompanyHero.Platform.Data;
using CompanyHero.Platform.Modules;
using Microsoft.EntityFrameworkCore;

namespace CompanyHero.Modules.Privacy.Infrastructure;

/// <summary>Schema <c>privacy</c>: Sichtbarkeitseinstellungen; Zustimmungs- und Prüfprotokoll folgen mit dem Fachpfad.</summary>
public sealed class PrivacyDbContext(DbContextOptions<PrivacyDbContext> options) : ModuleDbContext(options)
{
    public DbSet<VisibilitySetting> VisibilitySettings => Set<VisibilitySetting>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        IdentifierConventions.Apply(configurationBuilder);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(ModuleSchemas.Privacy);

        modelBuilder.Entity<VisibilitySetting>(b =>
        {
            b.ToTable("visibility_setting");
            b.HasKey(v => new { v.TenantId, v.PersonId });
            b.Property(v => v.TenantId).HasColumnName("tenant_id");
            b.Property(v => v.PersonId).HasColumnName("person_id");
            b.Property(v => v.Level).HasColumnName("level").HasConversion<short>();
            b.Property(v => v.ChosenAt).HasColumnName("chosen_at");
        });
    }
}
