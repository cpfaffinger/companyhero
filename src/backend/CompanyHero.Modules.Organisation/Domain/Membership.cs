using CompanyHero.Platform.Tenancy;

namespace CompanyHero.Modules.Organisation.Domain;

/// <summary>Zustände der Mitgliedschaft (Organisation 3.1).</summary>
public enum MembershipState
{
    Active = 1,
    Left = 2,
    Removed = 3,
}

/// <summary>Mitgliedschaft einer Person in genau einem Tenant (Zugang 1). Personen werden nur über ihre Kennung referenziert.</summary>
public sealed class Membership : ITenantOwned
{
    private readonly List<RoleAssignment> _roles = [];

    private Membership(TenantId tenantId, PersonId personId, MembershipState state, DateTimeOffset joinedAt)
    {
        TenantId = tenantId;
        PersonId = personId;
        State = state;
        JoinedAt = joinedAt;
    }

    public TenantId TenantId { get; }

    public PersonId PersonId { get; }

    public MembershipState State { get; private set; }

    public DateTimeOffset JoinedAt { get; }

    public IReadOnlyList<RoleAssignment> Roles => _roles;

    public DateTimeOffset? LeftAt { get; private set; }

    public static Membership Join(TenantId tenantId, PersonId personId, DateTimeOffset joinedAt) =>
        new(tenantId, personId, MembershipState.Active, joinedAt);

    /// <summary>Austritt durch die Person (Organisation 3.1, A-019): Mitgliedschaft endet, Rollen enden mit ihr.</summary>
    public void Leave(DateTimeOffset leftAt)
    {
        State = MembershipState.Left;
        LeftAt = leftAt;
        _roles.Clear();
    }

    /// <summary>Entfernen durch einen Tenant-Admin (Organisation 3.1): wirkt wie Austritt, protokolliert.</summary>
    public void Remove(DateTimeOffset removedAt)
    {
        State = MembershipState.Removed;
        LeftAt = removedAt;
        _roles.Clear();
    }

    /// <summary>Rolle entziehen; liefert die entfernte Zuweisung oder <c>null</c>.</summary>
    public RoleAssignment? Revoke(string role)
    {
        var existing = _roles.Find(r => string.Equals(r.Role, role, StringComparison.Ordinal));
        if (existing is not null)
        {
            _roles.Remove(existing);
        }

        return existing;
    }

    /// <summary>Eine Person kann mehrere Rollen ihrer Organisation halten (Organisation 4.1); jede höchstens einmal.</summary>
    public RoleAssignment Assign(string role, DateTimeOffset assignedAt)
    {
        if (!Role.IsTenantRole(role))
        {
            throw new ArgumentException($"'{role}' ist keine Tenant-Rolle.", nameof(role));
        }

        var existing = _roles.Find(r => string.Equals(r.Role, role, StringComparison.Ordinal));
        if (existing is not null)
        {
            return existing;
        }

        var assignment = new RoleAssignment(TenantId, Guid.CreateVersion7(), PersonId, role, assignedAt);
        _roles.Add(assignment);
        return assignment;
    }
}

/// <summary>Rollenzuweisung nach Rollenkatalog; zusammengesetzter Fremdschlüssel (tenant_id, person_id) auf die Mitgliedschaft (A-011).</summary>
public sealed class RoleAssignment : ITenantOwned
{
    internal RoleAssignment(TenantId tenantId, Guid id, PersonId personId, string role, DateTimeOffset assignedAt)
    {
        TenantId = tenantId;
        Id = id;
        PersonId = personId;
        Role = role;
        AssignedAt = assignedAt;
    }

    public TenantId TenantId { get; }

    public Guid Id { get; }

    public PersonId PersonId { get; }

    public string Role { get; }

    public DateTimeOffset AssignedAt { get; }
}
