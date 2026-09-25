using CompanyHero.Modules.Metering.Domain;
using CompanyHero.Modules.Metering.Infrastructure;
using CompanyHero.Platform.Data;
using CompanyHero.Platform.Metering;
using CompanyHero.Platform.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace CompanyHero.Modules.Metering.Application;

/// <summary>Ein Ledger-Ereignis aus Sicht der Verwaltung; ohne Personenbezug.</summary>
public sealed record LedgerEntry(Guid Id, string Module, string Metric, string SubjectRef, decimal Quantity, MeteringSource Source, DateTimeOffset OccurredAt, string Period, string IdempotencyKey);

/// <summary>Öffentliche Lesefunktion des Ledgers (Domänenkarte 2); Tenant-Kontext.</summary>
public interface IMeteringLedger
{
    Task<IReadOnlyList<LedgerEntry>> ListAsync(string? metric, CancellationToken cancellationToken);
}

/// <summary>
/// Eigentümer des Querschnitts „Metering-Emission“ (Domänenkarte 3). Schreibt das Ereignis in derselben
/// Kontexttransaktion wie die fachliche Änderung (Metering 3.3, A-006) und dedupliziert über den Idempotenzschlüssel.
/// </summary>
internal sealed class MeteringEmitter(MeteringDbContext db, IContextTransaction transaction, ITenantContextAccessor context, TimeProvider clock) : IMeteringEmitter
{
    public async Task<EmissionOutcome> EmitAsync(MeteringEmission emission, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(emission);
        if (!transaction.IsActive)
        {
            throw new InvalidOperationException("Metering-Emission nur in der Transaktion der fachlichen Änderung (Metering 3.3, A-006).");
        }

        var tenantId = context.Require().RequireTenant();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var exists = await db.LedgerEvents.AnyAsync(e => e.TenantId == tenantId && e.IdempotencyKey == emission.IdempotencyKey, cancellationToken);
        if (exists)
        {
            await tx.CommitAsync(cancellationToken);
            return EmissionOutcome.Duplicate;
        }

        db.LedgerEvents.Add(LedgerEvent.Record(tenantId, emission, clock.GetUtcNow()));
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return EmissionOutcome.Recorded;
    }
}

internal sealed class MeteringLedger(MeteringDbContext db, IContextTransaction transaction, ITenantContextAccessor context) : IMeteringLedger
{
    public async Task<IReadOnlyList<LedgerEntry>> ListAsync(string? metric, CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var query = db.LedgerEvents.Where(e => e.TenantId == tenantId);
        if (metric is not null)
        {
            query = query.Where(e => e.Metric == metric);
        }

        var entries = await query
            .OrderBy(e => e.OccurredAt).ThenBy(e => e.Id)
            .Select(e => new LedgerEntry(e.Id, e.Module, e.Metric, e.SubjectRef, e.Quantity, e.Source, e.OccurredAt, e.Period, e.IdempotencyKey))
            .ToListAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return entries;
    }
}
