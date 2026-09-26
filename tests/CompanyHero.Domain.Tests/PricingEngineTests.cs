using CompanyHero.Modules.Metering.Domain;

namespace CompanyHero.Domain.Tests;

/// <summary>Metering 4.2, 4.5, 9.4 bis 9.6 (A-072): sechs Modelle in einem Plan, jede Position einzeln nachrechenbar, Rundung je Position, Testphase mit Betrag null.</summary>
public sealed class PricingEngineTests
{
    private static readonly DateOnly September = new(2026, 9, 1);

    /// <summary>Beispielplan mit tiered, flat, per_unit, revenue_share, min_max und credit (Metering 9.4).</summary>
    private static readonly PricePlan Beispielplan = new(
        "beispiel",
        [
            new("m1.flat", PriceModel.Flat, 10, "M1", MeteringMetrics.TenantMonth, Amount: 49.0000m),
            new("kern.active", PriceModel.PerUnit, 20, "Kern", MeteringMetrics.ActiveMonth, Amount: 2.5000m),
            new("m1.days", PriceModel.Tiered, 30, "M1", MeteringMetrics.ParticipantDay, Tiers: [new(50m, 0.2000m), new(250m, 0.1500m), new(null, 0.1000m)]),
            new("plan.min_max", PriceModel.MinMax, 100, Minimum: 30.0000m, Maximum: 5000.0000m),
            new("plan.credit", PriceModel.Credit, 200, Percent: 0.1000m),
            new("partner.share", PriceModel.RevenueShare, 300, Percent: 0.2000m),
        ]);

    [Fact]
    public void Beispielplan_liefert_einzeln_nachrechenbare_Positionen_mit_Rundung_je_Position()
    {
        var quantities = new List<MeteredQuantity>
        {
            new("M1", MeteringMetrics.TenantMonth, 1m, 0m),
            new("Kern", MeteringMetrics.ActiveMonth, 37m, 0m),
            new("M1", MeteringMetrics.ParticipantDay, 300m, 0m),
        };

        var result = PricingEngine.Evaluate(Beispielplan, quantities, September);

        // flat: 1 × 49,00 = 49,00; per_unit: 37 × 2,50 = 92,50; tiered: 50 × 0,20 + 200 × 0,15 + 50 × 0,10 = 10 + 30 + 5 = 45,00.
        Assert.Equal(49.00m, result.Lines.Single(l => l.RuleKey == "m1.flat").Net);
        Assert.Equal(92.50m, result.Lines.Single(l => l.RuleKey == "kern.active").Net);
        var tiered = result.Lines.Single(l => l.RuleKey == "m1.days");
        Assert.Equal(45.00m, tiered.Net);
        Assert.Equal("50 × 0.2 + 200 × 0.15 + 50 × 0.1", tiered.Calculation);
        // min_max greift nicht (186,50 liegt zwischen 30 und 5000); credit 10 % von 186,50 = 18,65.
        Assert.DoesNotContain(result.Lines, l => l.RuleKey == "plan.min_max");
        Assert.Equal(-18.65m, result.Lines.Single(l => l.RuleKey == "plan.credit").Net);
        Assert.Equal(167.85m, result.Net);
        // Steuer je Position (20 %): 9,80 + 18,50 + 9,00 − 3,73 = 33,57.
        Assert.Equal(33.57m, result.Tax);
        Assert.Equal(201.42m, result.Gross);
        Assert.Equal(result.Lines.Sum(l => l.Net), result.Net);
        Assert.Equal(result.Lines.Sum(l => l.Tax), result.Tax);
        // Provision der Ebene darüber: 20 % von 167,85 = 33,57; kein Teil der Tenant-Rechnung.
        Assert.NotNull(result.RevenueShare);
        Assert.Equal(33.57m, result.RevenueShare!.Net);
        Assert.Equal(0m, result.WouldHaveBeen);
    }

    [Fact]
    public void Rundung_je_Position_nicht_erst_in_der_Summe()
    {
        // 3 × 0,3333 = 0,9999 → 1,00 je Position; zwei Positionen ergeben 2,00, nicht round(1,9998) = 2,00 zufällig gleich, deshalb dritte Probe mit 0,005-Grenze.
        var plan = new PricePlan("rundung", [
            new("a", PriceModel.PerUnit, 1, "Kern", "a", Amount: 0.3333m),
            new("b", PriceModel.PerUnit, 2, "Kern", "b", Amount: 0.0050m),
        ]);
        var result = PricingEngine.Evaluate(plan, [new("Kern", "a", 3m, 0m), new("Kern", "b", 1m, 0m)], September);
        Assert.Equal(1.00m, result.Lines[0].Net);
        // kaufmännisch: 0,005 → 0,01 (nicht Banker's Rounding auf 0,00).
        Assert.Equal(0.01m, result.Lines[1].Net);
        Assert.Equal(1.01m, result.Net);
    }

    [Fact]
    public void Mindestbetrag_und_Deckel_wirken_als_Modifikator_auf_das_Planergebnis()
    {
        var plan = new PricePlan("min", [
            new("kern.active", PriceModel.PerUnit, 20, "Kern", MeteringMetrics.ActiveMonth, Amount: 2.5000m),
            new("plan.min_max", PriceModel.MinMax, 100, Minimum: 30.0000m, Maximum: 40.0000m),
        ]);
        var low = PricingEngine.Evaluate(plan, [new("Kern", MeteringMetrics.ActiveMonth, 2m, 0m)], September);
        Assert.Equal(25.00m, low.Lines[1].Net);
        Assert.Equal(30.00m, low.Net);

        var high = PricingEngine.Evaluate(plan, [new("Kern", MeteringMetrics.ActiveMonth, 100m, 0m)], September);
        Assert.Equal(-210.00m, high.Lines[1].Net);
        Assert.Equal(40.00m, high.Net);
    }

    [Fact]
    public void Buchung_am_16_eines_Monats_mit_30_Tagen_halbiert_die_Flatrate_Kuendigung_am_10_ergibt_den_vollen_Monat()
    {
        var plan = new PricePlan("flat", [new("m3.flat", PriceModel.Flat, 10, "M3", MeteringMetrics.TenantMonth, Amount: 29.0000m)]);
        // Entitlements liefert für die Buchung am 16.09. die Monatsmenge 15/30 = 0,5; für ein am 10. gekündigtes Modul bleibt die Menge 1.
        var booked = PricingEngine.Evaluate(plan, [new("M3", MeteringMetrics.TenantMonth, 0.5m, 0m)], September);
        Assert.Equal(14.50m, booked.Net);
        Assert.Equal("0.5 × 29", booked.Lines[0].Calculation);

        var cancelled = PricingEngine.Evaluate(plan, [new("M3", MeteringMetrics.TenantMonth, 1m, 0m)], September);
        Assert.Equal(29.00m, cancelled.Net);
    }

    [Fact]
    public void Testphase_erfasst_Mengen_bewertet_sie_nicht_und_nennt_den_Vergleichswert()
    {
        var quantities = new List<MeteredQuantity>
        {
            new("M1", MeteringMetrics.TenantMonth, 0m, 0m),
            new("M1", MeteringMetrics.ParticipantDay, 0m, 40m),
            new("Kern", MeteringMetrics.ActiveMonth, 4m, 0m),
        };
        var result = PricingEngine.Evaluate(Beispielplan, quantities, September);

        var trial = result.Lines.Single(l => l.RuleKey == "m1.days");
        Assert.False(trial.Rated);
        Assert.Equal(0m, trial.Net);
        Assert.Equal(40m, trial.Quantity);
        // „In der Testphase wären das X Euro gewesen“: 40 × 0,20 = 8,00.
        Assert.Equal(8.00m, result.WouldHaveBeen);
        // Bewertet bleibt nur der Kern (10,00), der Mindestbetrag hebt auf 30,00, der Rabatt zieht 3,00 ab.
        Assert.Equal(27.00m, result.Net);
    }

    [Fact]
    public void Weitere_Modelle_volume_package_step_und_one_time_rechnen_nachvollziehbar()
    {
        Assert.Equal(30.00m, PricingEngine.Amount(new("v", PriceModel.Volume, 1, Tiers: [new(50m, 0.2000m), new(250m, 0.1500m), new(null, 0.1000m)]), 300m).Net);
        Assert.Equal(15.00m, PricingEngine.Amount(new("v", PriceModel.Volume, 1, Tiers: [new(50m, 0.2000m), new(250m, 0.1500m), new(null, 0.1000m)]), 100m).Net);
        Assert.Equal(120.00m, PricingEngine.Amount(new("p", PriceModel.Package, 1, Amount: 100.0000m, Included: 100m, OveragePrice: 2.0000m), 110m).Net);
        Assert.Equal(100.00m, PricingEngine.Amount(new("p", PriceModel.Package, 1, Amount: 100.0000m, Included: 100m, OveragePrice: 2.0000m), 80m).Net);
        Assert.Equal(60.00m, PricingEngine.Amount(new("s", PriceModel.Step, 1, Amount: 20.0000m, BlockSize: 50m), 101m).Net);
        Assert.Equal(250.00m, PricingEngine.Amount(new("o", PriceModel.OneTime, 1, Amount: 250.0000m), 1m).Net);
    }

    [Fact]
    public void Preisplan_ist_als_JSON_verlustfrei_gespeichert_und_der_Listenpreisplan_enthaelt_die_sechs_Modelle()
    {
        var roundtrip = PricePlan.FromJson(Beispielplan.ToJson());
        Assert.Equal(Beispielplan.Rules.Count, roundtrip.Rules.Count);
        Assert.Equal(Beispielplan.Rules[2].Tiers![1].UnitPrice, roundtrip.Rules[2].Tiers![1].UnitPrice);
        var models = ListPrices.Default.Rules.Select(r => r.Model).ToHashSet();
        Assert.Superset(new HashSet<PriceModel> { PriceModel.Flat, PriceModel.PerUnit, PriceModel.Tiered, PriceModel.MinMax, PriceModel.Credit, PriceModel.RevenueShare }, models);
    }
}
