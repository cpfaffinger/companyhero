using CompanyHero.Modules.Branding.Infrastructure;
using CompanyHero.Modules.Challenges.Infrastructure;
using CompanyHero.Modules.Feed.Infrastructure;
using CompanyHero.Modules.Identity.Infrastructure;
using CompanyHero.Modules.Metering.Infrastructure;
using CompanyHero.Modules.Notifications.Infrastructure;
using CompanyHero.Modules.Organisation.Infrastructure;
using CompanyHero.Modules.Privacy.Infrastructure;
using CompanyHero.Modules.Progress.Infrastructure;
using CompanyHero.Platform.Data;
using CompanyHero.Platform.Modules;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CompanyHero.Migrations;

/// <summary>Nur für das Werkzeug dotnet-ef beim Erzeugen von Migrationen; zur Laufzeit ungenutzt.</summary>
internal static class DesignTime
{
    public static DbContextOptions<TContext> Options<TContext>(string schema) where TContext : DbContext =>
        new DbContextOptionsBuilder<TContext>()
            .UseNpgsql("Host=localhost;Database=design", npgsql => MigrationsConfiguration.Configure(npgsql, schema))
            .Options;
}

internal sealed class PlatformDbContextFactory : IDesignTimeDbContextFactory<PlatformDbContext>
{
    public PlatformDbContext CreateDbContext(string[] args) => new(DesignTime.Options<PlatformDbContext>(ModuleSchemas.Platform));
}

internal sealed class OrganisationDbContextFactory : IDesignTimeDbContextFactory<OrganisationDbContext>
{
    public OrganisationDbContext CreateDbContext(string[] args) => new(DesignTime.Options<OrganisationDbContext>(ModuleSchemas.Organisation));
}

internal sealed class IdentityDbContextFactory : IDesignTimeDbContextFactory<IdentityDbContext>
{
    public IdentityDbContext CreateDbContext(string[] args) => new(DesignTime.Options<IdentityDbContext>(ModuleSchemas.Identity));
}

internal sealed class PrivacyDbContextFactory : IDesignTimeDbContextFactory<PrivacyDbContext>
{
    public PrivacyDbContext CreateDbContext(string[] args) => new(DesignTime.Options<PrivacyDbContext>(ModuleSchemas.Privacy));
}

internal sealed class ProgressDbContextFactory : IDesignTimeDbContextFactory<ProgressDbContext>
{
    public ProgressDbContext CreateDbContext(string[] args) => new(DesignTime.Options<ProgressDbContext>(ModuleSchemas.Progress));
}

internal sealed class ChallengesDbContextFactory : IDesignTimeDbContextFactory<ChallengesDbContext>
{
    public ChallengesDbContext CreateDbContext(string[] args) => new(DesignTime.Options<ChallengesDbContext>(ModuleSchemas.Challenges));
}

internal sealed class MeteringDbContextFactory : IDesignTimeDbContextFactory<MeteringDbContext>
{
    public MeteringDbContext CreateDbContext(string[] args) => new(DesignTime.Options<MeteringDbContext>(ModuleSchemas.Metering));
}

internal sealed class BrandingDbContextFactory : IDesignTimeDbContextFactory<BrandingDbContext>
{
    public BrandingDbContext CreateDbContext(string[] args) => new(DesignTime.Options<BrandingDbContext>(ModuleSchemas.Branding));
}

internal sealed class FeedDbContextFactory : IDesignTimeDbContextFactory<FeedDbContext>
{
    public FeedDbContext CreateDbContext(string[] args) => new(DesignTime.Options<FeedDbContext>(ModuleSchemas.Feed));
}

internal sealed class NotificationsDbContextFactory : IDesignTimeDbContextFactory<NotificationsDbContext>
{
    public NotificationsDbContext CreateDbContext(string[] args) => new(DesignTime.Options<NotificationsDbContext>(ModuleSchemas.Notifications));
}
