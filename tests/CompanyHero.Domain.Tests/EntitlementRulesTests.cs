using CompanyHero.Modules.Entitlements.Domain;
using Katalog = CompanyHero.Modules.Entitlements.Domain.ModuleCatalog;
using CompanyHero.Platform.Entitlements;
using CompanyHero.Platform.Tenancy;

namespace CompanyHero.Domain.Tests;

/// <summary>Entitlements 2 bis 4 (A-064 bis A-066): Bündelvorschlag, Kaskade, Rücknahme, eine Testphase, Navigation nur aus aktiven Modulen, Proratierung.</summary>
public sealed class EntitlementRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 10, 0, 0, TimeSpan.Zero);
    private static readonly TimeZoneInfo Wien = TenantTimeZone.Resolve("Europe/Vienna");

    [Fact]
    public void Buchung_von_M2_ohne_M1_ergibt_den_Buendelvorschlag_M1_und_M2()
    {
        var active = new HashSet<string>(StringComparer.Ordinal) { ModuleCodes.Core };
        Assert.Equal(["M1"], Katalog.MissingDependencies("M2", active));
        Assert.Equal(["M1", "M2"], Katalog.SuggestBundle("M2", active));
        Assert.Empty(Katalog.MissingDependencies("M2", new HashSet<string>(StringComparer.Ordinal) { "M1" }));
    }

    [Fact]
    public void Kuendigung_von_M1_erfasst_abhaengige_aktive_Module()
    {
        var active = new HashSet<string>(StringComparer.Ordinal) { "M1", "M2", "M3" };
        Assert.Equal(["M2"], Katalog.Dependents("M1", active));
        Assert.Empty(Katalog.Dependents("M3", active));
    }

    [Fact]
    public void Navigation_nur_aus_aktiven_Modulen_Kern_zeigt_zwei_Bereiche()
    {
        Assert.Equal(["start", "ich"], Katalog.NavigationAreas(new HashSet<string>(StringComparer.Ordinal) { ModuleCodes.Core }));
        Assert.Equal(["start", "challenges", "ich"], Katalog.NavigationAreas(new HashSet<string>(StringComparer.Ordinal) { ModuleCodes.Core, "M1", "M2" }));
        Assert.Equal(["start", "challenges", "entdecken", "firma", "ich"], Katalog.NavigationAreas(new HashSet<string>(StringComparer.Ordinal) { "M1", "M5", "M10" }));
        Assert.True(Katalog.NavigationAreas(new HashSet<string>(Katalog.Modules.Select(m => m.Code), StringComparer.Ordinal)).Count <= 5);
    }

    [Fact]
    public void Testphase_hoechstens_einmal_je_Modul_Kuendigung_in_der_Testphase_kostenfrei_zum_Testende()
    {
        var e = Entitlement.Create(TenantId.New(), "M1");
        e.StartTrial(Now, EntitlementSource.TenantAdmin);
        Assert.Equal(EntitlementState.Trial, e.State);
        Assert.True(e.IsTrialAt(Now.AddDays(10)));
        Assert.Equal(Now.AddDays(30), e.TrialUntil);

        // Kündigung in der Testphase: inaktiv zum Testende, keine Bewertung.
        e.Cancel(Now.AddDays(5), Proration.CancellationEffectiveAt(Now.AddDays(5), Wien));
        Assert.Equal(EntitlementState.Trial, e.State);
        Assert.Equal(e.TrialUntil, e.ActiveUntil);
        Assert.Equal(EntitlementTransition.None, e.Advance(Now.AddDays(29)));
        Assert.Equal(EntitlementTransition.TrialCancelled, e.Advance(Now.AddDays(30)));
        Assert.Equal(EntitlementState.Inactive, e.State);
        Assert.Equal(ModuleAccess.ExportOnly, e.AccessAt(Now.AddDays(31)));

        // Zweite Testphase desselben Moduls abgelehnt; erneute Buchung ohne Testphase möglich.
        var second = Assert.Throws<EntitlementException>(() => e.StartTrial(Now.AddDays(40), EntitlementSource.TenantAdmin));
        Assert.Equal("trial_used", second.Reason);
        e.Book(Now.AddDays(40), EntitlementSource.TenantAdmin);
        Assert.Equal(EntitlementState.Active, e.State);
    }

    [Fact]
    public void Testende_aktiviert_das_Modul_mit_Bewertung_ab_Testende()
    {
        var e = Entitlement.Create(TenantId.New(), "M3");
        e.StartTrial(Now, EntitlementSource.TenantAdmin);
        Assert.Equal(EntitlementTransition.TrialEnded, e.Advance(Now.AddDays(30).AddMinutes(1)));
        Assert.Equal(EntitlementState.Active, e.State);
        Assert.Equal(Now.AddDays(30), e.ActiveFrom);
        Assert.Null(e.TrialUntil);
        Assert.False(e.IsTrialAt(Now.AddDays(31)));
    }

    [Fact]
    public void Kuendigung_zum_Monatsende_voller_Zugang_bis_dahin_Ruecknahme_stellt_wieder_her_danach_Exportfrist()
    {
        var e = Entitlement.Create(TenantId.New(), "M1");
        e.Book(new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero), EntitlementSource.Preset);
        var cancelAt = new DateTimeOffset(2026, 9, 10, 9, 0, 0, TimeSpan.Zero);
        var effective = Proration.CancellationEffectiveAt(cancelAt, Wien);
        Assert.Equal(new DateTimeOffset(2026, 9, 30, 22, 0, 0, TimeSpan.Zero), effective);

        e.Cancel(cancelAt, effective);
        Assert.Equal(EntitlementState.Expiring, e.State);
        Assert.Equal(ModuleAccess.Active, e.AccessAt(new DateTimeOffset(2026, 9, 30, 21, 59, 0, TimeSpan.Zero)));
        Assert.Equal(ModuleAccess.ExportOnly, e.AccessAt(effective));

        e.RevokeCancellation(cancelAt.AddDays(1));
        Assert.Equal(EntitlementState.Active, e.State);
        Assert.Null(e.ActiveUntil);

        e.Cancel(cancelAt.AddDays(2), effective);
        Assert.Equal(EntitlementTransition.Expired, e.Advance(effective));
        Assert.Equal(EntitlementState.Inactive, e.State);
        Assert.Equal(ModuleAccess.ExportOnly, e.AccessAt(effective.AddDays(89)));
        Assert.Equal(ModuleAccess.None, e.AccessAt(effective.AddDays(91)));
        Assert.Throws<EntitlementException>(() => e.RevokeCancellation(effective.AddDays(1)));
    }

    [Fact]
    public void Proratierung_tagesgenau_Buchung_am_16_eines_Monats_mit_30_Tagen_ergibt_die_Haelfte()
    {
        Assert.Equal(0.5m, Proration.RemainingMonthFraction(new DateTimeOffset(2026, 9, 16, 10, 0, 0, TimeSpan.Zero), Wien));
        Assert.Equal(1m, Proration.RemainingMonthFraction(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.FromHours(2)), Wien));
        // 30.09. 23:30 UTC ist in Wien bereits der 1. Oktober: voller Oktober.
        Assert.Equal(1m, Proration.RemainingMonthFraction(new DateTimeOffset(2026, 9, 30, 23, 30, 0, TimeSpan.Zero), Wien));
        Assert.Equal(0.0323m, Proration.RemainingMonthFraction(new DateTimeOffset(2026, 10, 31, 10, 0, 0, TimeSpan.Zero), Wien));
    }

    [Fact]
    public void Unbekanntes_Modul_und_Grenzwerte()
    {
        Assert.Throws<EntitlementException>(() => Entitlement.Create(TenantId.New(), "M99"));
        Assert.Equal(20, TenantLimit.DefaultFor(TenantLimitNames.KioskDevices));
        Assert.Throws<EntitlementException>(() => TenantLimit.DefaultFor("unbekannt"));
    }
}
