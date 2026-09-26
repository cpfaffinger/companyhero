using CompanyHero.Modules.Organisation.Domain;
using CompanyHero.Platform.Tenancy;

namespace CompanyHero.Modules.Organisation.Application;

/// <summary>Ein Mitglied aus Sicht der Verwaltung: Kennung, Beitritt, Rollen. Keine Aktivitäts-, Fortschritts- oder Anmeldedaten (Datenschutz 3.4).</summary>
public sealed record MemberRecord(PersonId PersonId, DateTimeOffset JoinedAt, IReadOnlyList<string> Roles);

/// <summary>
/// Öffentliche Anwendungsfunktionen der Domäne Organisation (Domänenkarte 2). Andere Module verwenden ausschließlich
/// diese Schnittstelle, nie die Tabellen des Schemas <c>organisation</c>.
/// </summary>
public interface IOrganisationDirectory
{
    /// <summary>Plattformkontext: legt den Operator an, falls er fehlt, und liefert seine Kennung.</summary>
    Task<Guid> EnsureOperatorAsync(string displayName, CancellationToken cancellationToken);

    /// <summary>Plattformkontext (Operator oder Partner-Admin, Organisation 1.4): neuer Tenant im Zustand „Eingerichtet“.</summary>
    Task<TenantId> CreateTenantAsync(string displayName, Guid parentId, CancellationToken cancellationToken);

    /// <summary>Tenant-Kontext: Mitgliedschaft der Person im aktiven Tenant.</summary>
    Task AddMemberAsync(PersonId personId, CancellationToken cancellationToken);

    /// <summary>Tenant-Kontext: Rolle hinzufügen; der erste Tenant-Admin aktiviert den Tenant (Organisation 1.3).</summary>
    Task AssignRoleAsync(PersonId personId, string role, CancellationToken cancellationToken);

    /// <summary>Tenant-Kontext: Rollen einer Person; leer, wenn sie kein aktives Mitglied ist.</summary>
    Task<IReadOnlySet<string>> GetRolesAsync(PersonId personId, CancellationToken cancellationToken);

    /// <summary>Tenant-Kontext: Mitgliederliste ohne Aktivitätsdaten und ohne Sortierung, die Aktivität verrät (Organisation 3.2).</summary>
    Task<IReadOnlyList<MemberRecord>> ListMembersAsync(CancellationToken cancellationToken);

    /// <summary>Tenant-Kontext: Anzeigename und Zustand des aktiven Tenants.</summary>
    Task<(string DisplayName, OrganisationState State)?> GetCurrentTenantAsync(CancellationToken cancellationToken);

    /// <summary>Plattformkontext: Anzeigename und Zustand eines beliebigen Tenants (Tenant-Vorschau beim Beitritt, Zugang 2.1).</summary>
    Task<(string DisplayName, OrganisationState State)?> GetTenantAsync(TenantId tenantId, CancellationToken cancellationToken);

    /// <summary>Plattformkontext: alle aktiven Tenants, etwa für tenantübergreifende Zeitpläne.</summary>
    Task<IReadOnlyList<TenantId>> ListActiveTenantsAsync(CancellationToken cancellationToken);

    /// <summary>Tenant-Kontext: Rolle entziehen (Organisation 3.2); der letzte Tenant-Admin kann seine Rolle nicht abgeben (Organisation 4.1).</summary>
    Task RemoveRoleAsync(PersonId personId, string role, CancellationToken cancellationToken);

    /// <summary>Tenant-Kontext: Austritt der Person (Organisation 3.1, Zugang 8); ihre Rollen enden mit der Mitgliedschaft.</summary>
    Task LeaveAsync(PersonId personId, CancellationToken cancellationToken);
}
