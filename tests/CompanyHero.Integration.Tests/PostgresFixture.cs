using CompanyHero.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Testcontainers.PostgreSql;

namespace CompanyHero.Integration.Tests;

/// <summary>
/// Echtes PostgreSQL 18 in Testcontainern mit denselben Rollen wie im Compose-Projekt (Backend 9):
/// dasselbe Init-Skript, dieselbe Migrationsrolle, dieselbe Laufzeitrolle ohne BYPASSRLS.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    public const string MigratorPassword = "test-migrator-pw";
    public const string AppPassword = "test-app-pw";
    public const string DatabaseName = "companyhero";
    public const string ReleaseVersion = "test-release";

    private readonly PostgreSqlContainer _container;

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

    public string SuperuserConnectionString => _container.GetConnectionString();

    public string MigratorConnectionString => For("ch_migrator", MigratorPassword, DatabaseName);

    public string AppConnectionString => For("ch_app", AppPassword, DatabaseName);

    public int MigrationExitCode { get; private set; } = -1;

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

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        MigrationExitCode = await MigrationRunner.RunAsync(
            new MigrationOptions { MigratorConnectionString = MigratorConnectionString, ReleaseVersion = ReleaseVersion, ImageDigest = "sha256:test" },
            NullLoggerFactory.Instance,
            CancellationToken.None);
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();
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
