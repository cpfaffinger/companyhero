using CompanyHero.Platform.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CompanyHero.Platform.Hosting;

public static class PlatformDataExtensions
{
    public const string ReadyTag = "ready";

    /// <summary>
    /// Datenzugriff der Laufzeit (Rolle ch_app): kurzlebige DbContexte ohne Pooling (Backend 5.2), Npgsql-Verbindungspool bleibt aktiv.
    /// Die Verbindung kommt aus <c>ConnectionStrings:Default</c>, in Compose aus OpenBao (app/database), in Tests aus der Umgebung.
    /// </summary>
    public static IServiceCollection AddPlatformData(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default fehlt. In Compose liefert OpenBao das Geheimnis app/database.");

        services.AddDbContext<PlatformDbContext>(o => o.UseNpgsql(connectionString));
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
