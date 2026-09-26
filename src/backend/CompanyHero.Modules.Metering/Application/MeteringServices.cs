using CompanyHero.Modules.Metering.Domain;
using CompanyHero.Modules.Metering.Infrastructure;
using CompanyHero.Platform.Data;
using CompanyHero.Platform.Entitlements;
using CompanyHero.Platform.Metering;
using CompanyHero.Platform.Tenancy;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace CompanyHero.Modules.Metering.Application;

/// <summary>Ein Ledger-Ereignis aus Sicht der Prüfung; ohne Personenbezug. Nie über die Tenant-API, nur für Prüfzwecke (Metering 2.2, A-073).</summary>
public sealed record LedgerEntry(Guid Id, string Module, string Metric, string SubjectRef, decimal Quantity, MeteringSource Source, DateTimeOffset OccurredAt, string Period, string IdempotencyKey, bool Late, bool Rated, Guid? ReversalOf);

/// <summary>Lesefunktion des Ledgers für Prüfzwecke (Domänenkarte 2); Tenant-Kontext. Tenant-Oberflächen und -Exporte lesen ausschließlich Tagesaggregate.</summary>
public interface IMeteringLedger
{
    Task<IReadOnlyList<LedgerEntry>> ListAsync(string? metric, CancellationToken cancellationToken);
}

/// <summary>Zustand einer Abrechnungsperiode aus Sicht der Verwaltung.</summary>
public sealed record PeriodRecord(string Period, DateTimeOffset OpenedAt, DateTimeOffset? SealedAt, DateTimeOffset? SaltDestroyedAt, bool SaltAvailable);

/// <summary>Perioden des Tenants (Metering 2.2, 2.3): öffnen, versiegeln, Salz vernichten.</summary>
public interface IBillingPeriods
{
    Task<IReadOnlyList<PeriodRecord>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Versiegelt alle fälligen offenen Perioden des aktiven Tenants, führt die Aggregate nach und erzeugt den Rechnungsentwurf; liefert die versiegelten Perioden.</summary>
    Task<IReadOnlyList<string>> SealDueAsync(CancellationToken cancellationToken);

    /// <summary>Vernichtet die Salze versiegelter Perioden nach Ablauf der sieben Tage; liefert die betroffenen Perioden.</summary>
    Task<IReadOnlyList<string>> DestroySaltsDueAsync(CancellationToken cancellationToken);
}

/// <summary>Preisplan des Tenants (Metering 4.3): Versionen gültig ab Monatserstem; ohne eigene Version gilt der Listenpreisplan.</summary>
public interface IPricePlans
{
    Task<PricePlan> ForPeriodAsync(string period, CancellationToken cancellationToken);

    /// <summary>Operator: neue Version ab der genannten Periode; vergangene Perioden bleiben unveränderlich.</summary>
    Task SetAsync(PricePlan plan, string validFromPeriod, CancellationToken cancellationToken);
}

public sealed record UsageDay(DateOnly Day, decimal Quantity);

/// <summary>Verbrauch je Metrik: personennahe Metriken nur als Summe (<c>Days</c> leer), sonst Tagesaggregate (Metering 5, A-023).</summary>
public sealed record UsageMetric(string Module, string Metric, bool Personal, decimal Total, decimal Rated, decimal Unrated, IReadOnlyList<UsageDay>? Days);

public sealed record UsageReport(string Period, bool Sealed, IReadOnlyList<UsageMetric> Metrics);

/// <summary>Verbrauchsdetail aus Tagesaggregaten (Metering 5, A-073): einzige Datenquelle für Oberfläche, API und Export.</summary>
public interface IUsageReports
{
    /// <summary>Führt die Aggregate der Periode aus dem Ledger nach (stündlich und bei jeder Buchung) und liefert sie.</summary>
    Task<UsageReport> UsageAsync(string period, CancellationToken cancellationToken);

    Task<string> ExportCsvAsync(string period, CancellationToken cancellationToken);

    /// <summary>Metriken mit Preisregel im aktuellen Plan, ohne Beträge (Einsichtsrolle, Metering 5).</summary>
    Task<IReadOnlyList<(string Module, string Metric, PriceModel Model)>> UsedMetricsAsync(CancellationToken cancellationToken);
}

public sealed record Forecast(decimal Net, int ElapsedDays, int DaysInMonth);

public sealed record CostPreview(string Period, InvoiceCalculation Calculation, Forecast Forecast, string PlanName);

/// <summary>Auswirkung einer Buchung vor dem Klick (Metering 5, Entitlements 4.2 Nr. 2): laufender Monat und Folgemonat.</summary>
public sealed record Simulation(string Module, decimal ThisMonth, decimal NextMonth, IReadOnlyList<string> UsageBasedMetrics, string Currency);

/// <summary>Kostenvorschau des laufenden Monats (Metering 5, A-073) aus Tagesaggregaten und dem gültigen Preisplan.</summary>
public interface ICostPreview
{
    Task<CostPreview> CurrentAsync(CancellationToken cancellationToken);

    Task<Simulation> SimulateAsync(string moduleCode, CancellationToken cancellationToken);
}

public sealed record InvoiceDraftRecord(Guid Id, string Period, DateTimeOffset CreatedAt, string Currency, decimal TaxRate, decimal Net, decimal Tax, decimal Gross, decimal WouldHaveBeen, string PlanName, IReadOnlyList<InvoiceLine> Lines);

/// <summary>Rechnungsentwürfe je versiegelter Periode (Metering 6.1 Nr. 5); nummerierte Rechnungen, Versand und Mahnwesen folgen.</summary>
public interface IInvoiceDrafts
{
    Task<IReadOnlyList<InvoiceDraftRecord>> ListAsync(CancellationToken cancellationToken);

    Task<InvoiceDraftRecord?> GetAsync(Guid invoiceId, CancellationToken cancellationToken);
}

/// <summary>Perioden mit Salz innerhalb der laufenden Kontexttransaktion; das Salz liegt nur geschützt in der Zeile (A-113).</summary>
internal sealed class PeriodStore(MeteringDbContext db, ITenantContextAccessor context, IDataProtectionProvider dataProtection, TimeProvider clock)
{
    private readonly IDataProtector _protector = dataProtection.CreateProtector("CompanyHero.Metering.PeriodSalt");

    public Task<BillingPeriod?> FindAsync(string period, CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        return db.Periods.SingleOrDefaultAsync(p => p.TenantId == tenantId && p.Period == period, cancellationToken);
    }

    public async Task<BillingPeriod> EnsureOpenAsync(string period, CancellationToken cancellationToken)
    {
        var existing = await FindAsync(period, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var opened = BillingPeriod.Open(context.Require().RequireTenant(), period, clock.GetUtcNow(), _protector.Protect(PeriodRules.NewSalt()));
        db.Periods.Add(opened);
        await db.SaveChangesAsync(cancellationToken);
        return opened;
    }

    /// <summary>Buchungsperiode eines fachlichen Zeitpunkts: die fachliche Periode, solange sie offen ist, sonst die offene Periode von jetzt (Nachlauf).</summary>
    public async Task<(BillingPeriod Period, bool Late)> BookingPeriodAsync(DateTimeOffset occurredAt, TimeZoneInfo zone, CancellationToken cancellationToken)
    {
        var factual = TenantTimeZone.PeriodOf(occurredAt, zone);
        var current = TenantTimeZone.PeriodOf(clock.GetUtcNow(), zone);
        if (string.CompareOrdinal(factual, current) >= 0)
        {
            return (await EnsureOpenAsync(factual, cancellationToken), false);
        }

        var factualPeriod = await FindAsync(factual, cancellationToken);
        if (factualPeriod is { IsSealed: true })
        {
            return (await EnsureOpenAsync(current, cancellationToken), true);
        }

        return (factualPeriod ?? await EnsureOpenAsync(factual, cancellationToken), false);
    }

    public byte[] Salt(BillingPeriod period)
    {
        ArgumentNullException.ThrowIfNull(period);
        return period.ProtectedSalt is { } protectedSalt
            ? _protector.Unprotect(protectedSalt)
            : throw new InvalidOperationException("Das Periodensalz ist vernichtet; eine Rückrechnung ist nicht möglich (Metering 2.3).");
    }
}

/// <summary>
/// Eigentümer des Querschnitts „Metering-Emission“ (Domänenkarte 3). Schreibt das Ereignis in derselben Kontexttransaktion
/// wie die fachliche Änderung (Metering 3.3, A-006), dedupliziert über den Idempotenzschlüssel, bucht Nachläufer in die
/// offene Periode und markiert Ereignisse eines Moduls in der Testphase als nicht bewertet.
/// </summary>
internal sealed class MeteringEmitter(MeteringDbContext db, IContextTransaction transaction, ITenantContextAccessor context, ITenantTimeZone timeZone, PeriodStore periods, IModuleEntitlements entitlements, TimeProvider clock) : IMeteringEmitter
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

        var zone = await timeZone.GetAsync(cancellationToken);
        var (period, late) = await periods.BookingPeriodAsync(emission.OccurredAt, zone, cancellationToken);

        LedgerEvent? original = null;
        if (emission.ReversalOf is { } reversalKey)
        {
            original = await db.LedgerEvents.AsNoTracking().SingleOrDefaultAsync(e => e.TenantId == tenantId && e.IdempotencyKey == reversalKey, cancellationToken);
        }

        // Testphase: erfasst, nicht bewertet (Metering 2.2); eine Gegenbuchung übernimmt die Bewertung des Ursprungs.
        var rated = original?.Rated ?? !await entitlements.IsTrialAsync(emission.Module, emission.OccurredAt, cancellationToken);
        db.LedgerEvents.Add(LedgerEvent.Record(tenantId, emission, clock.GetUtcNow(), period.Period, late, rated, original?.Id));
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return EmissionOutcome.Recorded;
    }
}

internal sealed class MeteringSlots(IContextTransaction transaction, ITenantTimeZone timeZone, PeriodStore periods) : IMeteringSlots
{
    public async Task<string> SlotForAsync(PersonId personId, DateTimeOffset occurredAt, CancellationToken cancellationToken)
    {
        var zone = await timeZone.GetAsync(cancellationToken);
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var (period, _) = await periods.BookingPeriodAsync(occurredAt, zone, cancellationToken);
        var slot = PeriodRules.Slot(periods.Salt(period), personId);
        await tx.CommitAsync(cancellationToken);
        return slot;
    }
}

internal sealed class MeteringLedger(MeteringDbContext db, IContextTransaction transaction, ITenantContextAccessor context) : IMeteringLedger
{
    public async Task<IReadOnlyList<LedgerEntry>> ListAsync(string? metric, CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var query = db.LedgerEvents.AsNoTracking().Where(e => e.TenantId == tenantId);
        if (metric is not null)
        {
            query = query.Where(e => e.Metric == metric);
        }

        var entries = await query
            .OrderBy(e => e.OccurredAt).ThenBy(e => e.Id)
            .Select(e => new LedgerEntry(e.Id, e.Module, e.Metric, e.SubjectRef, e.Quantity, e.Source, e.OccurredAt, e.Period, e.IdempotencyKey, e.Late, e.Rated, e.ReversalOf))
            .ToListAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return entries;
    }
}

/// <summary>Führt die Tagesaggregate einer Periode aus dem Ledger nach (idempotent); Nachläufer stehen am ersten Tag der Buchungsperiode.</summary>
internal sealed class UsageAggregator(MeteringDbContext db, ITenantContextAccessor context, ITenantTimeZone timeZone, TimeProvider clock)
{
    public async Task RebuildAsync(string period, CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        var zone = await timeZone.GetAsync(cancellationToken);
        var events = await db.LedgerEvents.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.Period == period)
            .Select(e => new { e.Module, e.Metric, e.OccurredAt, e.Quantity, e.Rated, e.Late })
            .ToListAsync(cancellationToken);
        var firstDay = PeriodRules.FirstDay(period);
        var groups = events
            .GroupBy(e => (e.Module, e.Metric, Day: e.Late ? firstDay : TenantTimeZone.DayOf(e.OccurredAt, zone)))
            .ToDictionary(g => g.Key, g => (Rated: g.Where(e => e.Rated).Sum(e => e.Quantity), Unrated: g.Where(e => !e.Rated).Sum(e => e.Quantity), Count: g.Count()));

        var existing = await db.Aggregates.Where(a => a.TenantId == tenantId && a.Period == period).ToListAsync(cancellationToken);
        var now = clock.GetUtcNow();
        foreach (var aggregate in existing)
        {
            if (groups.Remove((aggregate.Module, aggregate.Metric, aggregate.Day), out var sums))
            {
                aggregate.Set(sums.Rated, sums.Unrated, sums.Count, now);
            }
            else
            {
                db.Aggregates.Remove(aggregate);
            }
        }

        foreach (var (key, sums) in groups)
        {
            var aggregate = DailyAggregate.Create(tenantId, period, key.Day, key.Module, key.Metric);
            aggregate.Set(sums.Rated, sums.Unrated, sums.Count, now);
            db.Aggregates.Add(aggregate);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Mengen der Periode je Modul und Metrik aus den Aggregaten.</summary>
    public async Task<IReadOnlyList<MeteredQuantity>> QuantitiesAsync(string period, CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        var rows = await db.Aggregates.AsNoTracking().Where(a => a.TenantId == tenantId && a.Period == period).ToListAsync(cancellationToken);
        return rows.GroupBy(a => (a.Module, a.Metric))
            .Select(g => new MeteredQuantity(g.Key.Module, g.Key.Metric, g.Sum(a => a.RatedQuantity), g.Sum(a => a.UnratedQuantity)))
            .OrderBy(q => q.Module, StringComparer.Ordinal).ThenBy(q => q.Metric, StringComparer.Ordinal)
            .ToList();
    }
}

internal sealed class PricePlans(MeteringDbContext db, IContextTransaction transaction, ITenantContextAccessor context, TimeProvider clock) : IPricePlans
{
    public async Task<PricePlan> ForPeriodAsync(string period, CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var version = await db.PricePlans.AsNoTracking()
            .Where(p => p.TenantId == tenantId && string.Compare(p.ValidFromPeriod, period) <= 0)
            .OrderByDescending(p => p.ValidFromPeriod).ThenByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return version?.Plan ?? ListPrices.Default;
    }

    public async Task SetAsync(PricePlan plan, string validFromPeriod, CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        db.PricePlans.Add(PricePlanVersion.Create(tenantId, validFromPeriod, plan, clock.GetUtcNow()));
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }
}

internal sealed class BillingPeriods(MeteringDbContext db, IContextTransaction transaction, ITenantContextAccessor context, ITenantTimeZone timeZone, PeriodStore periods, UsageAggregator aggregator, IPricePlans plans, TimeProvider clock) : IBillingPeriods
{
    public async Task<IReadOnlyList<PeriodRecord>> ListAsync(CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var rows = await db.Periods.AsNoTracking().Where(p => p.TenantId == tenantId).OrderBy(p => p.Period).ToListAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return rows.Select(p => new PeriodRecord(p.Period, p.OpenedAt, p.SealedAt, p.SaltDestroyedAt, p.ProtectedSalt is not null)).ToList();
    }

    public async Task<IReadOnlyList<string>> SealDueAsync(CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        var now = clock.GetUtcNow();
        var zone = await timeZone.GetAsync(cancellationToken);
        await using var tx = await transaction.BeginAsync(cancellationToken);
        await periods.EnsureOpenAsync(TenantTimeZone.PeriodOf(now, zone), cancellationToken);
        var open = await db.Periods.Where(p => p.TenantId == tenantId && p.SealedAt == null).OrderBy(p => p.Period).ToListAsync(cancellationToken);
        var sealedPeriods = new List<string>();
        foreach (var period in open.Where(p => PeriodRules.IsSealingDue(p.Period, now, zone)))
        {
            await aggregator.RebuildAsync(period.Period, cancellationToken);
            period.Seal(now);
            if (!await db.InvoiceDrafts.AnyAsync(i => i.TenantId == tenantId && i.Period == period.Period, cancellationToken))
            {
                var plan = await plans.ForPeriodAsync(period.Period, cancellationToken);
                var calculation = PricingEngine.Evaluate(plan, await aggregator.QuantitiesAsync(period.Period, cancellationToken), PeriodRules.FirstDay(period.Period));
                db.InvoiceDrafts.Add(InvoiceDraft.Create(tenantId, period.Period, calculation, plan.Name, now));
            }

            sealedPeriods.Add(period.Period);
        }

        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return sealedPeriods;
    }

    public async Task<IReadOnlyList<string>> DestroySaltsDueAsync(CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        var now = clock.GetUtcNow();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var due = await db.Periods.Where(p => p.TenantId == tenantId && p.SealedAt != null && p.SaltDestroyedAt == null).ToListAsync(cancellationToken);
        var destroyed = new List<string>();
        foreach (var period in due.Where(p => PeriodRules.IsSaltDestructionDue(p.SealedAt!.Value, now)))
        {
            period.DestroySalt(now);
            destroyed.Add(period.Period);
        }

        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return destroyed;
    }
}

internal sealed class UsageReports(MeteringDbContext db, IContextTransaction transaction, ITenantContextAccessor context, ITenantTimeZone timeZone, UsageAggregator aggregator, IPricePlans plans, TimeProvider clock) : IUsageReports
{
    public async Task<UsageReport> UsageAsync(string period, CancellationToken cancellationToken)
    {
        if (!PeriodRules.IsValid(period))
        {
            throw new ArgumentException("Periode als YYYY-MM erwartet.", nameof(period));
        }

        var tenantId = context.Require().RequireTenant();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var billingPeriod = await db.Periods.AsNoTracking().SingleOrDefaultAsync(p => p.TenantId == tenantId && p.Period == period, cancellationToken);
        if (billingPeriod is not { IsSealed: true })
        {
            // Offene Periode: Aggregate bei jedem Abruf nachführen (Metering 5: stündlich und bei jeder Buchung).
            await aggregator.RebuildAsync(period, cancellationToken);
        }

        var rows = await db.Aggregates.AsNoTracking().Where(a => a.TenantId == tenantId && a.Period == period).OrderBy(a => a.Day).ToListAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        var metrics = rows.GroupBy(a => (a.Module, a.Metric)).Select(g =>
        {
            var personal = MeteringMetrics.IsPersonal(g.Key.Metric);
            var rated = g.Sum(a => a.RatedQuantity);
            var unrated = g.Sum(a => a.UnratedQuantity);
            // Keine Tagesschnitte bei personennahen Metriken (A-023): nur die Monatssumme.
            var days = personal ? null : g.OrderBy(a => a.Day).Select(a => new UsageDay(a.Day, a.RatedQuantity + a.UnratedQuantity)).ToList();
            return new UsageMetric(g.Key.Module, g.Key.Metric, personal, rated + unrated, rated, unrated, days);
        }).OrderBy(m => m.Module, StringComparer.Ordinal).ThenBy(m => m.Metric, StringComparer.Ordinal).ToList();
        return new UsageReport(period, billingPeriod?.IsSealed == true, metrics);
    }

    public async Task<string> ExportCsvAsync(string period, CancellationToken cancellationToken)
    {
        var report = await UsageAsync(period, cancellationToken);
        var lines = new List<string> { "periode;modul;metrik;tag;menge" };
        foreach (var metric in report.Metrics)
        {
            if (metric.Days is null)
            {
                lines.Add(string.Join(';', report.Period, metric.Module, metric.Metric, string.Empty, Quantity(metric.Total)));
                continue;
            }

            foreach (var day in metric.Days)
            {
                lines.Add(string.Join(';', report.Period, metric.Module, metric.Metric, day.Day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), Quantity(day.Quantity)));
            }
        }

        return string.Join('\n', lines) + "\n";
    }

    public async Task<IReadOnlyList<(string Module, string Metric, PriceModel Model)>> UsedMetricsAsync(CancellationToken cancellationToken)
    {
        var zone = await timeZone.GetAsync(cancellationToken);
        var plan = await plans.ForPeriodAsync(TenantTimeZone.PeriodOf(clock.GetUtcNow(), zone), cancellationToken);
        return plan.Rules.Where(r => r.Module is not null && r.Metric is not null).OrderBy(r => r.Priority).Select(r => (r.Module!, r.Metric!, r.Model)).ToList();
    }

    private static string Quantity(decimal value) => value.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture);
}

internal sealed class CostPreviewService(IContextTransaction transaction, ITenantTimeZone timeZone, PeriodStore periods, UsageAggregator aggregator, IPricePlans plans, TimeProvider clock) : ICostPreview
{
    public async Task<CostPreview> CurrentAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var zone = await timeZone.GetAsync(cancellationToken);
        var period = TenantTimeZone.PeriodOf(now, zone);
        await using var tx = await transaction.BeginAsync(cancellationToken);
        await periods.EnsureOpenAsync(period, cancellationToken);
        await aggregator.RebuildAsync(period, cancellationToken);
        var quantities = await aggregator.QuantitiesAsync(period, cancellationToken);
        var plan = await plans.ForPeriodAsync(period, cancellationToken);
        await tx.CommitAsync(cancellationToken);

        var calculation = PricingEngine.Evaluate(plan, quantities, PeriodRules.FirstDay(period));
        var today = TenantTimeZone.DayOf(now, zone);
        var daysInMonth = PeriodRules.DaysIn(period);
        // Prognose als Schätzung (Metering 5): linearer Verlauf des bisherigen Monats; die letzten drei Monate folgen mit der Historie.
        var elapsed = today.Day;
        var forecast = PricingEngine.RoundMoney(calculation.Net * daysInMonth / elapsed);
        return new CostPreview(period, calculation, new Forecast(forecast, elapsed, daysInMonth), plan.Name);
    }

    public async Task<Simulation> SimulateAsync(string module, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var zone = await timeZone.GetAsync(cancellationToken);
        var period = TenantTimeZone.PeriodOf(now, zone);
        var plan = await plans.ForPeriodAsync(period, cancellationToken);
        var rules = plan.Rules.Where(r => string.Equals(r.Module, module, StringComparison.Ordinal)).ToList();
        var flats = rules.Where(r => r.Model == PriceModel.Flat && r.Metric == MeteringMetrics.TenantMonth).ToList();
        var today = TenantTimeZone.DayOf(now, zone);
        var daysInMonth = PeriodRules.DaysIn(period);
        var fraction = decimal.Round((daysInMonth - today.Day + 1) / (decimal)daysInMonth, 4, MidpointRounding.AwayFromZero);
        var thisMonth = flats.Sum(r => PricingEngine.Amount(r, fraction).Net);
        var nextMonth = flats.Sum(r => PricingEngine.Amount(r, 1m).Net);
        return new Simulation(module, thisMonth, nextMonth, rules.Where(r => r.Model != PriceModel.Flat).Select(r => r.Metric!).ToList(), plan.Currency);
    }
}

internal sealed class InvoiceDrafts(MeteringDbContext db, IContextTransaction transaction, ITenantContextAccessor context) : IInvoiceDrafts
{
    public async Task<IReadOnlyList<InvoiceDraftRecord>> ListAsync(CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var rows = await db.InvoiceDrafts.AsNoTracking().Where(i => i.TenantId == tenantId).OrderByDescending(i => i.Period).ToListAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return rows.Select(ToRecord).ToList();
    }

    public async Task<InvoiceDraftRecord?> GetAsync(Guid invoiceId, CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var row = await db.InvoiceDrafts.AsNoTracking().SingleOrDefaultAsync(i => i.TenantId == tenantId && i.Id == invoiceId, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return row is null ? null : ToRecord(row);
    }

    private static InvoiceDraftRecord ToRecord(InvoiceDraft i) => new(i.Id, i.Period, i.CreatedAt, i.Currency, i.TaxRate, i.Net, i.Tax, i.Gross, i.WouldHaveBeen, i.PlanName, i.Lines);
}
