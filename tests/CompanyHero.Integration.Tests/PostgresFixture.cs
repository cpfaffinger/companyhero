using System.Collections.Concurrent;
using CompanyHero.Migrations;
using CompanyHero.Modules.Challenges.Domain;
using CompanyHero.Modules.Identity.Application.Kiosk;
using CompanyHero.Modules.Identity.Application.Sessions;
using CompanyHero.Modules.Identity.Infrastructure.Oidc;
using CompanyHero.Modules.Organisation.Application;
using CompanyHero.Platform.Events;
using CompanyHero.Platform.Messaging;
using CompanyHero.Platform.Tenancy;
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
/// startet der API-Host mit der Laufzeitrolle, der echten Cookie-Sitzung (A-007), dem In-Process-OIDC-Anbieter und zwei
/// synthetischen Tenants (<see cref="TwoTenants"/>). Sitzungen der Testpersonen stellt der Sitzungsdienst aus, wie es ein
/// Anmeldeweg täte; Tests senden sie als Cookie mit CSRF-Header.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    public const string MigratorPassword = "test-migrator-pw";
    public const string AppPassword = "test-app-pw";
    public const string DatabaseName = "companyhero";
    public const string ReleaseVersion = "test-release";
    public const string Origin = "https://localhost";

    private readonly PostgreSqlContainer _container;
    private readonly ConcurrentDictionary<(TenantId, PersonId), IssuedSession> _sessions = new();
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

    /// <summary>API-Host mit Laufzeitrolle, echter Sitzung und fester Uhr; die Dienste darin tragen den Tenant-Kontext je Scope.</summary>
    public WebApplicationFactory<Program> Api => _api ?? throw new InvalidOperationException("Fixture nicht initialisiert.");

    public TwoTenants Tenants => _tenants ?? throw new InvalidOperationException("Fixture nicht initialisiert.");

    public FixedClock Clock { get; } = new(FixedClock.Start);

    public FakeIdentityProvider Idp { get; } = new();

    public CapturingMailSender Mails { get; } = new();

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

    /// <summary>Ein weiterer API-Host gegen dieselbe Datenbank (zweite Instanz: gemeinsamer Sitzungsspeicher, gemeinsame Data-Protection-Schlüssel).</summary>
    public WebApplicationFactory<Program> CreateApi(string? connectionString = null)
    {
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:Default", connectionString ?? AppConnectionString);
            b.UseSetting("Identity:PublicOrigin", Origin);
            foreach (var (key, name) in new[] { ("microsoft", "Microsoft"), ("google", "Google") })
            {
                b.UseSetting($"Identity:Providers:{key}:DisplayName", name);
                b.UseSetting($"Identity:Providers:{key}:Authority", FakeIdentityProvider.Issuer(key));
                b.UseSetting($"Identity:Providers:{key}:ClientId", $"companyhero-{key}");
                b.UseSetting($"Identity:Providers:{key}:ClientSecret", $"secret-{key}");
            }

            b.ConfigureServices(services =>
            {
                services.Replace(ServiceDescriptor.Singleton<TimeProvider>(Clock));
                services.Replace(ServiceDescriptor.Singleton<IOidcBackchannel>(new TestOidcBackchannel(Idp)));
                services.Replace(ServiceDescriptor.Singleton<IAccountMailSender>(Mails));
                // Testabonnent eines fremden Moduls (Backend 6.4): die API reiht je Ereignis den Job ein, der Worker-Host verarbeitet ihn.
                services.AddSingleton(new EventSubscription(ChallengeEventTypes.ContributionRecorded, SubscriberJobHandler.JobType));
            });
        });
        factory.ClientOptions.BaseAddress = new Uri(Origin);
        factory.ClientOptions.AllowAutoRedirect = false;
        factory.ClientOptions.HandleCookies = false;
        return factory;
    }

    /// <summary>Client mit der vorab ausgestellten Sitzung einer Testperson (Cookie und CSRF-Header).</summary>
    public HttpClient Client(TenantId tenant, PersonId person, WebApplicationFactory<Program>? api = null)
    {
        var session = _sessions.GetValueOrDefault((tenant, person)) ?? throw new InvalidOperationException("Für diese Person wurde keine Sitzung ausgestellt; IssueSessionAsync verwenden.");
        return ClientWithSession(session, api);
    }

    public HttpClient ClientWithSession(IssuedSession session, WebApplicationFactory<Program>? api = null)
    {
        var client = (api ?? Api).CreateClient();
        TestSessions.Apply(client, session);
        return client;
    }

    /// <summary>Neue Sitzung über den Sitzungsdienst (wie nach einer Anmeldung); die Sitzungsart folgt den Rollen.</summary>
    public async Task<IssuedSession> IssueSessionAsync(TenantId tenant, PersonId person, CancellationToken cancellationToken)
    {
        var session = await TestSessions.IssueAsync(Api.Services, tenant, person, cancellationToken);
        _sessions[(tenant, person)] = session;
        return session;
    }

    public IssuedSession SessionOf(TenantId tenant, PersonId person) => _sessions[(tenant, person)];

    /// <summary>
    /// Kiosk-Personensitzung einer Person (A-005): legt ein Gerät des Tenants an, registriert es und stellt die Personensitzung
    /// innerhalb der Gerätesitzung aus, wie es Anmeldung mit Kennung und PIN am Gerät täte.
    /// </summary>
    public async Task<KioskSession> KioskSessionAsync(TenantId tenant, PersonId person, CancellationToken cancellationToken, int idleSeconds = 60)
    {
        var scopes = Api.Services.GetRequiredService<ITenantScopeFactory>();
        var (device, code) = await scopes.RunAsync(TenantContext.ForPerson(tenant, person, ["member", "tenant_admin"]), (sp, ct) =>
            sp.GetRequiredService<IKioskService>().CreateDeviceAsync($"Testgerät {Guid.NewGuid():N}", ct), cancellationToken);
        var (_, secret) = await scopes.RunAsync(TenantContext.ForPlatform(), (sp, ct) => sp.GetRequiredService<IKioskService>().RegisterAsync(code, ct), cancellationToken);
        var session = await scopes.RunAsync(TenantContext.ForTenant(tenant), (sp, ct) =>
            sp.GetRequiredService<ISessionService>().IssueKioskPersonAsync(person, device.Id, idleSeconds, Modules.Identity.Infrastructure.Http.SessionCookies.DeviceCsrfToken(secret), ct), cancellationToken);
        return new KioskSession(session, device.Id, secret);
    }

    /// <summary>Client mit Kiosk-Personensitzung und Gerätecookie.</summary>
    public HttpClient KioskClient(KioskSession kiosk, WebApplicationFactory<Program>? api = null)
    {
        var client = (api ?? Api).CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", $"{Modules.Identity.Infrastructure.Http.SessionCookies.Session}={kiosk.Session.Token}; {Modules.Identity.Infrastructure.Http.SessionCookies.Csrf}={kiosk.Session.CsrfToken}; {Modules.Identity.Infrastructure.Http.SessionCookies.KioskDevice}={kiosk.DeviceSecret}");
        client.DefaultRequestHeaders.Add(Modules.Identity.Infrastructure.Http.SessionCookies.CsrfHeader, kiosk.Session.CsrfToken);
        return client;
    }

    /// <summary>Testbrowser ohne Sitzung gegen eine Instanz: nimmt Cookies der Anmeldewege an.</summary>
    public TestBrowser Browser(WebApplicationFactory<Program>? api = null) => new((api ?? Api).CreateClient());

    /// <summary>Ein eigener, aktiver Tenant für Tests, die Daten schreiben; die beiden Stamm-Tenants bleiben unverändert.</summary>
    public async Task<TenantId> CreateScratchTenantAsync(string displayName, CancellationToken cancellationToken)
    {
        var scopes = Api.Services.GetRequiredService<ITenantScopeFactory>();
        return await scopes.RunAsync(TenantContext.ForPlatform(), (sp, ct) =>
            sp.GetRequiredService<IOrganisationDirectory>().CreateTenantAsync(displayName, Tenants.OperatorId, ct), cancellationToken);
    }

    /// <summary>Ein eigener aktiver Tenant mit Rollen, Mitgliedern und ausgestellten Sitzungen; die beiden Stamm-Tenants bleiben unverändert.</summary>
    public async Task<ScratchTenant> CreateScratchTenantWithMembersAsync(string displayName)
    {
        var scratch = await TwoTenants.SeedScratchAsync(Api.Services, displayName, Tenants.OperatorId);
        foreach (var person in new[] { scratch.Admin, scratch.Manager, scratch.MemberA, scratch.MemberB })
        {
            await IssueSessionAsync(scratch.Id, person, CancellationToken.None);
        }

        return scratch;
    }

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        await Idp.StartAsync();
        MigrationExitCode = await MigrationRunner.RunAsync(
            new MigrationOptions { MigratorConnectionString = MigratorConnectionString, ReleaseVersion = ReleaseVersion, ImageDigest = "sha256:test" },
            NullLoggerFactory.Instance,
            CancellationToken.None);

        if (MigrationExitCode == 0)
        {
            _api = CreateApi();
            _tenants = await TwoTenants.SeedAsync(_api.Services, Clock);
            foreach (var (tenant, person) in _tenants.Persons)
            {
                await IssueSessionAsync(tenant, person, CancellationToken.None);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_api is not null)
        {
            await _api.DisposeAsync();
        }

        await Idp.DisposeAsync();
        await _container.DisposeAsync();
    }
}

/// <summary>Kiosk-Personensitzung mit Gerät und Gerätegeheimnis für Tests.</summary>
public sealed record KioskSession(IssuedSession Session, Guid DeviceId, string DeviceSecret);

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
