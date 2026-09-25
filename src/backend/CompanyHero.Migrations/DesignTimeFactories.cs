using CompanyHero.Platform.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CompanyHero.Migrations;

/// <summary>Nur für das Werkzeug dotnet-ef beim Erzeugen von Migrationen; zur Laufzeit ungenutzt.</summary>
internal sealed class PlatformDbContextFactory : IDesignTimeDbContextFactory<PlatformDbContext>
{
    public PlatformDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseNpgsql("Host=localhost;Database=design", MigrationsConfiguration.ConfigurePlatform)
            .Options;
        return new PlatformDbContext(options);
    }
}
