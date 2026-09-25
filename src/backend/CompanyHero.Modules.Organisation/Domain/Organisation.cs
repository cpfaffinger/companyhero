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
/// <c>tenant_id</c> aller tenantbezogenen Zeilen. Höchstens drei Ebenen (Organisation 1.1).
/// </summary>
public sealed class Organisation
{
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

    public Guid? ParentId { get; }

    public string DisplayName { get; private set; }

    public OrganisationState State { get; private set; }

    public DateTimeOffset CreatedAt { get; }

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
    public static Organisation CreateTenant(string displayName, Organisation parent, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(parent);
        if (parent.Type is not (OrganisationType.Operator or OrganisationType.Partner))
        {
            throw new OrganisationHierarchyException("Ein Tenant liegt unter dem Operator oder einem Partner; eine vierte Ebene gibt es nicht.");
        }

        return new Organisation(Guid.CreateVersion7(), OrganisationType.Tenant, parent.Id, displayName.Trim(), OrganisationState.Provisioned, now);
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

    /// <summary>Mitglieder und Tenant-Rollen haben nur in einem aktiven Tenant Zugang (Organisation 1.3).</summary>
    public bool GrantsMemberAccess => Type == OrganisationType.Tenant && State == OrganisationState.Active;
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
