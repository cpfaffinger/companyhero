using System.Data;
using System.Data.Common;
using CompanyHero.Platform.Tenancy;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CompanyHero.Platform.Data;

/// <summary>
/// Setzt den geprüften Kontext beim Start jeder Transaktion auf derselben Verbindung transaktionslokal
/// (<c>set_config(..., true)</c>, Backend 5.2). Der Kontext endet mit Commit oder Rollback und kann für den nächsten
/// Pool-Nutzer nicht fortbestehen; eine Wiederholung baut Transaktion und Kontext gemeinsam neu auf.
/// Ohne Kontext wird die Transaktion abgelehnt, nie unbeschränkt geöffnet.
/// </summary>
internal sealed class TenantContextTransactionInterceptor(ITenantContextAccessor accessor) : DbTransactionInterceptor
{
    public const string TenantSetting = "app.tenant_id";
    public const string ContextSetting = "app.context";

    public override InterceptionResult<DbTransaction> TransactionStarting(DbConnection connection, TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result)
    {
        RequireContext();
        return result;
    }

    public override ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(DbConnection connection, TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result, CancellationToken cancellationToken = default)
    {
        RequireContext();
        return ValueTask.FromResult(result);
    }

    public override DbTransaction TransactionStarted(DbConnection connection, TransactionEndEventData eventData, DbTransaction result)
    {
        ApplyContext(result);
        return result;
    }

    public override async ValueTask<DbTransaction> TransactionStartedAsync(DbConnection connection, TransactionEndEventData eventData, DbTransaction result, CancellationToken cancellationToken = default)
    {
        await ApplyContextAsync(result, cancellationToken);
        return result;
    }

    public override DbTransaction TransactionUsed(DbConnection connection, TransactionEventData eventData, DbTransaction result)
    {
        RequireContext();
        ApplyContext(result);
        return result;
    }

    public override async ValueTask<DbTransaction> TransactionUsedAsync(DbConnection connection, TransactionEventData eventData, DbTransaction result, CancellationToken cancellationToken = default)
    {
        RequireContext();
        await ApplyContextAsync(result, cancellationToken);
        return result;
    }

    private TenantContext RequireContext() => accessor.Current ?? throw new TenantContextMissingException(
        "Transaktion ohne geprüften Tenant-Kontext abgelehnt (Backend 5.2).");

    private void ApplyContext(DbTransaction transaction)
    {
        using var command = CreateCommand(transaction, RequireContext());
        command.ExecuteNonQuery();
    }

    private async Task ApplyContextAsync(DbTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(transaction, RequireContext());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static DbCommand CreateCommand(DbTransaction transaction, TenantContext context)
    {
        var connection = transaction.Connection ?? throw new InvalidOperationException("Transaktion ohne Verbindung.");
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "select set_config(@tenant_key, @tenant, true), set_config(@context_key, @context, true)";
        AddParameter(command, "tenant_key", TenantSetting);
        AddParameter(command, "tenant", context.Kind == TenantContextKind.Tenant ? context.RequireTenant().ToString() : string.Empty);
        AddParameter(command, "context_key", ContextSetting);
        AddParameter(command, "context", context.Kind == TenantContextKind.Tenant ? CompanyHeroClaims.ContextTenant : CompanyHeroClaims.ContextPlatform);
        return command;
    }

    private static void AddParameter(DbCommand command, string name, string value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = DbType.String;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
