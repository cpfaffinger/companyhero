using CompanyHero.Platform.Tenancy;

namespace CompanyHero.Modules.Entitlements.Domain;

/// <summary>
/// Tagesgenaue Proratierung (Metering 4.5, 6.5; A-072): Tage im Zustand aktiv oder auslaufend geteilt durch Tage des Monats,
/// auch für Flatrates. Entitlements meldet Metering die Monatsmenge <c>tenant.month</c> je Modul: bei Buchung mitten im Monat
/// den Anteil ab dem Buchungstag, danach je Monat 1. Testtage werden nicht proratiert und nicht bewertet.
/// </summary>
public static class Proration
{
    /// <summary>Anteil des Monats ab dem Kalendertag von <paramref name="from"/> in der Zone (einschließlich), vier Nachkommastellen.</summary>
    public static decimal RemainingMonthFraction(DateTimeOffset from, TimeZoneInfo zone)
    {
        var day = TenantTimeZone.DayOf(from, zone);
        var daysInMonth = DateTime.DaysInMonth(day.Year, day.Month);
        var remaining = daysInMonth - day.Day + 1;
        return decimal.Round(remaining / (decimal)daysInMonth, 4, MidpointRounding.AwayFromZero);
    }

    /// <summary>Beginn der Abrechnungsperiode eines Zeitpunkts in der Zone.</summary>
    public static DateTimeOffset StartOfPeriod(DateTimeOffset instant, TimeZoneInfo zone)
    {
        var day = TenantTimeZone.DayOf(instant, zone);
        return TenantTimeZone.StartOfDay(new DateOnly(day.Year, day.Month, 1), zone);
    }

    /// <summary>Kündigungstermin: Monatsende in der Zeitzone des Tenants (Entitlements 4.3).</summary>
    public static DateTimeOffset CancellationEffectiveAt(DateTimeOffset now, TimeZoneInfo zone) => TenantTimeZone.EndOfMonth(now, zone);
}
