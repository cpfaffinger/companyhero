using CompanyHero.Modules.Privacy.Domain;
using CompanyHero.Platform.Tenancy;

namespace CompanyHero.Modules.Privacy.Application;

/// <summary>
/// Querschnitt „Sichtbarkeit und k-Anonymität“ (Domänenkarte 3), Eigentümer Datenschutz und Nachweis. Jede
/// Leseoperation mit Personenbezug läuft durch diese Regel; die lesende Person und ihre Rollen kommen aus dem Kontext.
/// </summary>
public interface IVisibilityRule
{
    /// <summary>Darf die handelnde Person des Kontexts individuelle Aktivitätswerte der betroffenen Person lesen?</summary>
    Task<bool> MayReadIndividualValuesAsync(PersonId subject, CancellationToken cancellationToken);
}

/// <summary>Die ausdrückliche Wahl der Sichtbarkeitsstufe (Pflichtschritt im Beitritt, Zugang 2.2) und ihre Änderung.</summary>
public interface IVisibilityChoice
{
    Task ChooseAsync(PersonId personId, VisibilityLevel level, CancellationToken cancellationToken);

    Task<VisibilityLevel?> GetAsync(PersonId personId, CancellationToken cancellationToken);
}
