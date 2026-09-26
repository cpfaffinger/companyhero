using CompanyHero.Platform.Metering;
using CompanyHero.Platform.Tenancy;

namespace CompanyHero.Modules.Metering.Domain;

/// <summary>
/// Ereignis im Ledger (Metering 2.1, A-069): anonymer Bezug, Menge mit vier Nachkommastellen, Buchungsperiode,
/// Idempotenzschlüssel, Nachlauf-Kennzeichnung, Storno-Verweis und Bewertungsmerker (Testphase: erfasst, nicht bewertet).
/// Nie ein Anzeigename, keine Personen-ID, kein Messwert einer Person. Append-only: Korrekturen sind Gegenbuchungen.
/// </summary>
public sealed class LedgerEvent : ITenantOwned
{
    private LedgerEvent(TenantId tenantId, Guid id, string module, string metric, string subjectRef, decimal quantity, MeteringSource source, DateTimeOffset occurredAt, string period, string idempotencyKey, DateTimeOffset recordedAt, bool late, bool rated, Guid? reversalOf)
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
        Late = late;
        Rated = rated;
        ReversalOf = reversalOf;
    }

    public TenantId TenantId { get; }

    public Guid Id { get; }

    public string Module { get; }

    public string Metric { get; }

    public string SubjectRef { get; }

    public decimal Quantity { get; }

    public MeteringSource Source { get; }

    /// <summary>Fachlicher Zeitpunkt; bleibt auch bei Nachlauf erhalten.</summary>
    public DateTimeOffset OccurredAt { get; }

    /// <summary>Buchungsperiode <c>YYYY-MM</c> in der Tenant-Zeitzone; bei Nachlauf die offene Periode.</summary>
    public string Period { get; }

    public string IdempotencyKey { get; }

    /// <summary>Gesetzt, wenn das Ereignis nach Versiegelung seiner fachlichen Periode eintraf (Metering 2.2).</summary>
    public bool Late { get; }

    /// <summary>Falsch in der Testphase des Moduls: erfasst, nicht bewertet (Metering 2.2).</summary>
    public bool Rated { get; }

    public Guid? ReversalOf { get; }

    public DateTimeOffset RecordedAt { get; }

    public static LedgerEvent Record(TenantId tenantId, MeteringEmission emission, DateTimeOffset recordedAt, string bookingPeriod, bool late, bool rated, Guid? reversalOf)
    {
        ArgumentNullException.ThrowIfNull(emission);
        ArgumentException.ThrowIfNullOrWhiteSpace(emission.Module);
        ArgumentException.ThrowIfNullOrWhiteSpace(emission.Metric);
        ArgumentException.ThrowIfNullOrWhiteSpace(emission.SubjectRef);
        ArgumentException.ThrowIfNullOrWhiteSpace(emission.IdempotencyKey);
        if (!PeriodRules.IsValid(bookingPeriod))
        {
            throw new ArgumentException("Buchungsperiode als YYYY-MM erwartet.", nameof(bookingPeriod));
        }

        return new LedgerEvent(
            tenantId,
            Guid.CreateVersion7(),
            emission.Module,
            emission.Metric,
            emission.SubjectRef,
            decimal.Round(emission.Quantity, 4, MidpointRounding.ToEven),
            emission.Source,
            emission.OccurredAt.ToUniversalTime(),
            bookingPeriod,
            emission.IdempotencyKey,
            recordedAt.ToUniversalTime(),
            late,
            rated,
            reversalOf);
    }
}

/// <summary>
/// Tagesaggregat je Tenant, Periode, Tag, Modul und Metrik (Metering 5, A-073): vom Worker aus dem Ledger geführt, einzige
/// Datenquelle für Oberflächen und Exporte des Tenants. Getrennt nach bewertet und nicht bewertet.
/// </summary>
public sealed class DailyAggregate : ITenantOwned
{
    private DailyAggregate(TenantId tenantId, string period, DateOnly day, string module, string metric)
    {
        TenantId = tenantId;
        Period = period;
        Day = day;
        Module = module;
        Metric = metric;
    }

    public TenantId TenantId { get; }

    public string Period { get; }

    /// <summary>Kalendertag des fachlichen Zeitpunkts in der Tenant-Zeitzone; Nachläufer stehen in der Buchungsperiode am ersten Tag.</summary>
    public DateOnly Day { get; }

    public string Module { get; }

    public string Metric { get; }

    public decimal RatedQuantity { get; private set; }

    public decimal UnratedQuantity { get; private set; }

    public int EventCount { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static DailyAggregate Create(TenantId tenantId, string period, DateOnly day, string module, string metric) => new(tenantId, period, day, module, metric);

    public void Set(decimal rated, decimal unrated, int events, DateTimeOffset now)
    {
        RatedQuantity = rated;
        UnratedQuantity = unrated;
        EventCount = events;
        UpdatedAt = now;
    }
}

/// <summary>Versionierter Preisplan eines Tenants (Metering 4.3): gültig ab Monatserstem; als JSON gespeichert, unveränderlich je Version.</summary>
public sealed class PricePlanVersion : ITenantOwned
{
    private PricePlanVersion(TenantId tenantId, Guid id, string validFromPeriod, string planJson, DateTimeOffset createdAt)
    {
        TenantId = tenantId;
        Id = id;
        ValidFromPeriod = validFromPeriod;
        PlanJson = planJson;
        CreatedAt = createdAt;
    }

    public TenantId TenantId { get; }

    public Guid Id { get; }

    /// <summary>Erste Periode, für die die Version gilt (<c>YYYY-MM</c>).</summary>
    public string ValidFromPeriod { get; }

    public string PlanJson { get; }

    public DateTimeOffset CreatedAt { get; }

    public static PricePlanVersion Create(TenantId tenantId, string validFromPeriod, PricePlan plan, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!PeriodRules.IsValid(validFromPeriod))
        {
            throw new ArgumentException("Periode als YYYY-MM erwartet.", nameof(validFromPeriod));
        }

        return new PricePlanVersion(tenantId, Guid.CreateVersion7(), validFromPeriod, plan.ToJson(), now);
    }

    public PricePlan Plan => PricePlan.FromJson(PlanJson);
}

/// <summary>Rechnungsentwurf je versiegelter Periode (Metering 6.1 Nr. 5): änderbar, ohne fortlaufende Nummer; Positionen mit Regel und Mengennachweis.</summary>
public sealed class InvoiceDraft : ITenantOwned
{
    private InvoiceDraft(TenantId tenantId, Guid id, string period, DateTimeOffset createdAt, string currency, decimal taxRate, decimal net, decimal tax, decimal gross, decimal wouldHaveBeen, string linesJson, string planName)
    {
        TenantId = tenantId;
        Id = id;
        Period = period;
        CreatedAt = createdAt;
        Currency = currency;
        TaxRate = taxRate;
        Net = net;
        Tax = tax;
        Gross = gross;
        WouldHaveBeen = wouldHaveBeen;
        LinesJson = linesJson;
        PlanName = planName;
    }

    private static readonly System.Text.Json.JsonSerializerOptions Json = new(System.Text.Json.JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    public TenantId TenantId { get; }

    public Guid Id { get; }

    public string Period { get; }

    public DateTimeOffset CreatedAt { get; }

    public string Currency { get; }

    public decimal TaxRate { get; }

    public decimal Net { get; }

    public decimal Tax { get; }

    public decimal Gross { get; }

    /// <summary>„In der Testphase wären das X Euro gewesen“ (Metering 5).</summary>
    public decimal WouldHaveBeen { get; }

    public string LinesJson { get; }

    public string PlanName { get; }

    public static InvoiceDraft Create(TenantId tenantId, string period, InvoiceCalculation calculation, string planName, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(calculation);
        var lines = calculation.RevenueShare is { } share ? calculation.Lines.Append(share).ToList() : calculation.Lines.ToList();
        return new InvoiceDraft(tenantId, Guid.CreateVersion7(), period, now, calculation.Currency, calculation.TaxRate, calculation.Net, calculation.Tax, calculation.Gross, calculation.WouldHaveBeen, System.Text.Json.JsonSerializer.Serialize(lines, Json), planName);
    }

    public IReadOnlyList<InvoiceLine> Lines => System.Text.Json.JsonSerializer.Deserialize<List<InvoiceLine>>(LinesJson, Json) ?? [];
}
