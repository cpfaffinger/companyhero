using System.Text.Json.Nodes;
using CompanyHero.Platform.Tenancy;

namespace CompanyHero.Platform.Privacy;

/// <summary>Feste Bezeichner des Querschnitts „Datenschutz“ für Jobs (Datenschutz 5.2, A-024).</summary>
public static class PersonalData
{
    /// <summary>Jobtyp der Löschfolgen eines Austritts; Referenz ist die Personenkennung. Eigentümer ist Datenschutz und Nachweis.</summary>
    public const string ErasureJobType = "privacy.person.erase";
}

/// <summary>
/// Ein Abschnitt der Auskunft (Datenschutz 6.4, A-025): jedes Modul liefert seine Daten mit Personenbezug zur Person des
/// Kontexts als JSON; die Domäne Datenschutz fügt die Abschnitte zum Selbstexport zusammen. Keine Daten anderer Personen.
/// </summary>
public interface IPersonalDataExporter
{
    /// <summary>Abschnittsname im Export, etwa <c>challenges</c>.</summary>
    string Section { get; }

    Task<JsonNode> ExportAsync(PersonId personId, CancellationToken cancellationToken);
}

/// <summary>
/// Löschfolgen des Austritts je Modul (Datenschutz 5.2): gelöscht, anonymisiert oder mit nicht rückführbarer Kennung
/// behalten, wie die Fristentabelle es je Kategorie festlegt. Läuft als Job im Tenant-Kontext, idempotent.
/// </summary>
public interface IPersonalDataEraser
{
    Task EraseAsync(PersonId personId, CancellationToken cancellationToken);
}
