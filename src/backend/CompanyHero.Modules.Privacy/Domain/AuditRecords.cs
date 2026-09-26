using CompanyHero.Platform.Tenancy;

namespace CompanyHero.Modules.Privacy.Domain;

/// <summary>
/// Eintrag des Prüfprotokolls (Datenschutz 5.1, 6.2): append-only, 3 Jahre, ohne Personenbezug auf Mitglieder. Die handelnde
/// Person steht als Kennung (Pseudonym) mit ihren Rollen; der Bezug ist eine Objektkennung, nie ein Anzeigename.
/// </summary>
public sealed class AuditRecord : ITenantOwned
{
    private AuditRecord(TenantId tenantId, Guid id, DateTimeOffset occurredAt, PersonId? actor, string actorRoles, string action, string? subjectRef, string? detail)
    {
        TenantId = tenantId;
        Id = id;
        OccurredAt = occurredAt;
        Actor = actor;
        ActorRoles = actorRoles;
        Action = action;
        SubjectRef = subjectRef;
        Detail = detail;
    }

    public TenantId TenantId { get; }

    public Guid Id { get; }

    public DateTimeOffset OccurredAt { get; }

    public PersonId? Actor { get; }

    /// <summary>Rollen der handelnden Person zum Zeitpunkt der Handlung, kommagetrennt.</summary>
    public string ActorRoles { get; }

    public string Action { get; }

    public string? SubjectRef { get; }

    public string? Detail { get; }

    public static AuditRecord Create(TenantId tenantId, DateTimeOffset occurredAt, PersonId? actor, IEnumerable<string> roles, string action, string? subjectRef, string? detail)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        return new AuditRecord(tenantId, Guid.CreateVersion7(), occurredAt, actor, string.Join(',', roles.Order(StringComparer.Ordinal)), action, subjectRef, detail);
    }
}

/// <summary>Eintrag des Sicherheitsprotokolls (Datenschutz 5.1): Anmeldungen und Fehlversuche, pseudonymisiert, 12 Monate.</summary>
public sealed class SecurityRecord : ITenantOwned
{
    private SecurityRecord(TenantId tenantId, Guid id, DateTimeOffset occurredAt, string eventType, bool success, string? pseudonym, string? detail)
    {
        TenantId = tenantId;
        Id = id;
        OccurredAt = occurredAt;
        EventType = eventType;
        Success = success;
        Pseudonym = pseudonym;
        Detail = detail;
    }

    public TenantId TenantId { get; }

    public Guid Id { get; }

    public DateTimeOffset OccurredAt { get; }

    public string EventType { get; }

    public bool Success { get; }

    /// <summary>Hash- oder Objektkennung; nie Kennung im Klartext, nie Name oder E-Mail.</summary>
    public string? Pseudonym { get; }

    public string? Detail { get; }

    public static SecurityRecord Create(TenantId tenantId, DateTimeOffset occurredAt, string eventType, bool success, string? pseudonym, string? detail)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        return new SecurityRecord(tenantId, Guid.CreateVersion7(), occurredAt, eventType, success, pseudonym, detail);
    }
}
