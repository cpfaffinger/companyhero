using CompanyHero.Platform.Tenancy;

namespace CompanyHero.Modules.Progress.Domain;

/// <summary>Quelle einer Handlung (Fortschritt 2.1): selbst, Plattform, automatisch.</summary>
public enum ActivitySource
{
    Self = 1,
    Platform = 2,
    Automatic = 3,
}

/// <summary>
/// Genau ein Aktivitätsereignis je Handlung einer Person (Fortschritt 2.1, A-044). Individuelle Aktivitätswerte sind
/// Gesundheitsdaten (Datenschutz 1.2) und verlassen die Person nur über die Sichtbarkeitsregel.
/// </summary>
public sealed class ActivityEvent : ITenantOwned
{
    private ActivityEvent(TenantId tenantId, Guid id, PersonId personId, string kind, ActivitySource source, DateTimeOffset occurredAt)
    {
        TenantId = tenantId;
        Id = id;
        PersonId = personId;
        Kind = kind;
        Source = source;
        OccurredAt = occurredAt;
    }

    public TenantId TenantId { get; }

    public Guid Id { get; }

    public PersonId PersonId { get; }

    /// <summary>Aktivitätsart, etwa „check_in“ oder „challenge_contribution“.</summary>
    public string Kind { get; }

    public ActivitySource Source { get; }

    public DateTimeOffset OccurredAt { get; }

    public static ActivityEvent Record(TenantId tenantId, PersonId personId, string kind, ActivitySource source, DateTimeOffset occurredAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        return new ActivityEvent(tenantId, Guid.CreateVersion7(), personId, kind.Trim(), source, occurredAt);
    }
}
