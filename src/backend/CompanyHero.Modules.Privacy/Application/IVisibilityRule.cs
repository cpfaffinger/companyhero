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

    /// <summary>
    /// Die Teilmenge der Personen, die für die handelnde Person des Kontexts sichtbar sind: für individuelle Werte gilt Regel 1,
    /// für Anzeigename und Beiträge der Kreis der Sichtbarkeitsstufe (Feed 2.4). Sammelkarten entscheiden Aufrufer über <see cref="AggregateRule"/>.
    /// </summary>
    Task<IReadOnlySet<PersonId>> FilterVisibleAsync(IEnumerable<PersonId> subjects, VisibilityPurpose purpose, CancellationToken cancellationToken);
}

/// <summary>Die ausdrückliche Wahl der Sichtbarkeitsstufe (Pflichtschritt im Beitritt, Zugang 2.2) und ihre Änderung.</summary>
public interface IVisibilityChoice
{
    Task ChooseAsync(PersonId personId, VisibilityLevel level, CancellationToken cancellationToken);

    Task<VisibilityLevel?> GetAsync(PersonId personId, CancellationToken cancellationToken);

    /// <summary>Stufen mehrerer Personen (Empfängerkreise, Feed).</summary>
    Task<IReadOnlyDictionary<PersonId, VisibilityLevel>> GetManyAsync(IEnumerable<PersonId> personIds, CancellationToken cancellationToken);
}
