using System.Globalization;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CompanyHero.Migrations;

/// <summary>
/// Row Level Security als Teil der versionierten Migrationen (A-011). Die Policies lesen den transaktionslokalen
/// Kontext, den die Plattforminfrastruktur beim Transaktionsstart setzt (Backend 5.2). Fehlender Kontext ergibt
/// <c>NULL</c> und damit keine sichtbare Zeile und keinen erlaubten Schreibzugriff. <c>FORCE</c> unterwirft auch den
/// Tabellenbesitzer (Migrationsrolle) der Policy; die Laufzeitrolle ist ohnehin weder Besitzer noch <c>BYPASSRLS</c>.
/// </summary>
public static class RowLevelSecurity
{
    /// <summary>SQL-Ausdruck für den Tenant des aktiven Kontexts (<c>NULL</c> ohne Kontext).</summary>
    public const string CurrentTenant = "nullif(current_setting('app.tenant_id', true), '')::uuid";

    /// <summary>SQL-Ausdruck: Der aktive Kontext ist der Plattformkontext für Operator-Anwendungsfälle (Backend 5.3).</summary>
    public const string IsPlatformContext = "current_setting('app.context', true) = 'platform'";

    /// <summary>Tenantbezogene Tabelle mit Spalte <c>tenant_id</c>: Lesen und Schreiben nur im eigenen Tenant.</summary>
    public static void IsolateByTenant(MigrationBuilder migrationBuilder, string schema, string table)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        Apply(migrationBuilder, schema, table, $"tenant_id = {CurrentTenant}", $"tenant_id = {CurrentTenant}");
    }

    /// <summary>
    /// Plattformtabelle mit Tenant-Bezug über ihre Kennung (Organisation): ein Tenant sieht und pflegt nur seine eigene
    /// Zeile; der Plattformkontext sieht und schreibt alle Organisationen.
    /// </summary>
    public static void IsolateOrganisationRows(MigrationBuilder migrationBuilder, string schema, string table)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        Apply(migrationBuilder, schema, table, $"id = {CurrentTenant} or {IsPlatformContext}", $"id = {CurrentTenant} or {IsPlatformContext}");
    }

    public static void Remove(MigrationBuilder migrationBuilder, string schema, string table)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        var qualified = Qualified(schema, table);
        migrationBuilder.Sql($"drop policy if exists tenant_isolation on {qualified};");
        migrationBuilder.Sql($"alter table {qualified} no force row level security;");
        migrationBuilder.Sql($"alter table {qualified} disable row level security;");
    }

    private static void Apply(MigrationBuilder migrationBuilder, string schema, string table, string usingExpression, string checkExpression)
    {
        var qualified = Qualified(schema, table);
        migrationBuilder.Sql($"alter table {qualified} enable row level security;");
        migrationBuilder.Sql($"alter table {qualified} force row level security;");
        migrationBuilder.Sql($"create policy tenant_isolation on {qualified} for all using ({usingExpression}) with check ({checkExpression});");
    }

    private static string Qualified(string schema, string table) =>
        string.Create(CultureInfo.InvariantCulture, $"\"{schema}\".\"{table}\"");
}
