using CompanyHero.Modules.Feed.Domain;
using CompanyHero.Platform.Data;
using CompanyHero.Platform.Modules;
using Microsoft.EntityFrameworkCore;

namespace CompanyHero.Modules.Feed.Infrastructure;

/// <summary>Schema <c>feed</c>: Feed-Einträge aus Fachereignissen und Mitglieder-Beiträgen.</summary>
public sealed class FeedDbContext(DbContextOptions<FeedDbContext> options) : ModuleDbContext(options)
{
    public DbSet<FeedEntry> Entries => Set<FeedEntry>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        IdentifierConventions.Apply(configurationBuilder);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(ModuleSchemas.Feed);

        modelBuilder.Entity<FeedEntry>(b =>
        {
            b.ToTable("feed_entry");
            b.HasKey(e => new { e.TenantId, e.Id });
            b.Property(e => e.TenantId).HasColumnName("tenant_id");
            b.Property(e => e.Id).HasColumnName("id");
            b.Property(e => e.Kind).HasColumnName("kind").HasConversion<short>();
            b.Property(e => e.EventKey).HasColumnName("event_key").HasMaxLength(120).IsRequired();
            b.Property(e => e.TextKey).HasColumnName("text_key").HasMaxLength(80).IsRequired();
            b.Property(e => e.ParamsJson).HasColumnName("params").HasColumnType("jsonb").IsRequired();
            b.Property(e => e.SubjectPersonId).HasColumnName("subject_person_id");
            b.Property(e => e.Scope).HasColumnName("scope").HasConversion<short>();
            b.PrimitiveCollection(e => e.GroupIds).HasColumnName("group_ids");
            b.Property(e => e.ReferenceKind).HasColumnName("reference_kind").HasMaxLength(40);
            b.Property(e => e.ReferenceId).HasColumnName("reference_id");
            b.Property(e => e.Body).HasColumnName("body").HasMaxLength(FeedRules.MaxPostLength);
            b.Property(e => e.OccurredAt).HasColumnName("occurred_at");
            b.Property(e => e.Day).HasColumnName("day");
            b.HasIndex(e => new { e.TenantId, e.EventKey }).IsUnique();
            b.HasIndex(e => new { e.TenantId, e.OccurredAt });
            b.HasIndex(e => new { e.TenantId, e.SubjectPersonId, e.Day });
        });
    }
}
