using CompanyHero.Platform.Tenancy;

namespace CompanyHero.Platform.Privacy;

/// <summary>Sichtbarkeitsstufen der ausdrücklichen Wahl im Beitritt (Datenschutz 3.1, A-022), ohne Voreinstellung.</summary>
public enum VisibilityChoice
{
    OnlyMe = 1,
    Team = 2,
    Company = 3,
}

/// <summary>
/// Querschnitt „Sichtbarkeit“ (Domänenkarte 3): Eigentümer ist Datenschutz und Nachweis. Der Beitritt (Identity) verlangt
/// die Wahl als Pflichtschritt (Zugang 2.2) und schreibt sie über diese Schnittstelle in derselben Transaktion wie Person
/// und Mitgliedschaft.
/// </summary>
public interface IVisibilityChoiceRecorder
{
    Task RecordAsync(PersonId personId, VisibilityChoice choice, CancellationToken cancellationToken);
}
