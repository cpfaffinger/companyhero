using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CompanyHero.Migrations;

internal static class EntryPoint
{
    private static async Task<int> Main()
    {
        var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        using var loggerFactory = LoggerFactory.Create(l => l.AddJsonConsole(o => o.UseUtcTimestamp = true));

        var connectionString = configuration.GetConnectionString("Migrator");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            loggerFactory.CreateLogger("Migrations").LogCritical("ConnectionStrings__Migrator ist nicht gesetzt.");
            return 2;
        }

        var options = new MigrationOptions
        {
            MigratorConnectionString = connectionString,
            RuntimeRole = configuration["CH_RUNTIME_ROLE"] ?? "ch_app",
            ReleaseVersion = configuration["CH_RELEASE_VERSION"] ?? "0.0.0-dev",
            ImageDigest = configuration["CH_IMAGE_DIGEST"],
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        return await MigrationRunner.RunAsync(options, loggerFactory, cts.Token);
    }
}
