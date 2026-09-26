using CompanyHero.Modules.Privacy.Domain;
using CompanyHero.Platform.Data;
using CompanyHero.Platform.Modules;
using Microsoft.EntityFrameworkCore;

namespace CompanyHero.Modules.Privacy.Infrastructure;

/// <summary>Schema <c>privacy</c>: Sichtbarkeitseinstellungen, Prüfprotokoll und Sicherheitsprotokoll; das Zustimmungsprotokoll folgt mit dem Fachpfad.</summary>
public sealed class PrivacyDbContext(DbContextOptions<PrivacyDbContext> options) : ModuleDbContext(options)
{
    public DbSet<VisibilitySetting> VisibilitySettings => Set<VisibilitySetting>();

    public DbSet<AuditRecord> AuditRecords => Set<AuditRecord>();

    public DbSet<SecurityRecord> SecurityRecords => Set<SecurityRecord>();

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

        modelBuilder.Entity<AuditRecord>(b =>
        {
            b.ToTable("audit_entry");
            b.HasKey(a => new { a.TenantId, a.Id });
            b.Property(a => a.TenantId).HasColumnName("tenant_id");
            b.Property(a => a.Id).HasColumnName("id");
            b.Property(a => a.OccurredAt).HasColumnName("occurred_at");
            b.Property(a => a.Actor).HasColumnName("actor_person_id");
            b.Property(a => a.ActorRoles).HasColumnName("actor_roles").HasMaxLength(200).IsRequired();
            b.Property(a => a.Action).HasColumnName("action").HasMaxLength(100).IsRequired();
            b.Property(a => a.SubjectRef).HasColumnName("subject_ref").HasMaxLength(200);
            b.Property(a => a.Detail).HasColumnName("detail").HasMaxLength(500);
            b.HasIndex(a => new { a.TenantId, a.OccurredAt });
        });

        modelBuilder.Entity<SecurityRecord>(b =>
        {
            b.ToTable("security_event");
            b.HasKey(a => new { a.TenantId, a.Id });
            b.Property(a => a.TenantId).HasColumnName("tenant_id");
            b.Property(a => a.Id).HasColumnName("id");
            b.Property(a => a.OccurredAt).HasColumnName("occurred_at");
            b.Property(a => a.EventType).HasColumnName("event_type").HasMaxLength(100).IsRequired();
            b.Property(a => a.Success).HasColumnName("success");
            b.Property(a => a.Pseudonym).HasColumnName("pseudonym").HasMaxLength(200);
            b.Property(a => a.Detail).HasColumnName("detail").HasMaxLength(500);
            b.HasIndex(a => new { a.TenantId, a.OccurredAt });
        });
    }
}
