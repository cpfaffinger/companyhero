using CompanyHero.Platform.Tenancy;

namespace CompanyHero.Modules.Organisation.Domain;

/// <summary>Organisationstyp (Organisation 1.1): genau ein Operator als Wurzel, Partner und Tenants darunter.</summary>
public enum OrganisationType
{
    Operator = 1,
    Partner = 2,
    Tenant = 3,
}

/// <summary>Lebenszyklus (Organisation 1.3).</summary>
public enum OrganisationState
{
    /// <summary>Eingerichtet: angelegt, noch keine Mitglieder.</summary>
    Provisioned = 1,
    Active = 2,
    Suspended = 3,
    Terminated = 4,
    Deleted = 5,
}

/// <summary>
/// Plattformdatum (Backend 5.3): Operator, Partner und Tenants. Für Tenants ist die Organisationskennung zugleich die
/// <c>tenant_id</c> aller tenantbezogenen Zeilen. Höchstens drei Ebenen (Organisation 1.1). Tenants tragen ihre Zeitzone
/// (Organisation 1.2) und die Zeitpunkte ihres Lebenszyklus (1.3): Sperre mit Grund, Kündigung zum Monatsende mit 90 Tagen
/// Lesezugriff, Löschung.
/// </summary>
public sealed class Organisation
{
    /// <summary>Lesender Zugriff für Tenant-Admin und Einsichtsrolle nach der Kündigung (Organisation 1.3).</summary>
    public static TimeSpan ReadOnlyAfterTermination { get; } = TimeSpan.FromDays(90);

    private Organisation(Guid id, OrganisationType type, Guid? parentId, string displayName, OrganisationState state, DateTimeOffset createdAt)
    {
        Id = id;
        Type = type;
        ParentId = parentId;
        DisplayName = displayName;
        State = state;
        CreatedAt = createdAt;
    }

    public Guid Id { get; }

    public OrganisationType Type { get; }

    public Guid? ParentId { get; private set; }

    public string DisplayName { get; private set; }

    public OrganisationState State { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    /// <summary>IANA-Zeitzone des Tenants (Organisation 1.2), Voreinstellung Europe/Vienna.</summary>
    public string TimeZoneId { get; private set; } = TenantTimeZone.DefaultId;

    public DateTimeOffset? SuspendedAt { get; private set; }

    /// <summary>Grund der Sperre (Zahlungsverzug, Missbrauch); Tenant-Admins sehen ihn, Mitglieder nur einen neutralen Hinweis.</summary>
    public string? SuspensionReason { get; private set; }

    public DateTimeOffset? TerminatedAt { get; private set; }

    /// <summary>Kündigung wirksam zum Monatsende; bis dahin regulärer Betrieb (Organisation 1.3).</summary>
    public DateTimeOffset? TerminationEffectiveAt { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public static Organisation CreateOperator(string displayName, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        return new Organisation(Guid.CreateVersion7(), OrganisationType.Operator, null, displayName.Trim(), OrganisationState.Active, now);
    }

    /// <summary>Partner unter dem Operator (Organisation 1.1: ein Partner hat keine Sub-Partner).</summary>
    public static Organisation CreatePartner(string displayName, Organisation parent, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(parent);
        if (parent.Type != OrganisationType.Operator)
        {
            throw new OrganisationHierarchyException("Ein Partner liegt direkt unter dem Operator.");
        }

        return new Organisation(Guid.CreateVersion7(), OrganisationType.Partner, parent.Id, displayName.Trim(), OrganisationState.Active, now);
    }

    /// <summary>Tenant unter Operator oder Partner; beginnt im Zustand „Eingerichtet“ (Organisation 1.3, 1.4).</summary>
    public static Organisation CreateTenant(string displayName, Organisation parent, DateTimeOffset now, string? timeZoneId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(parent);
        if (parent.Type is not (OrganisationType.Operator or OrganisationType.Partner))
        {
            throw new OrganisationHierarchyException("Ein Tenant liegt unter dem Operator oder einem Partner; eine vierte Ebene gibt es nicht.");
        }

        var tenant = new Organisation(Guid.CreateVersion7(), OrganisationType.Tenant, parent.Id, displayName.Trim(), OrganisationState.Provisioned, now);
        if (timeZoneId is not null)
        {
            tenant.SetTimeZone(timeZoneId);
        }

        return tenant;
    }

    /// <summary>Wird aktiv, sobald der erste Tenant-Admin den Rollencode eingelöst hat (Organisation 1.3).</summary>
    public void ActivateOnFirstTenantAdmin()
    {
        if (Type != OrganisationType.Tenant)
        {
            throw new OrganisationHierarchyException("Nur ein Tenant wird durch seinen ersten Tenant-Admin aktiv.");
        }

        if (State == OrganisationState.Provisioned)
        {
            State = OrganisationState.Active;
        }
    }

    public void SetTimeZone(string timeZoneId)
    {
        if (!TenantTimeZone.IsKnown(timeZoneId))
        {
            throw new ArgumentException("Unbekannte Zeitzone.", nameof(timeZoneId));
        }

        TimeZoneId = timeZoneId;
    }

    /// <summary>Sperre durch Operator oder Partner mit Grund (Organisation 1.3); Daten bleiben vollständig erhalten.</summary>
    public void Suspend(string reason, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        RequireTenant();
        if (State is not (OrganisationState.Active or OrganisationState.Provisioned))
        {
            throw new OrganisationHierarchyException("Nur ein aktiver Tenant kann gesperrt werden.");
        }

        State = OrganisationState.Suspended;
        SuspendedAt = now;
        SuspensionReason = reason.Trim();
    }

    /// <summary>Entsperren stellt den Zustand ohne Datenverlust wieder her.</summary>
    public void Unsuspend()
    {
        if (State != OrganisationState.Suspended)
        {
            throw new OrganisationHierarchyException("Der Tenant ist nicht gesperrt.");
        }

        State = OrganisationState.Active;
        SuspendedAt = null;
        SuspensionReason = null;
    }

    /// <summary>Kündigung zum Monatsende in der Zeitzone des Tenants; danach 90 Tage Lesezugriff, dann Löschung (Organisation 1.3).</summary>
    public void Terminate(DateTimeOffset now)
    {
        RequireTenant();
        if (State is OrganisationState.Terminated or OrganisationState.Deleted)
        {
            throw new OrganisationHierarchyException("Der Tenant ist bereits gekündigt.");
        }

        State = OrganisationState.Terminated;
        TerminatedAt = now;
        TerminationEffectiveAt = TenantTimeZone.EndOfMonth(now, TenantTimeZone.Resolve(TimeZoneId));
    }

    public void MarkDeleted(DateTimeOffset now)
    {
        if (State != OrganisationState.Terminated || !IsDueForDeletionAt(now))
        {
            throw new OrganisationHierarchyException("Löschung erst nach Ablauf der Lesefrist.");
        }

        State = OrganisationState.Deleted;
        DeletedAt = now;
    }

    /// <summary>Ende der Lesefrist nach der Kündigung.</summary>
    public DateTimeOffset? ReadOnlyUntil => TerminationEffectiveAt?.Add(ReadOnlyAfterTermination);

    public bool IsDueForDeletionAt(DateTimeOffset now) => State == OrganisationState.Terminated && ReadOnlyUntil is { } until && now >= until;

    /// <summary>Mitglieder und Tenant-Rollen haben nur in einem aktiven Tenant Zugang; nach der Kündigung bis zum Wirksamkeitstermin (Organisation 1.3).</summary>
    public bool GrantsMemberAccessAt(DateTimeOffset now) =>
        Type == OrganisationType.Tenant && (State == OrganisationState.Active || (State == OrganisationState.Terminated && now < TerminationEffectiveAt));

    /// <summary>Nach dem Wirksamkeitstermin: 90 Tage lesender Zugriff für Tenant-Admin und Einsichtsrolle zum Export.</summary>
    public bool GrantsReadOnlyAccessAt(DateTimeOffset now) =>
        Type == OrganisationType.Tenant && State == OrganisationState.Terminated && now >= TerminationEffectiveAt && now < ReadOnlyUntil;

    /// <summary>Neutraler Grund für die Ablehnung einer Mitgliedersitzung (Zugang 5, Organisation 1.3).</summary>
    public string? AccessDeniedReasonAt(DateTimeOffset now) => State switch
    {
        OrganisationState.Suspended => "tenant_suspended",
        OrganisationState.Terminated when now >= TerminationEffectiveAt => "tenant_terminated",
        OrganisationState.Deleted => "tenant_terminated",
        _ => null,
    };

    private void RequireTenant()
    {
        if (Type != OrganisationType.Tenant)
        {
            throw new OrganisationHierarchyException("Nur Tenants haben einen Lebenszyklus mit Sperre und Kündigung.");
        }
    }
}

public sealed class OrganisationHierarchyException : InvalidOperationException
{
    public OrganisationHierarchyException()
    {
    }

    public OrganisationHierarchyException(string message)
        : base(message)
    {
    }

    public OrganisationHierarchyException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
