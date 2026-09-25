using CompanyHero.Platform.Data;
using CompanyHero.Platform.Modules;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace CompanyHero.Migrations;

public static class MigrationsConfiguration
{
    public const string HistoryTable = ModuleDbContextExtensions.HistoryTable;

    /// <summary>Migrationen jedes Kontexts liegen in dieser Assembly; die Historientabelle liegt im Schema des Moduls, damit ein Modul mit seinem Schema herauslösbar bleibt (A-012).</summary>
    public static void ConfigurePlatform(NpgsqlDbContextOptionsBuilder npgsql) => Configure(npgsql, ModuleSchemas.Platform);

    public static void Configure(NpgsqlDbContextOptionsBuilder npgsql, string schema)
    {
        ArgumentNullException.ThrowIfNull(npgsql);
        npgsql.MigrationsAssembly(typeof(MigrationsConfiguration).Assembly.GetName().Name);
        npgsql.MigrationsHistoryTable(HistoryTable, schema);
    }
}
