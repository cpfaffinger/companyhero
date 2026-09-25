using CompanyHero.Modules.Challenges.Domain;
using CompanyHero.Platform.Data;
using CompanyHero.Platform.Modules;
using Microsoft.EntityFrameworkCore;

namespace CompanyHero.Modules.Challenges.Infrastructure;

/// <summary>Schema <c>challenges</c>: Challenges, Beiträge, Idempotenznachweise, Fachereignisse, Kollektivstände.</summary>
public sealed class ChallengesDbContext(DbContextOptions<ChallengesDbContext> options) : ModuleDbContext(options)
{
    public DbSet<Challenge> Challenges => Set<Challenge>();

    public DbSet<Contribution> Contributions => Set<Contribution>();

    public DbSet<ContributionKey> ContributionKeys => Set<ContributionKey>();

    public DbSet<ChallengeEvent> Events => Set<ChallengeEvent>();

    public DbSet<CollectiveState> CollectiveStates => Set<CollectiveState>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        IdentifierConventions.Apply(configurationBuilder);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(ModuleSchemas.Challenges);

        modelBuilder.Entity<Challenge>(b =>
        {
            b.ToTable("challenge");
            b.HasKey(c => new { c.TenantId, c.Id });
            b.Property(c => c.TenantId).HasColumnName("tenant_id");
            b.Property(c => c.Id).HasColumnName("id");
            b.Property(c => c.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
            b.Property(c => c.Metric).HasColumnName("metric").HasConversion<short>();
            b.Property(c => c.State).HasColumnName("state").HasConversion<short>();
            b.Property(c => c.StartsAt).HasColumnName("starts_at");
            b.Property(c => c.EndsAt).HasColumnName("ends_at");
            b.Property(c => c.CreatedAt).HasColumnName("created_at");
        });

        modelBuilder.Entity<Contribution>(b =>
        {
            b.ToTable("contribution");
            b.HasKey(c => new { c.TenantId, c.Id });
            b.Property(c => c.TenantId).HasColumnName("tenant_id");
            b.Property(c => c.Id).HasColumnName("id");
            b.Property(c => c.ChallengeId).HasColumnName("challenge_id");
            b.Property(c => c.PersonId).HasColumnName("person_id");
            b.Property(c => c.Value).HasColumnName("value").HasPrecision(12, 4);
            b.Property(c => c.RecordedAt).HasColumnName("recorded_at");
            b.Property(c => c.ReceivedAt).HasColumnName("received_at");
            b.Property(c => c.Channel).HasColumnName("channel").HasConversion<short>();
            // Zusammengesetzter Fremdschlüssel (tenant_id, challenge_id) (Backend 5.1 Nr. 3).
            b.HasOne<Challenge>().WithMany().HasForeignKey(c => new { c.TenantId, c.ChallengeId }).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(c => new { c.TenantId, c.ChallengeId, c.PersonId, c.RecordedAt });
        });

        modelBuilder.Entity<ContributionKey>(b =>
        {
            b.ToTable("contribution_key");
            b.HasKey(k => new { k.TenantId, k.PersonId, k.Key });
            b.Property(k => k.TenantId).HasColumnName("tenant_id");
            b.Property(k => k.PersonId).HasColumnName("person_id");
            b.Property(k => k.Key).HasColumnName("key").HasMaxLength(64);
            b.Property(k => k.Regime).HasColumnName("regime").HasConversion<short>();
            b.Property(k => k.State).HasColumnName("state").HasConversion<short>();
            b.Property(k => k.ContentHash).HasColumnName("content_hash").HasMaxLength(64);
            b.Property(k => k.ContributionId).HasColumnName("contribution_id");
            b.Property(k => k.ReservedAt).HasColumnName("reserved_at");
            b.Property(k => k.CommittedAt).HasColumnName("committed_at");
        });

        modelBuilder.Entity<ChallengeEvent>(b =>
        {
            b.ToTable("domain_event");
            b.HasKey(e => new { e.TenantId, e.Id });
            b.Property(e => e.TenantId).HasColumnName("tenant_id");
            b.Property(e => e.Id).HasColumnName("id");
            b.Property(e => e.Type).HasColumnName("type").HasMaxLength(100).IsRequired();
            b.Property(e => e.Version).HasColumnName("version");
            b.Property(e => e.OccurredAt).HasColumnName("occurred_at");
            b.Property(e => e.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
            b.Property(e => e.CausedBy).HasColumnName("caused_by");
            b.HasIndex(e => new { e.TenantId, e.Type, e.OccurredAt });
        });

        modelBuilder.Entity<CollectiveState>(b =>
        {
            b.ToTable("collective_state");
            b.HasKey(s => new { s.TenantId, s.ChallengeId });
            b.Property(s => s.TenantId).HasColumnName("tenant_id");
            b.Property(s => s.ChallengeId).HasColumnName("challenge_id");
            b.Property(s => s.Total).HasColumnName("total").HasPrecision(14, 4);
            b.Property(s => s.ContributionCount).HasColumnName("contribution_count");
            b.Property(s => s.ContributorCount).HasColumnName("contributor_count");
            b.Property(s => s.UpdatedAt).HasColumnName("updated_at");
            b.HasOne<Challenge>().WithOne().HasForeignKey<CollectiveState>(s => new { s.TenantId, s.ChallengeId }).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
