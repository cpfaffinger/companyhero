using CompanyHero.Migrations;
using CompanyHero.Modules.Challenges.Domain;
using CompanyHero.Platform.Events;
using CompanyHero.Modules.Organisation.Application;
using CompanyHero.Platform.Tenancy;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Testcontainers.PostgreSql;

namespace CompanyHero.Integration.Tests;

/// <summary>
/// Echtes PostgreSQL 18 in Testcontainern mit denselben Rollen wie im Compose-Projekt (Backend 9):
/// dasselbe Init-Skript, dieselbe Migrationsrolle, dieselbe Laufzeitrolle ohne BYPASSRLS. Nach dem Migrationslauf
/// startet der API-Host mit der Laufzeitrolle und zwei synthetischen Tenants (<see cref="TwoTenants"/>).
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    public const string MigratorPassword = "test-migrator-pw";
    public const string AppPassword = "test-app-pw";
    public const string DatabaseName = "companyhero";
    public const string ReleaseVersion = "test-release";

    private readonly PostgreSqlContainer _container;
    private WebApplicationFactory<Program>? _api;
    private TwoTenants? _tenants;

    public PostgresFixture()
    {
        var initDir = Path.Combine(RepositoryRoot.Find().FullName, "deploy", "compose", "plattform", "postgres", "init");
        _container = new PostgreSqlBuilder("postgres:18")
            .WithResourceMapping(new DirectoryInfo(initDir), "/docker-entrypoint-initdb.d/")
            .WithEnvironment("CH_MIGRATOR_PASSWORD", MigratorPassword)
            .WithEnvironment("CH_APP_PASSWORD", AppPassword)
            .WithEnvironment("CH_DB_NAME", DatabaseName)
            .Build();
    }

    /// <summary>Superuser auf der Wartungsdatenbank des Containers (nur für Rollen und Datenbankanlage).</summary>
    public string SuperuserConnectionString => _container.GetConnectionString();

    /// <summary>Superuser auf der Anwendungsdatenbank; nur für Katalogprüfungen, nie für Fachzugriffe.</summary>
    public string SuperuserDatabaseConnectionString =>
        new NpgsqlConnectionStringBuilder(SuperuserConnectionString) { Database = DatabaseName }.ConnectionString;

    public string MigratorConnectionString => For("ch_migrator", MigratorPassword, DatabaseName);

    public string AppConnectionString => For("ch_app", AppPassword, DatabaseName);

    public int MigrationExitCode { get; private set; } = -1;

    /// <summary>API-Host mit Laufzeitrolle, Test-Sitzung und fester Uhr; die Dienste darin tragen den Tenant-Kontext je Scope.</summary>
    public WebApplicationFactory<Program> Api => _api ?? throw new InvalidOperationException("Fixture nicht initialisiert.");

    public TwoTenants Tenants => _tenants ?? throw new InvalidOperationException("Fixture nicht initialisiert.");

    public FixedClock Clock { get; } = new(FixedClock.Start);

    public string For(string user, string password, string database)
    {
        var b = new NpgsqlConnectionStringBuilder(SuperuserConnectionString)
        {
            Username = user,
            Password = password,
            Database = database,
        };
        return b.ConnectionString;
    }

    /// <summary>Ein weiterer API-Host gegen dieselbe Datenbank, etwa mit anderem Verbindungspool.</summary>
    public WebApplicationFactory<Program> CreateApi(string? connectionString = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:Default", connectionString ?? AppConnectionString);
            b.ConfigureServices(services =>
            {
                services.Replace(ServiceDescriptor.Singleton<TimeProvider>(Clock));
                // Testabonnent eines fremden Moduls (Backend 6.4): die API reiht je Ereignis den Job ein, der Worker-Host verarbeitet ihn.
                services.AddSingleton(new EventSubscription(ChallengeEventTypes.ContributionRecorded, SubscriberJobHandler.JobType));
                services.AddAuthentication(TestSessionHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, TestSessionHandler>(TestSessionHandler.SchemeName, _ => { });
            });
        });

    /// <summary>Ein eigener, aktiver Tenant für Tests, die Daten schreiben; die beiden Stamm-Tenants bleiben unverändert.</summary>
    public async Task<TenantId> CreateScratchTenantAsync(string displayName, CancellationToken cancellationToken)
    {
        var scopes = Api.Services.GetRequiredService<ITenantScopeFactory>();
        return await scopes.RunAsync(TenantContext.ForPlatform(), (sp, ct) =>
            sp.GetRequiredService<IOrganisationDirectory>().CreateTenantAsync(displayName, Tenants.OperatorId, ct), cancellationToken);
    }

    /// <summary>Ein eigener aktiver Tenant mit Rollen und Mitgliedern; die beiden Stamm-Tenants bleiben unverändert.</summary>
    public Task<ScratchTenant> CreateScratchTenantWithMembersAsync(string displayName) =>
        TwoTenants.SeedScratchAsync(Api.Services, displayName, Tenants.OperatorId);

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        MigrationExitCode = await MigrationRunner.RunAsync(
            new MigrationOptions { MigratorConnectionString = MigratorConnectionString, ReleaseVersion = ReleaseVersion, ImageDigest = "sha256:test" },
            NullLoggerFactory.Instance,
            CancellationToken.None);

        if (MigrationExitCode == 0)
        {
            _api = CreateApi();
            _tenants = await TwoTenants.SeedAsync(_api.Services, Clock);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_api is not null)
        {
            await _api.DisposeAsync();
        }

        await _container.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresTests : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}

internal static class RepositoryRoot
{
    public static DirectoryInfo Find()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "global.json")))
        {
            dir = dir.Parent;
        }

        return dir ?? throw new InvalidOperationException("Repository-Wurzel (global.json) nicht gefunden.");
    }
}
