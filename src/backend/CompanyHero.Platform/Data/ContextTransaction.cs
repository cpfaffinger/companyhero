using System.Data;
using CompanyHero.Platform.Tenancy;
using Npgsql;

namespace CompanyHero.Platform.Data;

/// <summary>
/// Die Kontexttransaktion eines Scopes (Domänenkarte 3: „Tenant-Kontext und Transaktion“ gehören der
/// Plattforminfrastruktur; Backend 5.2). Alle Modulkontexte eines Requests oder Jobs arbeiten auf derselben Verbindung;
/// eine fachliche Änderung mit ihren Folgen (Fachereignis, Metering-Emission, Jobs) ist damit eine einzige Transaktion
/// (Backend 3.2, A-006). Der geprüfte Kontext wird beim Beginn transaktionslokal per <c>set_config(..., true)</c> gesetzt
/// und endet mit Commit oder Rollback; er kann für den nächsten Pool-Nutzer nicht fortbestehen.
/// Verschachtelte Aufrufe treten der laufenden Transaktion bei; nur der äußerste Aufrufer beendet sie.
/// Ohne Kontext wird der Beginn abgelehnt, nie unbeschränkt geöffnet.
/// </summary>
public interface IContextTransaction
{
    /// <summary>Wahr, solange die Kontexttransaktion dieses Scopes läuft.</summary>
    bool IsActive { get; }

    /// <summary>
    /// Beginnt die Kontexttransaktion des Scopes oder tritt der laufenden bei. Der zurückgegebene Abschnitt wird mit
    /// <see cref="ContextTransactionScope.CommitAsync"/> abgeschlossen; Entsorgen ohne Commit verwirft die gesamte Transaktion.
    /// </summary>
    Task<ContextTransactionScope> BeginAsync(CancellationToken cancellationToken);
}

/// <summary>Ein Abschnitt der Kontexttransaktion; der äußerste Abschnitt trägt die Transaktion.</summary>
public sealed class ContextTransactionScope : IAsyncDisposable
{
    private readonly ContextTransaction _owner;
    private bool _completed;
    private bool _disposed;

    internal ContextTransactionScope(ContextTransaction owner, bool outermost)
    {
        _owner = owner;
        IsOutermost = outermost;
    }

    public bool IsOutermost { get; }

    /// <summary>Äußerster Abschnitt: Commit. Innerer Abschnitt: erfolgreich beendet; der äußerste entscheidet.</summary>
    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_completed)
        {
            throw new InvalidOperationException("Der Abschnitt der Kontexttransaktion wurde bereits beendet.");
        }

        _completed = true;
        if (IsOutermost)
        {
            await _owner.CommitOutermostAsync(cancellationToken);
        }
    }

    /// <summary>Verwirft die gesamte Kontexttransaktion; ein innerer Abschnitt markiert sie als nicht mehr bestätigbar.</summary>
    public async Task RollbackAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _completed = true;
        await _owner.RollbackAsync(IsOutermost, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _owner.EndScopeAsync(this, _completed);
    }
}

/// <summary>Ein Exemplar je Scope (Request oder Job); erzeugt die Verbindung des Scopes, öffnet sie nur für die Transaktion.</summary>
internal sealed class ContextTransaction(NpgsqlDataSource dataSource, ITenantContextAccessor accessor, IServiceProvider services, IEnumerable<ModuleDbContextRegistration> contexts) : IContextTransaction, IAsyncDisposable
{
    private NpgsqlTransaction? _transaction;
    private int _depth;
    private bool _rollbackOnly;

    /// <summary>Die Verbindung dieses Scopes; alle Modulkontexte des Scopes verwenden genau diese Verbindung.</summary>
    public NpgsqlConnection Connection { get; } = dataSource.CreateConnection();

    public bool IsActive => _transaction is not null;

    /// <summary>Die laufende Transaktion für Plattformbefehle (Jobs, Ereignisse); wirft ohne laufende Transaktion.</summary>
    internal NpgsqlTransaction Current => _transaction ?? throw new TenantContextMissingException(
        "Diese Operation ist nur innerhalb der Kontexttransaktion erlaubt (Backend 5.2, A-006).");

    public async Task<ContextTransactionScope> BeginAsync(CancellationToken cancellationToken)
    {
        if (_transaction is not null)
        {
            _depth++;
            return new ContextTransactionScope(this, outermost: false);
        }

        var context = accessor.Current ?? throw new TenantContextMissingException(
            "Kontexttransaktion ohne geprüften Tenant-Kontext abgelehnt (Backend 5.2).");

        if (Connection.State != ConnectionState.Open)
        {
            await Connection.OpenAsync(cancellationToken);
        }

        var transaction = await Connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await ApplyContextAsync(transaction, context, cancellationToken);
        }
        catch
        {
            await transaction.DisposeAsync();
            await Connection.CloseAsync();
            throw;
        }

        _transaction = transaction;
        _depth = 1;
        _rollbackOnly = false;
        return new ContextTransactionScope(this, outermost: true);
    }

    internal async Task CommitOutermostAsync(CancellationToken cancellationToken)
    {
        var transaction = Current;
        if (_rollbackOnly)
        {
            await FinishAsync(transaction, commit: false, cancellationToken);
            throw new InvalidOperationException("Ein innerer Abschnitt der Kontexttransaktion wurde ohne Bestätigung beendet; die Transaktion wurde verworfen.");
        }

        await FinishAsync(transaction, commit: true, cancellationToken);
    }

    internal async Task RollbackAsync(bool outermost, CancellationToken cancellationToken)
    {
        if (_transaction is null)
        {
            return;
        }

        if (outermost)
        {
            await FinishAsync(_transaction, commit: false, cancellationToken);
        }
        else
        {
            _rollbackOnly = true;
        }
    }

    internal async Task EndScopeAsync(ContextTransactionScope scope, bool completed)
    {
        if (_transaction is null)
        {
            return;
        }

        if (scope.IsOutermost)
        {
            await FinishAsync(_transaction, commit: false, CancellationToken.None);
            return;
        }

        _depth--;
        if (!completed)
        {
            _rollbackOnly = true;
        }
    }

    private async Task FinishAsync(NpgsqlTransaction transaction, bool commit, CancellationToken cancellationToken)
    {
        _transaction = null;
        _depth = 0;
        try
        {
            if (commit)
            {
                await transaction.CommitAsync(cancellationToken);
            }
            else
            {
                await RollbackQuietlyAsync(transaction);
                // Verworfene Änderungen verschwinden auch aus den Modulkontexten des Scopes: der nächste Abschnitt beginnt ohne Altlasten.
                ClearTrackedEntities();
            }
        }
        finally
        {
            await transaction.DisposeAsync();
            // Verbindung zurück in den Pool: kurze Transaktionen, kein Kontext auf der Poolverbindung (Backend 5.2).
            await Connection.CloseAsync();
        }
    }

    private void ClearTrackedEntities()
    {
        foreach (var registration in contexts)
        {
            if (services.GetService(registration.ContextType) is Microsoft.EntityFrameworkCore.DbContext db)
            {
                db.ChangeTracker.Clear();
            }
        }
    }

    /// <summary>Rollback einer Transaktion, die nach einem Fehler bereits serverseitig beendet oder vom Treiber entsorgt sein kann.</summary>
    private static async Task RollbackQuietlyAsync(NpgsqlTransaction transaction)
    {
        try
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException or NpgsqlException)
        {
            // Die Transaktion ist bereits beendet; die Verbindung wird unten geschlossen und damit bereinigt.
        }
    }

    private static async Task ApplyContextAsync(NpgsqlTransaction transaction, TenantContext context, CancellationToken cancellationToken)
    {
        await using var command = transaction.Connection!.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "select set_config($1, $2, true), set_config($3, $4, true)";
        command.Parameters.Add(new NpgsqlParameter { Value = TenantSetting });
        command.Parameters.Add(new NpgsqlParameter { Value = context.Kind == TenantContextKind.Tenant ? context.RequireTenant().ToString() : string.Empty });
        command.Parameters.Add(new NpgsqlParameter { Value = ContextSetting });
        command.Parameters.Add(new NpgsqlParameter { Value = context.Kind == TenantContextKind.Tenant ? CompanyHeroClaims.ContextTenant : CompanyHeroClaims.ContextPlatform });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public const string TenantSetting = "app.tenant_id";
    public const string ContextSetting = "app.context";

    public async ValueTask DisposeAsync()
    {
        if (_transaction is not null)
        {
            await FinishAsync(_transaction, commit: false, CancellationToken.None);
        }

        await Connection.DisposeAsync();
    }
}
