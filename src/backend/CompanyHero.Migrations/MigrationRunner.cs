using System.Globalization;
using System.Text.RegularExpressions;
using CompanyHero.ModuleCatalog;
using CompanyHero.Platform.Data;
using CompanyHero.Platform.Hosting;
using CompanyHero.Platform.Modules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace CompanyHero.Migrations;

/// <summary>
/// Der Migrationslauf (Betrieb 4, A-030): läuft mit der Migrationsrolle vor dem Anwendungsstart,
/// wendet die Migrationen des Plattformbereichs und jedes Modulschemas an (eigene Historie je Schema, A-012),
/// erteilt der Laufzeitrolle ausschließlich Datenrechte auf den Modulschemata und protokolliert den Release-Stand.
/// Jeder Fehler beendet den Lauf mit Exit-Code ungleich null; der Anwendungsstart hängt davon ab.
/// </summary>
public static partial class MigrationRunner
{
    private static readonly TimeSpan DatabaseWaitInterval = TimeSpan.FromSeconds(2);
    private const int DatabaseWaitAttempts = 30;

    public static async Task<int> RunAsync(MigrationOptions options, ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        var log = loggerFactory.CreateLogger("CompanyHero.Migrations");

        if (!RoleNamePattern().IsMatch(options.RuntimeRole))
        {
            log.LogCritical("Ungültiger Name der Laufzeitrolle.");
            return 3;
        }

        try
        {
            await WaitForDatabaseAsync(options.MigratorConnectionString, log, cancellationToken);

            await using var db = CreatePlatformContext(options.MigratorConnectionString, loggerFactory);
            await MigrateAsync(db, ModuleSchemas.Platform, log, cancellationToken);

            await using (var modules = BuildModuleServices(options.MigratorConnectionString, loggerFactory))
            {
                foreach (var registration in modules.GetServices<ModuleDbContextRegistration>())
                {
                    await using var scope = modules.CreateAsyncScope();
                    var context = (DbContext)scope.ServiceProvider.GetRequiredService(registration.ContextType);
                    await MigrateAsync(context, registration.Schema, log, cancellationToken);
                }
            }

            await GrantRuntimeRoleAsync(db, options.RuntimeRole, log, cancellationToken);

            db.Releases.Add(new Release
            {
                Version = options.ReleaseVersion,
                ImageDigest = options.ImageDigest,
                AppliedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync(cancellationToken);

            log.LogInformation("Migrationslauf abgeschlossen: Release {Version}", options.ReleaseVersion);
            return 0;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogCritical(ex, "Migrationslauf abgebrochen; die Anwendung wird nicht gestartet.");
            return 1;
        }
    }

    private static async Task MigrateAsync(DbContext context, string schema, ILogger log, CancellationToken cancellationToken)
    {
        var pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        log.LogInformation("Schema {Schema}: {Count} ausstehende Migration(en)", schema, pending.Count);
        await context.Database.MigrateAsync(cancellationToken);
    }

    private static PlatformDbContext CreatePlatformContext(string connectionString, ILoggerFactory loggerFactory)
    {
        var options = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseNpgsql(connectionString, MigrationsConfiguration.ConfigurePlatform)
            .UseLoggerFactory(loggerFactory)
            .Options;
        return new PlatformDbContext(options);
    }

    /// <summary>
    /// Dieselben Module wie API und Worker, registriert gegen die Migrationsrolle und ohne Kontextdurchsetzung:
    /// der Migrationslauf bewegt keine Tenant-Daten.
    /// </summary>
    private static ServiceProvider BuildModuleServices(string connectionString, ILoggerFactory loggerFactory)
    {
        var services = new ServiceCollection();
        services.AddSingleton(loggerFactory);
        services.AddLogging();
        services.AddPlatformData(new PlatformDataOptions { ConnectionString = connectionString, EnforceTenantContext = false });
        var configuration = new ConfigurationBuilder().Build();
        foreach (var module in AllModules.Create())
        {
            module.AddModule(services, configuration);
        }

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static async Task WaitForDatabaseAsync(string connectionString, ILogger log, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var conn = new NpgsqlConnection(connectionString);
                await conn.OpenAsync(cancellationToken);
                return;
            }
            catch (Exception ex) when (ex is NpgsqlException or TimeoutException && attempt < DatabaseWaitAttempts)
            {
                log.LogWarning("Datenbank noch nicht erreichbar (Versuch {Attempt}/{Max}): {Reason}", attempt, DatabaseWaitAttempts, ex.GetType().Name);
                await Task.Delay(DatabaseWaitInterval, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Laufzeitrolle: USAGE auf den Modulschemata, Datenrechte auf Tabellen und Sequenzen, keine Rechte auf die
    /// Migrationshistorie, kein CREATE. Default-Privilegien sorgen dafür, dass spätere Migrationen dieselben Rechte erzeugen.
    /// </summary>
    private static async Task GrantRuntimeRoleAsync(PlatformDbContext db, string role, ILogger log, CancellationToken cancellationToken)
    {
        var existing = await db.Database
            .SqlQueryRaw<string>("select schema_name as \"Value\" from information_schema.schemata")
            .ToListAsync(cancellationToken);

        foreach (var schema in ModuleSchemas.All.Where(existing.Contains))
        {
            var quoted = QuoteIdentifier(schema);
            var statements = new[]
            {
                $"GRANT USAGE ON SCHEMA {quoted} TO {role}",
                $"GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA {quoted} TO {role}",
                $"GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA {quoted} TO {role}",
                $"ALTER DEFAULT PRIVILEGES IN SCHEMA {quoted} GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO {role}",
                $"ALTER DEFAULT PRIVILEGES IN SCHEMA {quoted} GRANT USAGE, SELECT ON SEQUENCES TO {role}",
                $"REVOKE ALL ON TABLE {quoted}.{QuoteIdentifier(MigrationsConfiguration.HistoryTable)} FROM {role}",
            };

            foreach (var statement in statements)
            {
                await db.Database.ExecuteSqlRawAsync(statement, cancellationToken);
            }

            log.LogInformation("Rechte der Laufzeitrolle {Role} auf Schema {Schema} gesetzt", role, schema);
        }
    }

    private static string QuoteIdentifier(string identifier) =>
        string.Create(CultureInfo.InvariantCulture, $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"");

    [GeneratedRegex("^[a-z_][a-z0-9_]{0,62}$")]
    private static partial Regex RoleNamePattern();
}
