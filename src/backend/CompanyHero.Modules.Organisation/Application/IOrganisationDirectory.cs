using CompanyHero.Modules.Organisation.Domain;
using CompanyHero.Platform.Tenancy;

namespace CompanyHero.Modules.Organisation.Application;

/// <summary>Ein Mitglied aus Sicht der Verwaltung: Kennung, Beitritt, Rollen, Gruppen. Keine Aktivitäts-, Fortschritts- oder Anmeldedaten (Datenschutz 3.4).</summary>
public sealed record MemberRecord(PersonId PersonId, DateTimeOffset JoinedAt, IReadOnlyList<string> Roles, IReadOnlyList<Guid> Groups);

/// <summary>Stammdaten und Zustand des aktiven Tenants (Organisation 1.2, 1.3).</summary>
public sealed record TenantRecord(TenantId TenantId, string DisplayName, OrganisationState State, string TimeZoneId, DateTimeOffset? TerminationEffectiveAt, DateTimeOffset? ReadOnlyUntil, string? SuspensionReason);

public sealed record GroupRecord(Guid GroupId, string Name, bool Archived, int? Headcount);

/// <summary>Dimension mit ihren Gruppen; Sollstärken nur für Rollen mit Leserecht (Organisation 4.2).</summary>
public sealed record DimensionRecord(Guid DimensionId, string Name, int Position, bool Active, IReadOnlyList<GroupRecord> Groups);

/// <summary>Zugehörigkeit einer Person je Dimension; <c>Rechoice</c>, wenn die Gruppe archiviert wurde und eine Neuwahl ansteht (Organisation 2.1).</summary>
public sealed record GroupChoiceRecord(Guid DimensionId, Guid? GroupId, bool Rechoice);

/// <summary>
/// Öffentliche Anwendungsfunktionen der Domäne Organisation (Domänenkarte 2). Andere Module verwenden ausschließlich
/// diese Schnittstelle, nie die Tabellen des Schemas <c>organisation</c>.
/// </summary>
public interface IOrganisationDirectory
{
    /// <summary>Plattformkontext: legt den Operator an, falls er fehlt, und liefert seine Kennung.</summary>
    Task<Guid> EnsureOperatorAsync(string displayName, CancellationToken cancellationToken);

    /// <summary>Plattformkontext (Operator, Organisation 1.1): Partner direkt unter dem Operator.</summary>
    Task<Guid> CreatePartnerAsync(string displayName, CancellationToken cancellationToken);

    /// <summary>Plattformkontext (Operator oder Partner-Admin, Organisation 1.4): neuer Tenant im Zustand „Eingerichtet“.</summary>
    Task<TenantId> CreateTenantAsync(string displayName, Guid parentId, CancellationToken cancellationToken, string? timeZoneId = null);

    /// <summary>Plattformkontext: Sperre mit Grund (Organisation 1.3); Mitgliedersitzungen enden beim nächsten Request.</summary>
    Task SuspendTenantAsync(TenantId tenantId, string reason, CancellationToken cancellationToken);

    Task UnsuspendTenantAsync(TenantId tenantId, CancellationToken cancellationToken);

    /// <summary>Plattformkontext (Operator, Partner): Kündigung zum Monatsende.</summary>
    Task TerminateTenantAsync(TenantId tenantId, CancellationToken cancellationToken);

    /// <summary>Plattformkontext: Tenants, deren Lesefrist abgelaufen ist (Organisation 1.3, Datenschutz 5.4).</summary>
    Task<IReadOnlyList<TenantId>> ListTenantsDueForDeletionAsync(CancellationToken cancellationToken);

    /// <summary>Plattformkontext: Endzustand „Gelöscht“ nach der Löschung der tenantbezogenen Daten.</summary>
    Task MarkTenantDeletedAsync(TenantId tenantId, CancellationToken cancellationToken);

    /// <summary>Tenant-Kontext: Mitgliedschaft der Person im aktiven Tenant mit Gruppenwahl je Dimension (Zugang 2.2 Schritt 4).</summary>
    Task AddMemberAsync(PersonId personId, CancellationToken cancellationToken, IReadOnlyDictionary<Guid, Guid>? groupChoices = null);

    /// <summary>Tenant-Kontext: Rolle hinzufügen; der erste Tenant-Admin aktiviert den Tenant (Organisation 1.3).</summary>
    Task AssignRoleAsync(PersonId personId, string role, CancellationToken cancellationToken);

    /// <summary>Tenant-Kontext: Rollen einer Person; leer, wenn sie kein aktives Mitglied ist.</summary>
    Task<IReadOnlySet<string>> GetRolesAsync(PersonId personId, CancellationToken cancellationToken);

    /// <summary>Tenant-Kontext: Mitgliederliste ohne Aktivitätsdaten und ohne Sortierung, die Aktivität verrät (Organisation 3.2).</summary>
    Task<IReadOnlyList<MemberRecord>> ListMembersAsync(CancellationToken cancellationToken);

    /// <summary>Tenant-Kontext: Kennungen aller aktiven Mitglieder (Empfängerkreise, Aggregate).</summary>
    Task<IReadOnlyList<PersonId>> ListActiveMemberIdsAsync(CancellationToken cancellationToken);

    /// <summary>Tenant-Kontext: Anzeigename und Zustand des aktiven Tenants.</summary>
    Task<(string DisplayName, OrganisationState State)?> GetCurrentTenantAsync(CancellationToken cancellationToken);

    /// <summary>Tenant-Kontext: Stammdaten und Lebenszyklus des aktiven Tenants.</summary>
    Task<TenantRecord?> GetCurrentTenantRecordAsync(CancellationToken cancellationToken);

    /// <summary>Tenant-Kontext (Tenant-Admin): Zeitzone des Tenants (Organisation 1.2).</summary>
    Task UpdateTimeZoneAsync(string timeZoneId, CancellationToken cancellationToken);

    /// <summary>Tenant-Kontext (Tenant-Admin): Kündigung des eigenen Tenants zum Monatsende (Organisation 1.3).</summary>
    Task TerminateCurrentTenantAsync(CancellationToken cancellationToken);

    /// <summary>Plattformkontext: Anzeigename und Zustand eines beliebigen Tenants (Tenant-Vorschau beim Beitritt, Zugang 2.1).</summary>
    Task<(string DisplayName, OrganisationState State)?> GetTenantAsync(TenantId tenantId, CancellationToken cancellationToken);

    /// <summary>Plattformkontext: neutraler Grund, warum ein Tenant derzeit keinen Mitgliederzugang gewährt, oder <c>null</c>.</summary>
    Task<string?> GetAccessDeniedReasonAsync(TenantId tenantId, CancellationToken cancellationToken);

    /// <summary>Plattformkontext: alle aktiven Tenants, etwa für tenantübergreifende Zeitpläne.</summary>
    Task<IReadOnlyList<TenantId>> ListActiveTenantsAsync(CancellationToken cancellationToken);

    /// <summary>Tenant-Kontext: Rolle entziehen (Organisation 3.2); der letzte Tenant-Admin kann seine Rolle nicht abgeben (Organisation 4.1).</summary>
    Task RemoveRoleAsync(PersonId personId, string role, CancellationToken cancellationToken);

    /// <summary>Tenant-Kontext: Austritt der Person (Organisation 3.1, Zugang 8); ihre Rollen enden mit der Mitgliedschaft.</summary>
    Task LeaveAsync(PersonId personId, CancellationToken cancellationToken);

    /// <summary>Tenant-Kontext (Tenant-Admin): Mitglied entfernen; wirkt wie Austritt (Organisation 3.1).</summary>
    Task RemoveMemberAsync(PersonId personId, CancellationToken cancellationToken);

    /// <summary>Tenant-Kontext: Dimensionen mit Gruppen; beim ersten Aufruf entstehen die drei voreingestellten Dimensionen (Organisation 2.1).</summary>
    Task<IReadOnlyList<DimensionRecord>> ListDimensionsAsync(bool includeHeadcount, CancellationToken cancellationToken);

    Task RenameDimensionAsync(Guid dimensionId, string name, bool active, CancellationToken cancellationToken);

    Task<Guid> CreateGroupAsync(Guid dimensionId, string name, int? headcount, CancellationToken cancellationToken);

    /// <summary>Archivieren: nicht mehr wählbar; zugeordnete Personen wählen beim nächsten Öffnen neu (Organisation 2.1).</summary>
    Task ArchiveGroupAsync(Guid groupId, CancellationToken cancellationToken);

    /// <summary>Sollstärke mit Stichtag je Tenant (<c>null</c>) oder Gruppe; vergangene Quoten bleiben unverändert (Organisation 2.2).</summary>
    Task SetHeadcountAsync(Guid? groupId, DateOnly effectiveFrom, int count, CancellationToken cancellationToken);

    /// <summary>Gültige Sollstärke zu einem Stichtag; <c>null</c>, wenn keine gemeldet ist.</summary>
    Task<int?> GetHeadcountAtAsync(Guid? groupId, DateOnly day, CancellationToken cancellationToken);

    /// <summary>Tenant-Kontext: eigene Zuordnung je Dimension, mit Hinweis auf Neuwahl nach Archivierung.</summary>
    Task<IReadOnlyList<GroupChoiceRecord>> GetGroupsOfAsync(PersonId personId, CancellationToken cancellationToken);

    /// <summary>Tenant-Kontext: Gruppen mehrerer Personen (Sichtbarkeit „Mein Team“, Geltungsbereiche).</summary>
    Task<IReadOnlyDictionary<PersonId, IReadOnlyList<Guid>>> GetGroupsOfManyAsync(IEnumerable<PersonId> personIds, CancellationToken cancellationToken);

    /// <summary>Tenant-Kontext: die Person wählt ihre Gruppe einer Dimension selbst (Organisation 2.1); <c>null</c> hebt die Zuordnung auf.</summary>
    Task ChooseGroupAsync(PersonId personId, Guid dimensionId, Guid? groupId, CancellationToken cancellationToken);

    /// <summary>Tenant-Kontext: aktive Mitglieder einer Gruppe.</summary>
    Task<IReadOnlyList<PersonId>> ListGroupMembersAsync(Guid groupId, CancellationToken cancellationToken);
}
