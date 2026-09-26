using CompanyHero.Platform.Tenancy;

namespace CompanyHero.Modules.Identity.Application;

public sealed record PersonRecord(PersonId PersonId, string DisplayName);

/// <summary>Öffentliche Anwendungsfunktionen der Domäne Identität und Zugang für Personen des aktiven Tenants.</summary>
public interface IPersonDirectory
{
    Task<PersonId> CreatePersonAsync(string displayName, CancellationToken cancellationToken);

    Task<PersonRecord?> GetAsync(PersonId personId, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<PersonId, string>> GetDisplayNamesAsync(IEnumerable<PersonId> personIds, CancellationToken cancellationToken);

    /// <summary>Hinterlegte E-Mail-Adresse der Person für Benachrichtigungen (Benachrichtigungen 2), sonst <c>null</c>.</summary>
    Task<string?> GetEmailAsync(PersonId personId, CancellationToken cancellationToken);

    /// <summary>Aktive Personen ohne Sitzungskontakt seit dem Stichtag (verwaiste Personen, Datenschutz 5.3).</summary>
    Task<IReadOnlyList<PersonId>> ListOrphanedAsync(DateTimeOffset since, CancellationToken cancellationToken);
}
