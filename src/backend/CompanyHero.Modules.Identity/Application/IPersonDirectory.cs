using CompanyHero.Platform.Tenancy;

namespace CompanyHero.Modules.Identity.Application;

public sealed record PersonRecord(PersonId PersonId, string DisplayName);

/// <summary>Öffentliche Anwendungsfunktionen der Domäne Identität und Zugang für Personen des aktiven Tenants.</summary>
public interface IPersonDirectory
{
    Task<PersonId> CreatePersonAsync(string displayName, CancellationToken cancellationToken);

    Task<PersonRecord?> GetAsync(PersonId personId, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<PersonId, string>> GetDisplayNamesAsync(IEnumerable<PersonId> personIds, CancellationToken cancellationToken);
}
