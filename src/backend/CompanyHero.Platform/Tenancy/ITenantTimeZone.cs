namespace CompanyHero.Platform.Tenancy;

/// <summary>
/// Zeitzone des Tenants des aktiven Kontexts (Organisation 1.2: je Tenant, Voreinstellung Europe/Vienna). Eigentümer ist
/// Organisation; alle Domänen, die Kalendertage, Perioden oder Nachfristen berechnen, lesen sie über diese Schnittstelle
/// (Challenges 3, Fortschritt 3.1, Benachrichtigungen 4.2). Im Plattformkontext gilt die Voreinstellung.
/// </summary>
public interface ITenantTimeZone
{
    Task<TimeZoneInfo> GetAsync(CancellationToken cancellationToken);
}
