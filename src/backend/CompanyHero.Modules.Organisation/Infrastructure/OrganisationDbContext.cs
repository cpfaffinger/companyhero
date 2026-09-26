using CompanyHero.Modules.Organisation.Domain;
using CompanyHero.Platform.Data;
using CompanyHero.Platform.Modules;
using Microsoft.EntityFrameworkCore;

namespace CompanyHero.Modules.Organisation.Infrastructure;

/// <summary>Schema <c>organisation</c>: Organisationen (Plattformdaten), Mitgliedschaften und Rollenzuweisungen (tenantbezogen, RLS).</summary>
public sealed class OrganisationDbContext(DbContextOptions<OrganisationDbContext> options) : ModuleDbContext(options)
{
    public DbSet<Domain.Organisation> Organisations => Set<Domain.Organisation>();

    public DbSet<Membership> Memberships => Set<Membership>();

    public DbSet<RoleAssignment> RoleAssignments => Set<RoleAssignment>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        IdentifierConventions.Apply(configurationBuilder);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(ModuleSchemas.Organisation);

        modelBuilder.Entity<Domain.Organisation>(b =>
        {
            b.ToTable("organisation");
            b.HasKey(o => o.Id);
            b.Property(o => o.Id).HasColumnName("id");
            b.Property(o => o.Type).HasColumnName("type").HasConversion<short>();
            b.Property(o => o.ParentId).HasColumnName("parent_id");
            b.Property(o => o.DisplayName).HasColumnName("display_name").HasMaxLength(200).IsRequired();
            b.Property(o => o.State).HasColumnName("state").HasConversion<short>();
            b.Property(o => o.CreatedAt).HasColumnName("created_at");
            b.HasOne<Domain.Organisation>().WithMany().HasForeignKey(o => o.ParentId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(o => o.ParentId);
        });

        modelBuilder.Entity<Membership>(b =>
        {
            b.ToTable("membership");
            b.HasKey(m => new { m.TenantId, m.PersonId });
            b.Property(m => m.TenantId).HasColumnName("tenant_id");
            b.Property(m => m.PersonId).HasColumnName("person_id");
            b.Property(m => m.State).HasColumnName("state").HasConversion<short>();
            b.Property(m => m.JoinedAt).HasColumnName("joined_at");
            b.Property(m => m.LeftAt).HasColumnName("left_at");
            // Fremdschlüssel tenant_id -> organisation.id liegt in der Migration (EF kann TenantId nicht auf Guid binden).
            b.HasMany(m => m.Roles).WithOne().HasForeignKey(r => new { r.TenantId, r.PersonId }).OnDelete(DeleteBehavior.Cascade);
            b.Navigation(m => m.Roles).HasField("_roles").UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<RoleAssignment>(b =>
        {
            b.ToTable("role_assignment");
            b.HasKey(r => new { r.TenantId, r.Id });
            b.Property(r => r.TenantId).HasColumnName("tenant_id");
            b.Property(r => r.Id).HasColumnName("id");
            b.Property(r => r.PersonId).HasColumnName("person_id");
            b.Property(r => r.Role).HasColumnName("role").HasMaxLength(40).IsRequired();
            b.Property(r => r.AssignedAt).HasColumnName("assigned_at");
            b.HasIndex(r => new { r.TenantId, r.PersonId, r.Role }).IsUnique();
        });
    }
}
