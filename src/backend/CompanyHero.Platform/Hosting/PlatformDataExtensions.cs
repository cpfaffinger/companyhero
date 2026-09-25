using CompanyHero.Platform.Data;
using CompanyHero.Platform.Events;
using CompanyHero.Platform.Jobs;
using CompanyHero.Platform.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace CompanyHero.Platform.Hosting;

public static class PlatformDataExtensions
{
    public const string ReadyTag = "ready";

    /// <summary>
    /// Datenzugriff der Laufzeit (Rolle ch_app): kurzlebige DbContexte ohne Pooling (Backend 5.2), Npgsql-Verbindungspool bleibt aktiv.
    /// Die Verbindung kommt aus <c>ConnectionStrings:Default</c>, in Compose aus OpenBao (app/database), in Tests aus der Umgebung.
    /// Muss vor der Modulregistrierung laufen, weil Modulkontexte die Optionen lesen.
    /// </summary>
    public static IServiceCollection AddPlatformData(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default fehlt. In Compose liefert OpenBao das Geheimnis app/database.");

        return services.AddPlatformData(new PlatformDataOptions { ConnectionString = connectionString, EnforceTenantContext = true });
    }

    public static IServiceCollection AddPlatformData(this IServiceCollection services, PlatformDataOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(options));
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<TenantContextAccessor>();
        services.TryAddScoped<ITenantContextAccessor>(sp => sp.GetRequiredService<TenantContextAccessor>());
        services.TryAddSingleton<ITenantScopeFactory, TenantScopeFactory>();

        // Ein Verbindungspool je Prozess; die Kontexttransaktion eines Scopes leiht sich daraus genau eine Verbindung (Backend 5.2).
        services.TryAddSingleton(_ => new NpgsqlDataSourceBuilder(options.ConnectionString).Build());
        services.TryAddScoped<ContextTransaction>();
        services.TryAddScoped<IContextTransaction>(sp => sp.GetRequiredService<ContextTransaction>());

        services.AddDbContext<PlatformDbContext>((sp, o) => o.UseNpgsql(sp.GetRequiredService<NpgsqlDataSource>()));
        services.AddJobQueue();
        services.AddDomainEvents();
        services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database", tags: [ReadyTag]);
        return services;
    }

    /// <summary>
    /// <c>/api/health/live</c>: Prozess antwortet. <c>/api/health/ready</c>: Datenbank erreichbar.
    /// Caddy, Compose und der Deploy-Job verwenden diese Endpunkte (Betrieb 4).
    /// </summary>
    public static IEndpointRouteBuilder MapCompanyHeroHealth(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapHealthChecks("/api/health/live", new HealthCheckOptions { Predicate = _ => false });
        endpoints.MapHealthChecks("/api/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains(ReadyTag) });
        return endpoints;
    }

    private sealed class DatabaseHealthCheck(PlatformDbContext db) : IHealthCheck
    {
        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                return await db.Database.CanConnectAsync(timeout.Token)
                    ? HealthCheckResult.Healthy()
                    : HealthCheckResult.Unhealthy("Datenbank nicht erreichbar");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return HealthCheckResult.Unhealthy("Datenbank antwortet nicht innerhalb von 5 Sekunden");
            }
        }
    }
}
