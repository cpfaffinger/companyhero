using CompanyHero.Platform.Tenancy;

namespace CompanyHero.Modules.Progress.Domain;

/// <summary>Quelle einer Handlung (Fortschritt 2.1): selbst, Plattform, automatisch.</summary>
public enum ActivitySource
{
    Self = 1,
    Platform = 2,
    Automatic = 3,
}

/// <summary>Aktivitätsarten des Durchstichs (Fortschritt 2.1, 2.2): jede Art hat einen Tagesdeckel.</summary>
public static class ActivityKinds
{
    public const string CheckIn = "check_in";
    public const string ChallengeContribution = "challenge_contribution";
    public const string RecognitionGiven = "recognition_given";
    public const string FeedPost = "feed_post";
}

/// <summary>
/// Genau ein Aktivitätsereignis je Handlung einer Person (Fortschritt 2.1, A-044). Individuelle Aktivitätswerte sind
/// Gesundheitsdaten (Datenschutz 1.2) und verlassen die Person nur über die Sichtbarkeitsregel. Ein Gegenereignis nimmt die
/// Wirkung einer korrigierten Handlung zurück; es ist eine Korrektur, keine Strafe.
/// </summary>
public sealed class ActivityEvent : ITenantOwned
{
    private ActivityEvent(TenantId tenantId, Guid id, PersonId personId, string kind, ActivitySource source, DateTimeOffset occurredAt, DateOnly day, int points, Guid? reversalOf)
    {
        TenantId = tenantId;
        Id = id;
        PersonId = personId;
        Kind = kind;
        Source = source;
        OccurredAt = occurredAt;
        Day = day;
        Points = points;
        ReversalOf = reversalOf;
    }

    public TenantId TenantId { get; }

    public Guid Id { get; }

    public PersonId PersonId { get; }

    /// <summary>Aktivitätsart, etwa „check_in“ oder „challenge_contribution“.</summary>
    public string Kind { get; }

    public ActivitySource Source { get; }

    public DateTimeOffset OccurredAt { get; }

    /// <summary>Kalendertag der Handlung in der Tenant-Zeitzone (Fortschritt 2.1).</summary>
    public DateOnly Day { get; }

    /// <summary>Gewertete Punkte: 10 oder 0 (Deckel); bei Gegenereignissen negativ (Fortschritt 2.2).</summary>
    public int Points { get; }

    /// <summary>Gesetzt bei Gegenereignissen: das zurückgenommene Ereignis.</summary>
    public Guid? ReversalOf { get; }

    public bool IsReversal => ReversalOf is not null;

    public bool Reversed { get; private set; }

    public static ActivityEvent Record(TenantId tenantId, PersonId personId, string kind, ActivitySource source, DateTimeOffset occurredAt, DateOnly day, int points)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        return new ActivityEvent(tenantId, Guid.CreateVersion7(), personId, kind.Trim(), source, occurredAt, day, points, null);
    }

    /// <summary>Gegenereignis (Fortschritt 2.1): nimmt die Punkte des Auslösers zurück; Abzeichen und Stufen bleiben.</summary>
    public ActivityEvent Reverse(DateTimeOffset now)
    {
        if (IsReversal || Reversed)
        {
            throw new InvalidOperationException("Ein Ereignis wird höchstens einmal zurückgenommen.");
        }

        Reversed = true;
        return new ActivityEvent(TenantId, Guid.CreateVersion7(), PersonId, Kind, Source, now, Day, -Points, Id);
    }
}
