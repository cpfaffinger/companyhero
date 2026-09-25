namespace CompanyHero.Platform.Tenancy;

/// <summary>
/// Zeitzone des Tenants (Organisation 2: Voreinstellung <c>Europe/Vienna</c>). Bis die Tenant-Einrichtung im Fachpfad
/// die Zeitzone je Tenant führt, gilt die Voreinstellung für alle Tenants. Kalendertage, Perioden und Nachfristen
/// werden in dieser Zone berechnet (Challenges 3, Metering 2.2).
/// </summary>
public static class TenantTimeZone
{
    public const string DefaultId = "Europe/Vienna";

    private static readonly Lazy<TimeZoneInfo> DefaultZone = new(Resolve);

    public static TimeZoneInfo Default => DefaultZone.Value;

    /// <summary>Kalendertag eines Zeitpunkts in der Tenant-Zeitzone.</summary>
    public static DateOnly DayOf(DateTimeOffset instant) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, Default).DateTime);

    /// <summary>Beginn eines Kalendertags der Tenant-Zeitzone als UTC-Zeitpunkt.</summary>
    public static DateTimeOffset StartOfDay(DateOnly day)
    {
        var local = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, Default.GetUtcOffset(local)).ToUniversalTime();
    }

    /// <summary>Abrechnungsperiode <c>YYYY-MM</c> eines Zeitpunkts in der Tenant-Zeitzone (Metering 2.1).</summary>
    public static string PeriodOf(DateTimeOffset instant)
    {
        var local = TimeZoneInfo.ConvertTime(instant, Default);
        return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{local.Year:0000}-{local.Month:00}");
    }

    private static TimeZoneInfo Resolve()
    {
        if (TimeZoneInfo.TryFindSystemTimeZoneById(DefaultId, out var zone))
        {
            return zone;
        }

        // Windows ohne ICU (InvariantGlobalization): Windows-Kennung derselben Zone.
        return TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time");
    }
}
