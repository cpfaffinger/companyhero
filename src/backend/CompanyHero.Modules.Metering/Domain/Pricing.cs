using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CompanyHero.Modules.Metering.Domain;

/// <summary>Preismodelle (Metering 4.2, A-072).</summary>
public enum PriceModel
{
    Flat = 1,
    PerUnit = 2,
    Tiered = 3,
    Volume = 4,
    Package = 5,
    Step = 6,
    OneTime = 7,
    RevenueShare = 8,
    MinMax = 9,
    Credit = 10,
}

/// <summary>Stufe eines gestaffelten Modells: Einheiten bis einschließlich <paramref name="UpTo"/> (<c>null</c>: offen) zum Stückpreis.</summary>
public sealed record PriceTier(decimal? UpTo, decimal UnitPrice);

/// <summary>
/// Preisregel (Metering 4.1): Modul und Metrik als Bezug, Modell mit Parametern, Priorität für die Reihenfolge. Beträge sind
/// Konfiguration; Einzelpreise mit vier Nachkommastellen. <c>min_max</c> und <c>credit</c> wirken auf das Planergebnis,
/// <c>revenue_share</c> auf die Ebene darüber (Partner); sie tragen kein Modul und keine Metrik.
/// </summary>
public sealed record PriceRule(
    string Key,
    PriceModel Model,
    int Priority,
    string? Module = null,
    string? Metric = null,
    decimal? Amount = null,
    IReadOnlyList<PriceTier>? Tiers = null,
    decimal? Included = null,
    decimal? OveragePrice = null,
    decimal? BlockSize = null,
    decimal? Percent = null,
    decimal? Minimum = null,
    decimal? Maximum = null,
    DateOnly? ValidFrom = null,
    DateOnly? ValidUntil = null);

/// <summary>Versionierter Preisplan eines Tenants (Metering 4.3); vergangene Perioden bleiben unveränderlich.</summary>
public sealed record PricePlan(string Name, IReadOnlyList<PriceRule> Rules, string Currency = "EUR", decimal TaxRate = 0.2000m)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() }, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static PricePlan FromJson(string json) => JsonSerializer.Deserialize<PricePlan>(json, Json) ?? throw new InvalidOperationException("Preisplan leer.");
}

/// <summary>Gemessene Menge je Modul und Metrik einer Periode, getrennt nach bewertet und nicht bewertet (Testphase).</summary>
public sealed record MeteredQuantity(string Module, string Metric, decimal Rated, decimal Unrated);

/// <summary>Eine Rechnungsposition (Metering 6.1 Nr. 3): Regel, Metrik, Menge, Einzelpreis oder Staffel, Betrag gerundet je Position.</summary>
public sealed record InvoiceLine(string RuleKey, PriceModel Model, string? Module, string? Metric, decimal Quantity, decimal? UnitPrice, string Calculation, decimal Net, decimal Tax, bool Rated);

/// <summary>Ergebnis der Bewertung: Positionen, Modifikatoren, Summen, Provision der Ebene darüber und der Wert „wären X Euro gewesen“.</summary>
public sealed record InvoiceCalculation(IReadOnlyList<InvoiceLine> Lines, decimal Net, decimal Tax, decimal Gross, decimal WouldHaveBeen, InvoiceLine? RevenueShare, string Currency, decimal TaxRate);

/// <summary>
/// Bewertung (Metering 4.5, 6.1; A-072): Regeln nach Priorität, dann <c>min_max</c>, dann <c>credit</c>, dann Steuer je Position.
/// Alle Rechnungen in <see cref="decimal"/>; Mengen und Einzelpreise mit vier Nachkommastellen, Rundung kaufmännisch auf zwei
/// Nachkommastellen je Rechnungsposition. Nicht bewertete Mengen (Testphase) ergeben Positionen mit Betrag null und liefern
/// den Vergleichswert „in der Testphase wären das X Euro gewesen“.
/// </summary>
public static class PricingEngine
{
    public static decimal RoundMoney(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    public static decimal RoundQuantity(decimal value) => decimal.Round(value, 4, MidpointRounding.AwayFromZero);

    public static InvoiceCalculation Evaluate(PricePlan plan, IReadOnlyList<MeteredQuantity> quantities, DateOnly periodDay)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(quantities);
        var lines = new List<InvoiceLine>();
        var wouldHaveBeen = 0m;
        var active = plan.Rules.Where(r => (r.ValidFrom is null || r.ValidFrom <= periodDay) && (r.ValidUntil is null || periodDay <= r.ValidUntil)).OrderBy(r => r.Priority).ThenBy(r => r.Key, StringComparer.Ordinal).ToList();

        foreach (var rule in active.Where(r => r.Model is not (PriceModel.MinMax or PriceModel.Credit or PriceModel.RevenueShare)))
        {
            var quantity = quantities.FirstOrDefault(q => string.Equals(q.Module, rule.Module, StringComparison.Ordinal) && string.Equals(q.Metric, rule.Metric, StringComparison.Ordinal));
            if (quantity is null)
            {
                continue;
            }

            if (quantity.Rated != 0m)
            {
                var (ratedNet, calculation, unitPrice) = Amount(rule, quantity.Rated);
                lines.Add(new InvoiceLine(rule.Key, rule.Model, rule.Module, rule.Metric, RoundQuantity(quantity.Rated), unitPrice, calculation, ratedNet, RoundMoney(ratedNet * plan.TaxRate), true));
            }

            if (quantity.Unrated != 0m)
            {
                var (unratedNet, calculation, unitPrice) = Amount(rule, quantity.Unrated);
                wouldHaveBeen += unratedNet;
                lines.Add(new InvoiceLine(rule.Key, rule.Model, rule.Module, rule.Metric, RoundQuantity(quantity.Unrated), unitPrice, calculation + " (Testphase, nicht bewertet)", 0m, 0m, false));
            }
        }

        var subtotal = lines.Sum(l => l.Net);

        // Modifikatoren auf das Planergebnis: min_max, dann credit (Metering 4.5).
        foreach (var rule in active.Where(r => r.Model == PriceModel.MinMax))
        {
            if (rule.Minimum is { } min && subtotal < min)
            {
                var delta = RoundMoney(min - subtotal);
                lines.Add(new InvoiceLine(rule.Key, rule.Model, null, null, 1m, null, Format("Mindestbetrag {0} − Zwischensumme {1}", min, subtotal), delta, RoundMoney(delta * plan.TaxRate), true));
                subtotal += delta;
            }
            else if (rule.Maximum is { } max && subtotal > max)
            {
                var delta = RoundMoney(subtotal - max);
                lines.Add(new InvoiceLine(rule.Key, rule.Model, null, null, 1m, null, Format("Deckel {0}: Zwischensumme {1} gekürzt", max, subtotal), -delta, RoundMoney(-delta * plan.TaxRate), true));
                subtotal -= delta;
            }
        }

        foreach (var rule in active.Where(r => r.Model == PriceModel.Credit))
        {
            var credit = rule.Percent is { } percent
                ? RoundMoney(subtotal * percent)
                : RoundMoney(rule.Amount ?? 0m);
            credit = Math.Min(credit, subtotal);
            if (credit <= 0m)
            {
                continue;
            }

            var calculation = rule.Percent is { } p ? Format("{0} % von {1}", p * 100m, subtotal) : Format("Gutschrift {0}", credit);
            lines.Add(new InvoiceLine(rule.Key, rule.Model, null, null, 1m, null, calculation, -credit, RoundMoney(-credit * plan.TaxRate), true));
            subtotal -= credit;
        }

        var net = lines.Sum(l => l.Net);
        var tax = lines.Sum(l => l.Tax);

        // Provision der Ebene darüber (Partner, Metering 7): auf die Nettosumme, nie Teil der Tenant-Rechnung.
        InvoiceLine? share = null;
        var shareRule = active.FirstOrDefault(r => r.Model == PriceModel.RevenueShare);
        if (shareRule is { Percent: { } rate })
        {
            var amount = RoundMoney(net * rate);
            share = new InvoiceLine(shareRule.Key, PriceModel.RevenueShare, null, null, 1m, null, Format("{0} % von {1}", rate * 100m, net), amount, 0m, true);
        }

        return new InvoiceCalculation(lines, net, tax, net + tax, RoundMoney(wouldHaveBeen), share, plan.Currency, plan.TaxRate);
    }

    /// <summary>Betrag einer Position aus Modell und Menge; die Rechnung je Position ist im Text nachvollziehbar.</summary>
    public static (decimal Net, string Calculation, decimal? UnitPrice) Amount(PriceRule rule, decimal quantity)
    {
        ArgumentNullException.ThrowIfNull(rule);
        switch (rule.Model)
        {
            case PriceModel.Flat:
            case PriceModel.PerUnit:
            case PriceModel.OneTime:
            {
                var unit = Require(rule.Amount, rule, "amount");
                return (RoundMoney(quantity * unit), Format("{0} × {1}", quantity, unit), unit);
            }

            case PriceModel.Tiered:
            {
                var tiers = rule.Tiers is { Count: > 0 } ? rule.Tiers : throw new InvalidOperationException($"Regel {rule.Key}: Stufen fehlen.");
                var remaining = quantity;
                var lower = 0m;
                var total = 0m;
                var parts = new List<string>();
                foreach (var tier in tiers)
                {
                    if (remaining <= 0m)
                    {
                        break;
                    }

                    var capacity = tier.UpTo is { } upTo ? upTo - lower : remaining;
                    var units = Math.Min(remaining, capacity);
                    if (units > 0m)
                    {
                        total += units * tier.UnitPrice;
                        parts.Add(Format("{0} × {1}", units, tier.UnitPrice));
                    }

                    remaining -= units;
                    lower = tier.UpTo ?? lower;
                }

                return (RoundMoney(total), string.Join(" + ", parts), null);
            }

            case PriceModel.Volume:
            {
                var tiers = rule.Tiers is { Count: > 0 } ? rule.Tiers : throw new InvalidOperationException($"Regel {rule.Key}: Stufen fehlen.");
                var tier = tiers.FirstOrDefault(t => t.UpTo is null || quantity <= t.UpTo.Value) ?? tiers[^1];
                return (RoundMoney(quantity * tier.UnitPrice), Format("{0} × {1} (Stufe für alle Einheiten)", quantity, tier.UnitPrice), tier.UnitPrice);
            }

            case PriceModel.Package:
            {
                var basePrice = Require(rule.Amount, rule, "amount");
                var included = Require(rule.Included, rule, "included");
                var overage = Require(rule.OveragePrice, rule, "overagePrice");
                var over = Math.Max(0m, quantity - included);
                return (RoundMoney(basePrice + (over * overage)), Format("Grundpreis {0} inkl. {1} + {2} × {3}", basePrice, included, over, overage), null);
            }

            case PriceModel.Step:
            {
                var block = Require(rule.BlockSize, rule, "blockSize");
                var blockPrice = Require(rule.Amount, rule, "amount");
                var blocks = Math.Ceiling(quantity / block);
                return (RoundMoney(blocks * blockPrice), Format("{0} Blöcke à {1} × {2}", blocks, block, blockPrice), null);
            }

            default:
                throw new InvalidOperationException($"Regel {rule.Key}: Modell {rule.Model} ist kein Positionsmodell.");
        }
    }

    private static decimal Require(decimal? value, PriceRule rule, string parameter) =>
        value ?? throw new InvalidOperationException($"Regel {rule.Key}: Parameter {parameter} fehlt.");

    private static string Format(string format, params object[] args) =>
        string.Format(CultureInfo.InvariantCulture, format, args.Select(a => a is decimal d ? (object)d.ToString("0.####", CultureInfo.InvariantCulture) : a).ToArray());
}

/// <summary>
/// Listenpreisplan des Operators als Vorlage (Metering 4.3): Beispielplan des Durchstichs mit <c>flat</c>, <c>per_unit</c>,
/// <c>tiered</c>, <c>min_max</c>, <c>credit</c> und <c>revenue_share</c>. Alle Beträge sind Konfiguration; dieses Konzept legt
/// keine Preise fest (Metering 4.3). Metriken ohne Regel werden erfasst und nicht bewertet.
/// </summary>
public static class ListPrices
{
    public static PricePlan Default { get; } = new(
        "liste-2026",
        [
            new("m1.flat", PriceModel.Flat, 10, "M1", MeteringMetrics.TenantMonth, Amount: 49.0000m),
            new("m2.flat", PriceModel.Flat, 11, "M2", MeteringMetrics.TenantMonth, Amount: 19.0000m),
            new("m3.flat", PriceModel.Flat, 12, "M3", MeteringMetrics.TenantMonth, Amount: 29.0000m),
            new("kern.active_member", PriceModel.PerUnit, 20, "Kern", MeteringMetrics.ActiveMonth, Amount: 2.5000m),
            new("kern.joined", PriceModel.PerUnit, 21, "Kern", MeteringMetrics.Joined, Amount: 1.0000m),
            new("kern.kiosk", PriceModel.PerUnit, 22, "Kern", MeteringMetrics.KioskDeviceMonth, Amount: 5.0000m),
            new("m1.participant_day", PriceModel.Tiered, 30, "M1", MeteringMetrics.ParticipantDay, Tiers: [new(50m, 0.2000m), new(250m, 0.1500m), new(null, 0.1000m)]),
            new("plan.min_max", PriceModel.MinMax, 100, Minimum: 30.0000m, Maximum: 5000.0000m),
            new("plan.pilotrabatt", PriceModel.Credit, 200, Percent: 0.1000m, ValidUntil: new DateOnly(2027, 12, 31)),
            new("partner.provision", PriceModel.RevenueShare, 300, Percent: 0.2000m),
        ]);
}
