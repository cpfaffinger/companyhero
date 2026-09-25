using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CompanyHero.Platform.Data;

/// <summary>
/// Kein Modulkontext beginnt eine eigene Transaktion: Transaktion und Kontext gehören zusammen und werden nur von der
/// Kontexttransaktion der Plattform aufgebaut (<see cref="IContextTransaction"/>, Backend 5.2). Ein Versuch, an ihr
/// vorbei eine Transaktion zu beginnen, wird abgelehnt.
/// </summary>
internal sealed class TenantContextTransactionInterceptor : DbTransactionInterceptor
{
    public override InterceptionResult<DbTransaction> TransactionStarting(DbConnection connection, TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result) =>
        throw Rejected();

    public override ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(DbConnection connection, TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result, CancellationToken cancellationToken = default) =>
        throw Rejected();

    public override DbTransaction TransactionUsed(DbConnection connection, TransactionEventData eventData, DbTransaction result) =>
        throw Rejected();

    public override ValueTask<DbTransaction> TransactionUsedAsync(DbConnection connection, TransactionEventData eventData, DbTransaction result, CancellationToken cancellationToken = default) =>
        throw Rejected();

    private static InvalidOperationException Rejected() => new(
        "Modulkontexte beginnen keine eigene Transaktion; Transaktion und Kontext baut ausschließlich IContextTransaction auf (Backend 5.2).");
}
