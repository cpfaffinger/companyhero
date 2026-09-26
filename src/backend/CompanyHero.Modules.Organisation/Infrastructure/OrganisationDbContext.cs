using CompanyHero.Modules.Organisation.Domain;
using CompanyHero.Platform.Data;
using CompanyHero.Platform.Modules;
using Microsoft.EntityFrameworkCore;

namespace CompanyHero.Modules.Organisation.Infrastructure;

/// <summary>Schema <c>organisation</c>: Organisationen (Plattformdaten), Mitgliedschaften, Rollen, Dimensionen, Gruppen, Sollstärken und Gruppenzugehörigkeiten (tenantbezogen, RLS).</summary>
public sealed class OrganisationDbContext(DbContextOptions<OrganisationDbContext> options) : ModuleDbContext(options)
{
    public DbSet<Domain.Organisation> Organisations => Set<Domain.Organisation>();

    public DbSet<Membership> Memberships => Set<Membership>();

    public DbSet<RoleAssignment> RoleAssignments => Set<RoleAssignment>();

    public DbSet<GroupDimension> Dimensions => Set<GroupDimension>();

    public DbSet<MemberGroup> Groups => Set<MemberGroup>();

    public DbSet<HeadcountEntry> Headcounts => Set<HeadcountEntry>();

    public DbSet<GroupMembership> GroupMemberships => Set<GroupMembership>();

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
            b.Property(o => o.TimeZoneId).HasColumnName("time_zone").HasMaxLength(64).IsRequired().HasDefaultValue(Platform.Tenancy.TenantTimeZone.DefaultId);
            b.Property(o => o.SuspendedAt).HasColumnName("suspended_at");
            b.Property(o => o.SuspensionReason).HasColumnName("suspension_reason").HasMaxLength(200);
            b.Property(o => o.TerminatedAt).HasColumnName("terminated_at");
            b.Property(o => o.TerminationEffectiveAt).HasColumnName("termination_effective_at");
            b.Property(o => o.DeletedAt).HasColumnName("deleted_at");
            b.Ignore(o => o.ReadOnlyUntil);
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

        modelBuilder.Entity<GroupDimension>(b =>
        {
            b.ToTable("group_dimension");
            b.HasKey(d => new { d.TenantId, d.Id });
            b.Property(d => d.TenantId).HasColumnName("tenant_id");
            b.Property(d => d.Id).HasColumnName("id");
            b.Property(d => d.Name).HasColumnName("name").HasMaxLength(80).IsRequired();
            b.Property(d => d.Position).HasColumnName("position");
            b.Property(d => d.Active).HasColumnName("active");
            b.Property(d => d.CreatedAt).HasColumnName("created_at");
            b.HasIndex(d => new { d.TenantId, d.Position }).IsUnique();
        });

        modelBuilder.Entity<MemberGroup>(b =>
        {
            b.ToTable("member_group");
            b.HasKey(g => new { g.TenantId, g.Id });
            b.Property(g => g.TenantId).HasColumnName("tenant_id");
            b.Property(g => g.Id).HasColumnName("id");
            b.Property(g => g.DimensionId).HasColumnName("dimension_id");
            b.Property(g => g.Name).HasColumnName("name").HasMaxLength(80).IsRequired();
            b.Property(g => g.CreatedAt).HasColumnName("created_at");
            b.Property(g => g.ArchivedAt).HasColumnName("archived_at");
            b.Ignore(g => g.IsSelectable);
            b.HasOne<GroupDimension>().WithMany().HasForeignKey(g => new { g.TenantId, g.DimensionId }).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(g => new { g.TenantId, g.DimensionId });
        });

        modelBuilder.Entity<HeadcountEntry>(b =>
        {
            b.ToTable("headcount");
            b.HasKey(h => new { h.TenantId, h.Id });
            b.Property(h => h.TenantId).HasColumnName("tenant_id");
            b.Property(h => h.Id).HasColumnName("id");
            b.Property(h => h.GroupId).HasColumnName("group_id");
            b.Property(h => h.EffectiveFrom).HasColumnName("effective_from");
            b.Property(h => h.Count).HasColumnName("count");
            b.Property(h => h.RecordedAt).HasColumnName("recorded_at");
            b.HasIndex(h => new { h.TenantId, h.GroupId, h.EffectiveFrom });
        });

        modelBuilder.Entity<GroupMembership>(b =>
        {
            b.ToTable("group_membership");
            b.HasKey(g => new { g.TenantId, g.PersonId, g.DimensionId });
            b.Property(g => g.TenantId).HasColumnName("tenant_id");
            b.Property(g => g.PersonId).HasColumnName("person_id");
            b.Property(g => g.DimensionId).HasColumnName("dimension_id");
            b.Property(g => g.GroupId).HasColumnName("group_id");
            b.Property(g => g.Since).HasColumnName("since");
            b.HasOne<MemberGroup>().WithMany().HasForeignKey(g => new { g.TenantId, g.GroupId }).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(g => new { g.TenantId, g.GroupId });
        });
    }
}
