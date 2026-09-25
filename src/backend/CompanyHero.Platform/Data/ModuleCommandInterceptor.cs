using System.Data.Common;
using System.Text.RegularExpressions;
using CompanyHero.Platform.Modules;
using CompanyHero.Platform.Tenancy;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CompanyHero.Platform.Data;

/// <summary>
/// Zwei Regeln für jeden Befehl eines Modulkontexts:
/// 1. Datenzugriff nur innerhalb der Kontexttransaktion (Domänenkarte 3). Ein Befehl außerhalb wird abgelehnt,
///    weil der Kontext dann nicht gesetzt wäre; das gilt für Lesen, Exporte und Jobs gleichermaßen (Backend 5.2).
/// 2. Kein Modul liest oder schreibt Tabellen eines anderen Schemas (Domänenkarte 1). Auch Raw SQL nicht.
/// </summary>
internal sealed class ModuleCommandInterceptor : DbCommandInterceptor
{
    private readonly string _schema;
    private readonly IContextTransaction _transaction;
    private readonly Regex _foreignSchema;

    public ModuleCommandInterceptor(string schema, IContextTransaction transaction)
    {
        _schema = schema;
        _transaction = transaction;
        var foreign = ModuleSchemas.All.Where(s => !string.Equals(s, schema, StringComparison.Ordinal)).Select(Regex.Escape);
        _foreignSchema = new Regex($@"(?<![\w.])""?(?:{string.Join("|", foreign)})""?\.", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Check(command);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Check(command);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        Check(command);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Check(command);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        Check(command);
        return result;
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
    {
        Check(command);
        return ValueTask.FromResult(result);
    }

    private void Check(DbCommand command)
    {
        if (!_transaction.IsActive)
        {
            throw new TenantContextMissingException(
                $"Datenzugriff des Schemas '{_schema}' außerhalb der Kontexttransaktion abgelehnt (Backend 5.2).");
        }

        var match = _foreignSchema.Match(command.CommandText);
        if (match.Success)
        {
            throw new SchemaBoundaryViolationException(
                $"Modul '{_schema}' greift auf das fremde Schema '{match.Value.Trim('"', '.')}' zu (Domänenkarte 1).");
        }
    }
}
