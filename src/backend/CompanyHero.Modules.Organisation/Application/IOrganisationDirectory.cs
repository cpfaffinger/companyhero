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
}
