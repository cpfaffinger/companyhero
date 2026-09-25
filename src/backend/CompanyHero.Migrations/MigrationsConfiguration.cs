using CompanyHero.Platform.Modules;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace CompanyHero.Migrations;

public static class MigrationsConfiguration
{
    public const string HistoryTable = "__ef_migrations";

    /// <summary>Migrationen jedes Kontexts liegen in dieser Assembly; die Historientabelle liegt im Schema des Moduls, damit ein Modul mit seinem Schema herauslösbar bleibt (A-012).</summary>
    public static void ConfigurePlatform(NpgsqlDbContextOptionsBuilder npgsql)
    {
        ArgumentNullException.ThrowIfNull(npgsql);
        npgsql.MigrationsAssembly(typeof(MigrationsConfiguration).Assembly.GetName().Name);
        npgsql.MigrationsHistoryTable(HistoryTable, ModuleSchemas.Platform);
    }
}
