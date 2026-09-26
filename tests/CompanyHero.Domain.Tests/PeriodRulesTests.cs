using CompanyHero.Modules.Metering.Domain;
using CompanyHero.Platform.Tenancy;

namespace CompanyHero.Domain.Tests;

/// <summary>Metering 2.2, 2.3 (A-069): Versiegelung am dritten Kalendertag 03:00 Tenant-Zeit, Salzvernichtung nach sieben Tagen, Slots je Periode nicht verkettbar.</summary>
public sealed class PeriodRulesTests
{
    private static readonly TimeZoneInfo Wien = TenantTimeZone.Resolve("Europe/Vienna");

    [Fact]
    public void Versiegelung_am_dritten_Kalendertag_des_Folgemonats_um_drei_Uhr_Tenant_Zeit()
    {
        // September 2026: Versiegelung 03.10.2026 03:00 Wien (Sommerzeit, UTC+2) = 01:00 UTC.
        Assert.Equal(new DateTimeOffset(2026, 10, 3, 1, 0, 0, TimeSpan.Zero), PeriodRules.SealingDueAt("2026-09", Wien));
        Assert.False(PeriodRules.IsSealingDue("2026-09", new DateTimeOffset(2026, 10, 3, 0, 59, 0, TimeSpan.Zero), Wien));
        Assert.True(PeriodRules.IsSealingDue("2026-09", new DateTimeOffset(2026, 10, 3, 1, 0, 0, TimeSpan.Zero), Wien));
        Assert.Equal("2026-10", PeriodRules.Next("2026-09"));
        Assert.Equal(30, PeriodRules.DaysIn("2026-09"));
        Assert.True(PeriodRules.IsValid("2026-12"));
        Assert.False(PeriodRules.IsValid("2026-13"));
    }

    [Fact]
    public void Salz_wird_sieben_Tage_nach_der_Versiegelung_vernichtet_und_nur_nach_Versiegelung()
    {
        var sealedAt = new DateTimeOffset(2026, 10, 3, 1, 0, 0, TimeSpan.Zero);
        Assert.False(PeriodRules.IsSaltDestructionDue(sealedAt, sealedAt.AddDays(6)));
        Assert.True(PeriodRules.IsSaltDestructionDue(sealedAt, sealedAt.AddDays(7)));

        var period = BillingPeriod.Open(TenantId.New(), "2026-09", sealedAt.AddMonths(-1), new byte[48]);
        Assert.Throws<InvalidOperationException>(() => period.DestroySalt(sealedAt));
        period.Seal(sealedAt);
        Assert.Throws<InvalidOperationException>(() => period.Seal(sealedAt));
        period.DestroySalt(sealedAt.AddDays(7));
        Assert.Null(period.ProtectedSalt);
        Assert.True(period.SaltDestroyed);
    }

    [Fact]
    public void Slot_ist_innerhalb_der_Periode_stabil_und_ueber_Perioden_nicht_verkettbar()
    {
        var person = PersonId.New();
        var salzSeptember = PeriodRules.NewSalt();
        var salzOktober = PeriodRules.NewSalt();

        var a = PeriodRules.Slot(salzSeptember, person);
        var b = PeriodRules.Slot(salzSeptember, person);
        var c = PeriodRules.Slot(salzOktober, person);

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.StartsWith("slot:", a, StringComparison.Ordinal);
        Assert.Equal(45, a.Length);
        Assert.DoesNotContain(person.Value.ToString("D"), a, StringComparison.Ordinal);
        Assert.NotEqual(a, PeriodRules.Slot(salzSeptember, PersonId.New()));
        Assert.Throws<ArgumentException>(() => PeriodRules.Slot(new byte[8], person));
    }

    [Fact]
    public void Personennahe_Metriken_liefern_nur_Summen()
    {
        Assert.True(MeteringMetrics.IsPersonal(MeteringMetrics.ActiveMonth));
        Assert.True(MeteringMetrics.IsPersonal(MeteringMetrics.ParticipantDay));
        Assert.False(MeteringMetrics.IsPersonal(MeteringMetrics.KioskDeviceMonth));
        Assert.False(MeteringMetrics.IsPersonal(MeteringMetrics.NotificationSent));
        Assert.Equal("metrik.member_active_month", MeteringMetrics.DefinitionKey(MeteringMetrics.ActiveMonth));
    }
}
