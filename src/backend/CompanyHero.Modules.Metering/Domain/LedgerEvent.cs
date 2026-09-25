using CompanyHero.Platform.Metering;
using CompanyHero.Platform.Tenancy;

namespace CompanyHero.Modules.Metering.Domain;

/// <summary>
/// Ereignis im Ledger (Metering 2.1, A-069): anonymer Bezug, Menge mit vier Nachkommastellen, Periode, Idempotenzschlüssel.
/// Nie ein Anzeigename, keine Personen-ID, kein Messwert einer Person. Partitionierung je Periode und Versiegelung
/// folgen mit der Stufe Geld.
/// </summary>
public sealed class LedgerEvent : ITenantOwned
{
    private LedgerEvent(TenantId tenantId, Guid id, string module, string metric, string subjectRef, decimal quantity, MeteringSource source, DateTimeOffset occurredAt, string period, string idempotencyKey, DateTimeOffset recordedAt)
    {
        TenantId = tenantId;
        Id = id;
        Module = module;
        Metric = metric;
        SubjectRef = subjectRef;
        Quantity = quantity;
        Source = source;
        OccurredAt = occurredAt;
        Period = period;
        IdempotencyKey = idempotencyKey;
        RecordedAt = recordedAt;
    }

    public TenantId TenantId { get; }

    public Guid Id { get; }

    public string Module { get; }

    public string Metric { get; }

    public string SubjectRef { get; }

    public decimal Quantity { get; }

    public MeteringSource Source { get; }

    public DateTimeOffset OccurredAt { get; }

    /// <summary>Abrechnungsperiode <c>YYYY-MM</c> in der Tenant-Zeitzone.</summary>
    public string Period { get; }

    public string IdempotencyKey { get; }

    /// <summary>Gesetzt, wenn das Ereignis nach Versiegelung seiner fachlichen Periode eintraf (Metering 2.2); Versiegelung folgt in Stufe 7.</summary>
    public bool Late { get; private set; }

    public Guid? ReversalOf { get; private set; }

    public DateTimeOffset RecordedAt { get; }

    public static LedgerEvent Record(TenantId tenantId, MeteringEmission emission, DateTimeOffset recordedAt)
    {
        ArgumentNullException.ThrowIfNull(emission);
        ArgumentException.ThrowIfNullOrWhiteSpace(emission.Module);
        ArgumentException.ThrowIfNullOrWhiteSpace(emission.Metric);
        ArgumentException.ThrowIfNullOrWhiteSpace(emission.SubjectRef);
        ArgumentException.ThrowIfNullOrWhiteSpace(emission.IdempotencyKey);
        return new LedgerEvent(
            tenantId,
            Guid.CreateVersion7(),
            emission.Module,
            emission.Metric,
            emission.SubjectRef,
            decimal.Round(emission.Quantity, 4, MidpointRounding.ToEven),
            emission.Source,
            emission.OccurredAt.ToUniversalTime(),
            TenantTimeZone.PeriodOf(emission.OccurredAt),
            emission.IdempotencyKey,
            recordedAt.ToUniversalTime());
    }
}
