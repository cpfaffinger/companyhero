using CompanyHero.Platform.Modules;
using CompanyHero.Platform.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace CompanyHero.Platform.Data;

public static class ModuleDbContextExtensions
{
    /// <summary>Assembly mit den Migrationen aller Kontexte; die Historientabelle liegt im Schema des jeweiligen Moduls (A-012).</summary>
    public const string MigrationsAssembly = "CompanyHero.Migrations";

    public const string HistoryTable = "__ef_migrations";

    /// <summary>
    /// Registriert den DbContext eines Moduls: eigenes Schema, eigene Migrationshistorie, kurzlebig und ungepoolt (Backend 5.2),
    /// auf der Verbindung des Scopes mit den Interceptoren der Plattforminfrastruktur (Kontexttransaktion als einzige
    /// Transaktion, Schemagrenze, Tenant-Eigentum). Der Modulname des Schemas muss in <see cref="ModuleSchemas.All"/> stehen.
    /// </summary>
    public static IServiceCollection AddModuleDbContext<TContext>(this IServiceCollection services, string schema)
        where TContext : ModuleDbContext
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
            void Configure(Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.NpgsqlDbContextOptionsBuilder npgsql)
            {
                npgsql.MigrationsAssembly(MigrationsAssembly);
                npgsql.MigrationsHistoryTable(HistoryTable, schema);
            }

            if (data.EnforceTenantContext)
            {
                // Verbindung des Scopes: alle Modulkontexte teilen sie, die Kontexttransaktion trägt sie.
                var transaction = sp.GetRequiredService<ContextTransaction>();
                options.UseNpgsql(transaction.Connection, Configure);
                options.AddInterceptors(
                    new TenantContextTransactionInterceptor(),
                    new ModuleCommandInterceptor(schema, transaction),
                    new TenantOwnershipInterceptor(sp.GetRequiredService<ITenantContextAccessor>()));
            }
            else
            {
                // Migrationslauf: Migrationsrolle, eigene Verbindung, keine Kontextdurchsetzung.
                options.UseNpgsql(data.ConnectionString, Configure);
            }
        });

        return services;
    }
}
