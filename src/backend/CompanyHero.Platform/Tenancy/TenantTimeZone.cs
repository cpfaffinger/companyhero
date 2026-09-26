namespace CompanyHero.Platform.Tenancy;

/// <summary>
/// Kalenderrechnung in der Zeitzone des Tenants (Organisation 1.2: Voreinstellung <c>Europe/Vienna</c>). Die Zone je
/// Tenant liefert <see cref="ITenantTimeZone"/>; die Überladungen ohne Zone verwenden die Voreinstellung und dienen
/// Plattformabläufen ohne Tenant. Kalendertage, Perioden und Nachfristen werden in dieser Zone berechnet
/// (Challenges 3, Fortschritt 3.1, Metering 2.2).
/// </summary>
public static class TenantTimeZone
{
    public const string DefaultId = "Europe/Vienna";

    private static readonly Lazy<TimeZoneInfo> DefaultZone = new(() => Resolve(DefaultId));

    public static TimeZoneInfo Default => DefaultZone.Value;

    /// <summary>Zone zur IANA-Kennung; unbekannte Kennungen fallen auf die Voreinstellung zurück, damit kein Tenant ohne Kalender bleibt.</summary>
    public static TimeZoneInfo Resolve(string? id)
    {
        if (!string.IsNullOrWhiteSpace(id) && TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone))
        {
            return zone;
        }

        if (string.Equals(id, DefaultId, StringComparison.Ordinal) || id is null)
        {
            // Windows ohne ICU (InvariantGlobalization): Windows-Kennung derselben Zone.
            return TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time");
        }

        return Default;
    }

    public static bool IsKnown(string id) => !string.IsNullOrWhiteSpace(id) && TimeZoneInfo.TryFindSystemTimeZoneById(id, out _);

    /// <summary>Kalendertag eines Zeitpunkts in der Tenant-Zeitzone.</summary>
    public static DateOnly DayOf(DateTimeOffset instant) => DayOf(instant, Default);

    public static DateOnly DayOf(DateTimeOffset instant, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);
    }

    /// <summary>Beginn eines Kalendertags der Tenant-Zeitzone als UTC-Zeitpunkt.</summary>
    public static DateTimeOffset StartOfDay(DateOnly day) => StartOfDay(day, Default);

    public static DateTimeOffset StartOfDay(DateOnly day, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var local = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
    }

    /// <summary>Lokale Uhrzeit eines Zeitpunkts in der Zone (Ruhezeiten, Benachrichtigungen 4.2).</summary>
    public static TimeOnly TimeOf(DateTimeOffset instant, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        return TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);
    }

    /// <summary>Nächster Zeitpunkt, an dem die lokale Uhrzeit <paramref name="time"/> in der Zone erreicht wird (heute oder morgen).</summary>
    public static DateTimeOffset NextLocalTime(DateTimeOffset after, TimeOnly time, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var day = DayOf(after, zone);
        for (var i = 0; i < 3; i++)
        {
            var local = day.AddDays(i).ToDateTime(time, DateTimeKind.Unspecified);
            var candidate = new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
            if (candidate > after)
            {
                return candidate;
            }
        }

        return after;
    }

    /// <summary>Abrechnungsperiode <c>YYYY-MM</c> eines Zeitpunkts in der Tenant-Zeitzone (Metering 2.1).</summary>
    public static string PeriodOf(DateTimeOffset instant) => PeriodOf(instant, Default);

    public static string PeriodOf(DateTimeOffset instant, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var local = TimeZoneInfo.ConvertTime(instant, zone);
        return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{local.Year:0000}-{local.Month:00}");
    }

    /// <summary>Erster Tag des Folgemonats in der Zone (Kündigung zum Monatsende, Organisation 1.3).</summary>
    public static DateTimeOffset EndOfMonth(DateTimeOffset instant, TimeZoneInfo zone)
    {
        var day = DayOf(instant, zone);
        var first = new DateOnly(day.Year, day.Month, 1).AddMonths(1);
        return StartOfDay(first, zone);
    }
}
