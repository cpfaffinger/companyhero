using CompanyHero.Platform.Modules;
using CompanyHero.Platform.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace CompanyHero.Platform.Data;

public static class ModuleDbContextExtensions
{
    /// <summary>Assembly mit den Migrationen aller Kontexte; die Historientabelle liegt im Schema des jeweiligen Moduls (A-012).</summary>
    public const string MigrationsAssembly = "CompanyHero.Migrations";

    public const string HistoryTable = "__ef_migrations";

    /// <summary>
    /// Registriert den DbContext eines Moduls: eigenes Schema, eigene Migrationshistorie, kurzlebig und ungepoolt (Backend 5.2),
    /// mit den Interceptoren der Plattforminfrastruktur (Kontext beim Transaktionsstart, Transaktionspflicht,
    /// Schemagrenze, Tenant-Eigentum). Der Modulname des Schemas muss in <see cref="ModuleSchemas.All"/> stehen.
    /// </summary>
    public static IServiceCollection AddModuleDbContext<TContext>(this IServiceCollection services, string schema)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        if (!ModuleSchemas.All.Contains(schema, StringComparer.Ordinal))
        {
            throw new ArgumentException($"Unbekanntes Modulschema '{schema}'.", nameof(schema));
        }

        services.TryAddScoped<TenantContextAccessor>();
        services.TryAddScoped<ITenantContextAccessor>(sp => sp.GetRequiredService<TenantContextAccessor>());
        services.AddSingleton(new ModuleDbContextRegistration(typeof(TContext), schema));

        services.AddDbContext<TContext>((sp, options) =>
        {
            var data = sp.GetRequiredService<IOptions<PlatformDataOptions>>().Value;
            options.UseNpgsql(data.ConnectionString, npgsql =>
            {
                npgsql.MigrationsAssembly(MigrationsAssembly);
                npgsql.MigrationsHistoryTable(HistoryTable, schema);
            });

            if (data.EnforceTenantContext)
            {
                var accessor = sp.GetRequiredService<ITenantContextAccessor>();
                options.AddInterceptors(
                    new TenantContextTransactionInterceptor(accessor),
                    new ModuleCommandInterceptor(schema),
                    new TenantOwnershipInterceptor(accessor));
            }
        });

        return services;
    }
}
