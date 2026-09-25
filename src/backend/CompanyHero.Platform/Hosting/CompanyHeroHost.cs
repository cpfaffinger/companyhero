using System.Reflection;
using CompanyHero.Platform.Modules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace CompanyHero.Platform.Hosting;

/// <summary>
/// Gemeinsame Hosteinrichtung für API und Worker: strukturierte Logs ohne Personenbezug,
/// OpenTelemetry an den Collector (A-029), Modulregistrierung.
/// </summary>
public static class CompanyHeroHost
{
    public static string Version { get; } =
        Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "0.0.0-dev";

    public static IHostApplicationBuilder AddCompanyHeroHost(this IHostApplicationBuilder builder, string serviceName, IEnumerable<IModule> modules)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(modules);

        ConfigureLogging(builder.Logging);
        ConfigureTelemetry(builder, serviceName);

        var moduleList = modules.ToList();
        builder.Services.AddSingleton<IReadOnlyList<IModule>>(moduleList);
        foreach (var module in moduleList)
        {
            module.AddModule(builder.Services, builder.Configuration);
        }

        return builder;
    }

    private static void ConfigureLogging(ILoggingBuilder logging)
    {
        // Logs ohne Personenbezug (Betrieb 3.2, A-026): keine Request-URLs mit Query, keine Header,
        // keine Scopes mit Nutzdaten. Fachlogs verwenden pseudonyme Kennungen.
        logging.ClearProviders();
        logging.AddJsonConsole(options =>
        {
            options.IncludeScopes = false;
            options.UseUtcTimestamp = true;
            options.TimestampFormat = "O";
        });
        logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
        logging.AddFilter("Microsoft.AspNetCore.Routing.EndpointMiddleware", LogLevel.Warning);
        logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);
    }

    private static void ConfigureTelemetry(IHostApplicationBuilder builder, string serviceName)
    {
        var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        var telemetry = builder.Services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(serviceName, serviceVersion: Version, serviceInstanceId: Environment.MachineName));

        telemetry.WithTracing(t =>
        {
            t.AddAspNetCoreInstrumentation(o => o.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/api/health"));
            t.AddHttpClientInstrumentation();
            t.AddNpgsql();
            if (otlpEndpoint is not null)
            {
                t.AddOtlpExporter();
            }
        });

        telemetry.WithMetrics(m =>
        {
            m.AddAspNetCoreInstrumentation();
            m.AddHttpClientInstrumentation();
            m.AddRuntimeInstrumentation();
            m.AddNpgsqlInstrumentation();
            m.AddMeter(CompanyHero.Platform.Jobs.JobMetrics.MeterName);
            if (otlpEndpoint is not null)
            {
                m.AddOtlpExporter();
            }
        });

        if (otlpEndpoint is not null)
        {
            builder.Logging.AddOpenTelemetry(o =>
            {
                o.IncludeScopes = false;
                o.IncludeFormattedMessage = true;
                o.AddOtlpExporter();
            });
        }
    }
}
