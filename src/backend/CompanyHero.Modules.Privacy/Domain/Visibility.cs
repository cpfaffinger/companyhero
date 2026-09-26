using CompanyHero.Platform.Tenancy;

namespace CompanyHero.Modules.Privacy.Domain;

/// <summary>Sichtbarkeitsstufen (Datenschutz 3.1, A-022), von restriktiv nach offen.</summary>
public enum VisibilityLevel
{
    /// <summary>Nur für mich: nichts ist für andere sichtbar; Beiträge zählen anonym in Kollektivziele.</summary>
    OnlyMe = 1,

    /// <summary>Mein Team: Anzeigename und Beiträge für die eigenen Gruppen sichtbar.</summary>
    Team = 2,

    /// <summary>Ganze Firma: zusätzlich firmenweite Ranglisten und Feed-Sichtbarkeit.</summary>
    Company = 3,
}

/// <summary>Die ausdrücklich gewählte Sichtbarkeitsstufe einer Person; ohne Voreinstellung (Datenschutz 3.1).</summary>
public sealed class VisibilitySetting : ITenantOwned
{
    private VisibilitySetting(TenantId tenantId, PersonId personId, VisibilityLevel level, DateTimeOffset chosenAt)
    {
        TenantId = tenantId;
        PersonId = personId;
        Level = level;
        ChosenAt = chosenAt;
    }

    public TenantId TenantId { get; }

    public PersonId PersonId { get; }

    public VisibilityLevel Level { get; private set; }

    public DateTimeOffset ChosenAt { get; private set; }

    public static VisibilitySetting Choose(TenantId tenantId, PersonId personId, VisibilityLevel level, DateTimeOffset chosenAt) =>
        new(tenantId, personId, level, chosenAt);

    /// <summary>Änderungen wirken sofort und rückwirkend auf alle Anzeigen (Datenschutz 3.1).</summary>
    public void Change(VisibilityLevel level, DateTimeOffset changedAt)
    {
        Level = level;
        ChosenAt = changedAt;
    }
}

/// <summary>Wofür gelesen wird: individuelle Aktivitätswerte (Regel 1 gilt) oder Inhalte im Kreis der Sichtbarkeitsstufe (Feed, Anzeigename).</summary>
public enum VisibilityPurpose
{
    /// <summary>Individuelle Aktivitäts- und Fortschrittswerte: Funktionsrollen des Arbeitgebers sehen sie nie (A-021 Regel 1).</summary>
    IndividualValues = 1,

    /// <summary>Anzeigename und Beiträge im Kreis, den die Person gewählt hat (Feed 2.4, Datenschutz 3.1).</summary>
    Presence = 2,
}

/// <summary>
/// Die zentrale Leseregel für individuelle Werte und Sichtbarkeitskreise (Datenschutz 3.3, A-021, A-022). Reine Fachregel ohne
/// Infrastruktur; alle lesenden Domänen verwenden sie über <c>IVisibilityRule</c>, es gibt keine Filterlogik je Feature.
/// </summary>
public static class VisibilityRule
{
    /// <param name="reader">Die lesende Person.</param>
    /// <param name="readerHoldsEmployerRole">Hält die lesende Person eine Funktionsrolle des Arbeitgebers (Organisation 4.1)?</param>
    /// <param name="subject">Die Person, deren individuelle Werte gelesen werden sollen.</param>
    /// <param name="subjectLevel">Gewählte Stufe der betroffenen Person; <c>null</c>, wenn sie noch keine gewählt hat.</param>
    /// <param name="shareGroup">Teilen beide eine Gruppe (für „Mein Team“)?</param>
    public static bool MayReadIndividualValues(PersonId reader, bool readerHoldsEmployerRole, PersonId subject, VisibilityLevel? subjectLevel, bool shareGroup) =>
        IsVisible(reader, readerHoldsEmployerRole, subject, subjectLevel, shareGroup, VisibilityPurpose.IndividualValues);

    public static bool IsVisible(PersonId reader, bool readerHoldsEmployerRole, PersonId subject, VisibilityLevel? subjectLevel, bool shareGroup, VisibilityPurpose purpose)
    {
        // Eigene Daten sieht die Person immer (Datenschutz 6.4).
        if (reader == subject)
        {
            return true;
        }

        // Regel 1: Kein Arbeitgeber sieht individuelle Aktivitätswerte, auch nicht der Tenant-Admin, unabhängig von der Stufe.
        if (purpose == VisibilityPurpose.IndividualValues && readerHoldsEmployerRole)
        {
            return false;
        }

        // Die restriktivere Einstellung gewinnt; ohne Wahl gilt die restriktivste Stufe.
        return subjectLevel switch
        {
            VisibilityLevel.Company => true,
            VisibilityLevel.Team => shareGroup,
            _ => false,
        };
    }
}

/// <summary>Mindestzahl und Ranglistenschwelle (Datenschutz 4, A-023): reine Fachregel für alle Aggregate mit Personenbezug.</summary>
public static class AggregateRule
{
    /// <summary>Aggregate erst ab fünf Personen mit Beitrag; darunter nichts, nicht gerundet, nicht ungenau (A-021 Regel 2).</summary>
    public const int MinimumPersons = 5;

    /// <summary>Ranglisten zusätzlich erst ab 40 Prozent der aktivierten Personen (Datenschutz 4.3); im Durchstich gibt es keine Rangliste.</summary>
    public const int RankingMinimumPercent = 40;

    public static bool MayPublish(int personsWithContribution) => personsWithContribution >= MinimumPersons;

    /// <summary>Sammelkarten und Sammelereignisse mit Personenbezug ebenfalls erst ab fünf (Feed 2.5, Fortschritt 4.2).</summary>
    public static bool MayAggregatePersons(int persons) => persons >= MinimumPersons;

    public static bool MayRenderRanking(int personsWithContribution, int activatedPersons) =>
        MayPublish(personsWithContribution) && activatedPersons > 0 && personsWithContribution * 100 >= activatedPersons * RankingMinimumPercent;
}
