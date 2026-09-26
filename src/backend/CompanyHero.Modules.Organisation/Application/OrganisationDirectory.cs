using CompanyHero.Modules.Organisation.Domain;
using CompanyHero.Modules.Organisation.Infrastructure;
using CompanyHero.Platform.Audit;
using CompanyHero.Platform.Data;
using CompanyHero.Platform.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace CompanyHero.Modules.Organisation.Application;

internal sealed class OrganisationDirectory(OrganisationDbContext db, IContextTransaction transaction, ITenantContextAccessor context, IAuditLog audit, TimeProvider clock) : IOrganisationDirectory
{
    public async Task<Guid> EnsureOperatorAsync(string displayName, CancellationToken cancellationToken)
    {
        RequirePlatform();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var existing = await db.Organisations.SingleOrDefaultAsync(o => o.Type == OrganisationType.Operator, cancellationToken);
        if (existing is not null)
        {
            return existing.Id;
        }

        var operatorOrganisation = Domain.Organisation.CreateOperator(displayName, clock.GetUtcNow());
        db.Organisations.Add(operatorOrganisation);
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return operatorOrganisation.Id;
    }

    public async Task<Guid> CreatePartnerAsync(string displayName, CancellationToken cancellationToken)
    {
        RequirePlatform();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var operatorOrganisation = await db.Organisations.SingleOrDefaultAsync(o => o.Type == OrganisationType.Operator, cancellationToken)
            ?? throw new OrganisationHierarchyException("Der Operator fehlt.");
        var partner = Domain.Organisation.CreatePartner(displayName, operatorOrganisation, clock.GetUtcNow());
        db.Organisations.Add(partner);
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return partner.Id;
    }

    public async Task<TenantId> CreateTenantAsync(string displayName, Guid parentId, CancellationToken cancellationToken, string? timeZoneId = null)
    {
        RequirePlatform();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var parent = await db.Organisations.SingleOrDefaultAsync(o => o.Id == parentId, cancellationToken)
            ?? throw new OrganisationHierarchyException("Übergeordnete Organisation nicht gefunden.");
        var tenant = Domain.Organisation.CreateTenant(displayName, parent, clock.GetUtcNow(), timeZoneId);
        db.Organisations.Add(tenant);
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return new TenantId(tenant.Id);
    }

    public async Task SuspendTenantAsync(TenantId tenantId, string reason, CancellationToken cancellationToken)
    {
        RequirePlatform();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var tenant = await RequireTenantAsync(tenantId, cancellationToken);
        tenant.Suspend(reason, clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    public async Task UnsuspendTenantAsync(TenantId tenantId, CancellationToken cancellationToken)
    {
        RequirePlatform();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var tenant = await RequireTenantAsync(tenantId, cancellationToken);
        tenant.Unsuspend();
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    public async Task TerminateTenantAsync(TenantId tenantId, CancellationToken cancellationToken)
    {
        RequirePlatform();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var tenant = await RequireTenantAsync(tenantId, cancellationToken);
        tenant.Terminate(clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TenantId>> ListTenantsDueForDeletionAsync(CancellationToken cancellationToken)
    {
        RequirePlatform();
        var now = clock.GetUtcNow();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var terminated = await db.Organisations.Where(o => o.Type == OrganisationType.Tenant && o.State == OrganisationState.Terminated).ToListAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return terminated.Where(t => t.IsDueForDeletionAt(now)).Select(t => new TenantId(t.Id)).ToList();
    }

    public async Task MarkTenantDeletedAsync(TenantId tenantId, CancellationToken cancellationToken)
    {
        RequirePlatform();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var tenant = await RequireTenantAsync(tenantId, cancellationToken);
        tenant.MarkDeleted(clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    public async Task AddMemberAsync(PersonId personId, CancellationToken cancellationToken, IReadOnlyDictionary<Guid, Guid>? groupChoices = null)
    {
        var tenantId = context.Require().RequireTenant();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var now = clock.GetUtcNow();
        db.Memberships.Add(Membership.Join(tenantId, personId, now));
        if (groupChoices is { Count: > 0 })
        {
            var dimensions = await db.Dimensions.Where(d => d.TenantId == tenantId && d.Active).ToListAsync(cancellationToken);
            foreach (var (dimensionId, groupId) in groupChoices)
            {
                if (!dimensions.Any(d => d.Id == dimensionId))
                {
                    throw new OrganisationHierarchyException("Unbekannte Dimension.");
                }

                var group = await db.Groups.SingleOrDefaultAsync(g => g.TenantId == tenantId && g.Id == groupId && g.DimensionId == dimensionId, cancellationToken)
                    ?? throw new OrganisationHierarchyException("Unbekannte Gruppe.");
                db.GroupMemberships.Add(GroupMembership.Choose(tenantId, personId, group, now));
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    public async Task AssignRoleAsync(PersonId personId, string role, CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var membership = await db.Memberships.Include(m => m.Roles)
            .SingleOrDefaultAsync(m => m.TenantId == tenantId && m.PersonId == personId, cancellationToken)
            ?? throw new InvalidOperationException("Rolle nur für Mitglieder des Tenants.");
        membership.Assign(role, clock.GetUtcNow());

        if (string.Equals(role, Role.TenantAdmin, StringComparison.Ordinal))
        {
            var tenant = await db.Organisations.SingleAsync(o => o.Id == tenantId.Value, cancellationToken);
            tenant.ActivateOnFirstTenantAdmin();
        }

        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlySet<string>> GetRolesAsync(PersonId personId, CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var roles = await db.RoleAssignments
            .Where(r => r.TenantId == tenantId && r.PersonId == personId)
            .Select(r => r.Role)
            .ToListAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return new HashSet<string>(roles, StringComparer.Ordinal);
    }

    public async Task<IReadOnlyList<MemberRecord>> ListMembersAsync(CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var members = await db.Memberships.Include(m => m.Roles)
            .Where(m => m.TenantId == tenantId && m.State == MembershipState.Active)
            .OrderBy(m => m.PersonId)
            .ToListAsync(cancellationToken);
        var groups = await db.GroupMemberships.Where(g => g.TenantId == tenantId).ToListAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        var byPerson = groups.ToLookup(g => g.PersonId, g => g.GroupId);
        return members.Select(m => new MemberRecord(m.PersonId, m.JoinedAt, m.Roles.Select(r => r.Role).Order(StringComparer.Ordinal).ToList(), byPerson[m.PersonId].Order().ToList())).ToList();
    }

    public async Task<IReadOnlyList<PersonId>> ListActiveMemberIdsAsync(CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var ids = await db.Memberships.Where(m => m.TenantId == tenantId && m.State == MembershipState.Active).OrderBy(m => m.PersonId).Select(m => m.PersonId).ToListAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return ids;
    }

    public async Task<(string DisplayName, OrganisationState State)?> GetCurrentTenantAsync(CancellationToken cancellationToken)
    {
        var record = await GetCurrentTenantRecordAsync(cancellationToken);
        return record is null ? null : (record.DisplayName, record.State);
    }

    public async Task<TenantRecord?> GetCurrentTenantRecordAsync(CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var tenant = await db.Organisations.SingleOrDefaultAsync(o => o.Id == tenantId.Value, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return tenant is null ? null : new TenantRecord(tenantId, tenant.DisplayName, tenant.State, tenant.TimeZoneId, tenant.TerminationEffectiveAt, tenant.ReadOnlyUntil, tenant.SuspensionReason);
    }

    public async Task UpdateTimeZoneAsync(string timeZoneId, CancellationToken cancellationToken)
    {
        var tenantId = RequireRole(Role.TenantAdmin);
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var tenant = await db.Organisations.SingleAsync(o => o.Id == tenantId.Value, cancellationToken);
        tenant.SetTimeZone(timeZoneId);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditEntry("organisation.tenant.time_zone", tenantId.ToString(), timeZoneId), cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    public async Task TerminateCurrentTenantAsync(CancellationToken cancellationToken)
    {
        var tenantId = RequireRole(Role.TenantAdmin);
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var tenant = await db.Organisations.SingleAsync(o => o.Id == tenantId.Value, cancellationToken);
        tenant.Terminate(clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditEntry("organisation.tenant.terminated", tenantId.ToString(), tenant.TerminationEffectiveAt?.ToString("O")), cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    public async Task<(string DisplayName, OrganisationState State)?> GetTenantAsync(TenantId tenantId, CancellationToken cancellationToken)
    {
        RequirePlatform();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var tenant = await db.Organisations.SingleOrDefaultAsync(o => o.Id == tenantId.Value && o.Type == OrganisationType.Tenant, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return tenant is null ? null : (tenant.DisplayName, tenant.State);
    }

    public async Task<string?> GetAccessDeniedReasonAsync(TenantId tenantId, CancellationToken cancellationToken)
    {
        RequirePlatform();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var tenant = await db.Organisations.SingleOrDefaultAsync(o => o.Id == tenantId.Value && o.Type == OrganisationType.Tenant, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return tenant?.AccessDeniedReasonAt(clock.GetUtcNow());
    }

    public async Task<IReadOnlyList<TenantId>> ListActiveTenantsAsync(CancellationToken cancellationToken)
    {
        RequirePlatform();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var ids = await db.Organisations
            .Where(o => o.Type == OrganisationType.Tenant && o.State == OrganisationState.Active)
            .OrderBy(o => o.Id)
            .Select(o => o.Id)
            .ToListAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return ids.Select(id => new TenantId(id)).ToList();
    }

    public async Task RemoveRoleAsync(PersonId personId, string role, CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var membership = await db.Memberships.Include(m => m.Roles)
            .SingleOrDefaultAsync(m => m.TenantId == tenantId && m.PersonId == personId, cancellationToken)
            ?? throw new InvalidOperationException("Rolle nur für Mitglieder des Tenants.");
        if (string.Equals(role, Role.TenantAdmin, StringComparison.Ordinal))
        {
            var admins = await db.RoleAssignments.CountAsync(r => r.TenantId == tenantId && r.Role == Role.TenantAdmin, cancellationToken);
            if (admins <= 1)
            {
                throw new OrganisationHierarchyException("Der letzte Tenant-Admin kann seine Rolle nicht abgeben (Organisation 4.1).");
            }
        }

        var removed = membership.Revoke(role);
        if (removed is not null)
        {
            db.RoleAssignments.Remove(removed);
        }

        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    public async Task LeaveAsync(PersonId personId, CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var membership = await db.Memberships.Include(m => m.Roles)
            .SingleOrDefaultAsync(m => m.TenantId == tenantId && m.PersonId == personId, cancellationToken)
            ?? throw new InvalidOperationException("Austritt nur für Mitglieder des Tenants.");
        db.RoleAssignments.RemoveRange(membership.Roles);
        membership.Leave(clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    public async Task RemoveMemberAsync(PersonId personId, CancellationToken cancellationToken)
    {
        var tenantId = RequireRole(Role.TenantAdmin);
        if (context.Require().PersonId == personId)
        {
            throw new OrganisationHierarchyException("Sich selbst entfernt man über den Austritt.");
        }

        await using var tx = await transaction.BeginAsync(cancellationToken);
        var membership = await db.Memberships.Include(m => m.Roles)
            .SingleOrDefaultAsync(m => m.TenantId == tenantId && m.PersonId == personId && m.State == MembershipState.Active, cancellationToken)
            ?? throw new OrganisationHierarchyException("Kein aktives Mitglied.");
        if (membership.Roles.Any(r => r.Role == Role.TenantAdmin))
        {
            var admins = await db.RoleAssignments.CountAsync(r => r.TenantId == tenantId && r.Role == Role.TenantAdmin, cancellationToken);
            if (admins <= 1)
            {
                throw new OrganisationHierarchyException("Der letzte Tenant-Admin kann nicht entfernt werden (Organisation 4.1).");
            }
        }

        db.RoleAssignments.RemoveRange(membership.Roles);
        membership.Remove(clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        // Protokoll ohne Anzeigename: Kennung des Mitglieds als Objektbezug (Organisation 3.1, Datenschutz 6.2).
        await audit.RecordAsync(new AuditEntry("organisation.member.removed", personId.ToString(), null), cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DimensionRecord>> ListDimensionsAsync(bool includeHeadcount, CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        var today = TenantTimeZone.DayOf(clock.GetUtcNow());
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var dimensions = await EnsureDimensionsAsync(tenantId, cancellationToken);
        var groups = await db.Groups.Where(g => g.TenantId == tenantId).OrderBy(g => g.Name).ToListAsync(cancellationToken);
        var headcounts = includeHeadcount ? await db.Headcounts.Where(h => h.TenantId == tenantId && h.GroupId != null).ToListAsync(cancellationToken) : [];
        await tx.CommitAsync(cancellationToken);
        return dimensions.OrderBy(d => d.Position).Select(d => new DimensionRecord(
            d.Id, d.Name, d.Position, d.Active,
            groups.Where(g => g.DimensionId == d.Id).Select(g => new GroupRecord(g.Id, g.Name, g.ArchivedAt is not null, includeHeadcount ? HeadcountEntry.EffectiveAt(headcounts.Where(h => h.GroupId == g.Id), today) : null)).ToList())).ToList();
    }

    public async Task RenameDimensionAsync(Guid dimensionId, string name, bool active, CancellationToken cancellationToken)
    {
        var tenantId = RequireRole(Role.TenantAdmin, Role.ProgrammeManager);
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var dimension = await db.Dimensions.SingleOrDefaultAsync(d => d.TenantId == tenantId && d.Id == dimensionId, cancellationToken)
            ?? throw new OrganisationHierarchyException("Unbekannte Dimension.");
        dimension.Rename(name, active);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditEntry("organisation.dimension.changed", dimensionId.ToString("D"), active ? "active" : "inactive"), cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    public async Task<Guid> CreateGroupAsync(Guid dimensionId, string name, int? headcount, CancellationToken cancellationToken)
    {
        var tenantId = RequireRole(Role.TenantAdmin, Role.ProgrammeManager);
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var now = clock.GetUtcNow();
        var dimension = await db.Dimensions.SingleOrDefaultAsync(d => d.TenantId == tenantId && d.Id == dimensionId, cancellationToken)
            ?? throw new OrganisationHierarchyException("Unbekannte Dimension.");
        var group = MemberGroup.Create(tenantId, dimension, name, now);
        db.Groups.Add(group);
        if (headcount is { } count)
        {
            db.Headcounts.Add(HeadcountEntry.Record(tenantId, group.Id, TenantTimeZone.DayOf(now), count, now));
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditEntry("organisation.group.created", group.Id.ToString("D"), null), cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return group.Id;
    }

    public async Task ArchiveGroupAsync(Guid groupId, CancellationToken cancellationToken)
    {
        var tenantId = RequireRole(Role.TenantAdmin, Role.ProgrammeManager);
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var group = await db.Groups.SingleOrDefaultAsync(g => g.TenantId == tenantId && g.Id == groupId, cancellationToken)
            ?? throw new OrganisationHierarchyException("Unbekannte Gruppe.");
        group.Archive(clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditEntry("organisation.group.archived", groupId.ToString("D"), null), cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    public async Task SetHeadcountAsync(Guid? groupId, DateOnly effectiveFrom, int count, CancellationToken cancellationToken)
    {
        var tenantId = RequireRole(Role.TenantAdmin, Role.ProgrammeManager);
        await using var tx = await transaction.BeginAsync(cancellationToken);
        if (groupId is { } id && !await db.Groups.AnyAsync(g => g.TenantId == tenantId && g.Id == id, cancellationToken))
        {
            throw new OrganisationHierarchyException("Unbekannte Gruppe.");
        }

        db.Headcounts.Add(HeadcountEntry.Record(tenantId, groupId, effectiveFrom, count, clock.GetUtcNow()));
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditEntry("organisation.headcount.changed", groupId?.ToString("D") ?? tenantId.ToString(), $"{effectiveFrom:yyyy-MM-dd}:{count}"), cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    public async Task<int?> GetHeadcountAtAsync(Guid? groupId, DateOnly day, CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var entries = await db.Headcounts.Where(h => h.TenantId == tenantId && h.GroupId == groupId).ToListAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return HeadcountEntry.EffectiveAt(entries, day);
    }

    public async Task<IReadOnlyList<GroupChoiceRecord>> GetGroupsOfAsync(PersonId personId, CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var dimensions = await EnsureDimensionsAsync(tenantId, cancellationToken);
        var memberships = await db.GroupMemberships.Where(g => g.TenantId == tenantId && g.PersonId == personId).ToListAsync(cancellationToken);
        var groupIds = memberships.Select(m => m.GroupId).ToList();
        var archived = await db.Groups.Where(g => g.TenantId == tenantId && groupIds.Contains(g.Id) && g.ArchivedAt != null).Select(g => g.Id).ToListAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return dimensions.Where(d => d.Active).OrderBy(d => d.Position).Select(d =>
        {
            var membership = memberships.FirstOrDefault(m => m.DimensionId == d.Id);
            var isArchived = membership is not null && archived.Contains(membership.GroupId);
            return new GroupChoiceRecord(d.Id, isArchived ? null : membership?.GroupId, isArchived);
        }).ToList();
    }

    public async Task<IReadOnlyDictionary<PersonId, IReadOnlyList<Guid>>> GetGroupsOfManyAsync(IEnumerable<PersonId> personIds, CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        var ids = personIds.Distinct().ToList();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var archived = await db.Groups.Where(g => g.TenantId == tenantId && g.ArchivedAt != null).Select(g => g.Id).ToListAsync(cancellationToken);
        var rows = await db.GroupMemberships.Where(g => g.TenantId == tenantId && ids.Contains(g.PersonId)).ToListAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        // Archivierte Gruppen gelten als nicht zugeordnet (Organisation 2.1), bis die Person neu wählt.
        return ids.ToDictionary(id => id, id => (IReadOnlyList<Guid>)rows.Where(r => r.PersonId == id && !archived.Contains(r.GroupId)).Select(r => r.GroupId).Order().ToList());
    }

    public async Task ChooseGroupAsync(PersonId personId, Guid dimensionId, Guid? groupId, CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var now = clock.GetUtcNow();
        var existing = await db.GroupMemberships.SingleOrDefaultAsync(g => g.TenantId == tenantId && g.PersonId == personId && g.DimensionId == dimensionId, cancellationToken);
        if (groupId is null)
        {
            if (existing is not null)
            {
                db.GroupMemberships.Remove(existing);
            }
        }
        else
        {
            var group = await db.Groups.SingleOrDefaultAsync(g => g.TenantId == tenantId && g.Id == groupId && g.DimensionId == dimensionId, cancellationToken)
                ?? throw new OrganisationHierarchyException("Unbekannte Gruppe.");
            if (existing is null)
            {
                db.GroupMemberships.Add(GroupMembership.Choose(tenantId, personId, group, now));
            }
            else
            {
                existing.Change(group, now);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PersonId>> ListGroupMembersAsync(Guid groupId, CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var members = await db.GroupMemberships
            .Where(g => g.TenantId == tenantId && g.GroupId == groupId)
            .Join(db.Memberships.Where(m => m.TenantId == tenantId && m.State == MembershipState.Active), g => g.PersonId, m => m.PersonId, (g, m) => g.PersonId)
            .OrderBy(p => p)
            .ToListAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return members;
    }

    /// <summary>Die drei voreingestellten Dimensionen entstehen beim ersten Zugriff des Tenants (Organisation 2.1).</summary>
    private async Task<List<GroupDimension>> EnsureDimensionsAsync(TenantId tenantId, CancellationToken cancellationToken)
    {
        var dimensions = await db.Dimensions.Where(d => d.TenantId == tenantId).ToListAsync(cancellationToken);
        if (dimensions.Count > 0)
        {
            return dimensions;
        }

        // Die Standarddimensionen entstehen genau einmal je Tenant, auch wenn mehrere Jobs oder Requests gleichzeitig danach fragen:
        // Sperre je Tenant innerhalb der Kontexttransaktion, danach erneut lesen.
        await db.Database.ExecuteSqlAsync($"select pg_advisory_xact_lock(hashtext({tenantId.Value.ToString("D")}))", cancellationToken);
        dimensions = await db.Dimensions.Where(d => d.TenantId == tenantId).ToListAsync(cancellationToken);
        if (dimensions.Count > 0)
        {
            return dimensions;
        }

        var now = clock.GetUtcNow();
        for (var i = 0; i < GroupRules.DefaultDimensionNames.Count; i++)
        {
            var dimension = GroupDimension.Create(tenantId, GroupRules.DefaultDimensionNames[i], i, now);
            db.Dimensions.Add(dimension);
            dimensions.Add(dimension);
        }

        await db.SaveChangesAsync(cancellationToken);
        return dimensions;
    }

    private async Task<Domain.Organisation> RequireTenantAsync(TenantId tenantId, CancellationToken cancellationToken) =>
        await db.Organisations.SingleOrDefaultAsync(o => o.Id == tenantId.Value && o.Type == OrganisationType.Tenant, cancellationToken)
            ?? throw new OrganisationHierarchyException("Tenant nicht gefunden.");

    private TenantId RequireRole(params string[] roles)
    {
        var current = context.Require();
        if (!roles.Any(current.HasRole))
        {
            throw new UnauthorizedAccessException("Rechtematrix (Organisation 4.2): diese Rolle darf das nicht.");
        }

        return current.RequireTenant();
    }

    private void RequirePlatform()
    {
        if (context.Require().Kind != TenantContextKind.Platform)
        {
            throw new TenantContextMissingException("Organisationen werden nur im Plattformkontext angelegt (Backend 5.3).");
        }
    }
}

/// <summary>Mitgliedschaftsprüfung je Request (Backend 5.1 Nr. 1): aktives Mitglied eines aktiven Tenants, Rollen aus dem Katalog; Lesefrist nach Kündigung (Organisation 1.3).</summary>
internal sealed class MembershipVerification(OrganisationDbContext db, IContextTransaction transaction, TimeProvider clock) : IMembershipVerification
{
    public async Task<MembershipVerdict> VerifyAsync(TenantId tenantId, PersonId personId, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var tenant = await db.Organisations.SingleOrDefaultAsync(o => o.Id == tenantId.Value, cancellationToken);
        var grants = tenant is not null && tenant.GrantsMemberAccessAt(now);
        var readOnly = tenant is not null && !grants && tenant.GrantsReadOnlyAccessAt(now);
        var membership = tenant is null || (!grants && !readOnly)
            ? null
            : await db.Memberships.Include(m => m.Roles).SingleOrDefaultAsync(m => m.TenantId == tenantId && m.PersonId == personId, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        if (tenant is not null && !grants && !readOnly)
        {
            return tenant.AccessDeniedReasonAt(now) is { } reason ? MembershipVerdict.DeniedBecause(reason) : MembershipVerdict.Denied;
        }

        if (membership is null || membership.State != MembershipState.Active)
        {
            return MembershipVerdict.Denied;
        }

        var roles = new HashSet<string>(membership.Roles.Select(r => r.Role), StringComparer.Ordinal);
        if (readOnly)
        {
            // Nach dem Wirksamkeitstermin nur Tenant-Admin und Einsichtsrolle, nur lesend (Organisation 1.3).
            return roles.Contains(Role.TenantAdmin) || roles.Contains(Role.Insight)
                ? new MembershipVerdict(true, roles, ReadOnly: true)
                : MembershipVerdict.DeniedBecause("tenant_terminated");
        }

        return new MembershipVerdict(true, roles);
    }

    public async Task<bool> VerifyTenantAsync(TenantId tenantId, CancellationToken cancellationToken)
    {
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var tenant = await db.Organisations.SingleOrDefaultAsync(o => o.Id == tenantId.Value, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return tenant is not null && tenant.GrantsMemberAccessAt(clock.GetUtcNow());
    }
}

/// <summary>Eigentümer der Tenant-Zeitzone (Organisation 1.2); ein Wert je Scope.</summary>
internal sealed class TenantTimeZoneProvider(OrganisationDbContext db, IContextTransaction transaction, ITenantContextAccessor context) : ITenantTimeZone
{
    private TimeZoneInfo? _zone;

    public async Task<TimeZoneInfo> GetAsync(CancellationToken cancellationToken)
    {
        if (_zone is not null)
        {
            return _zone;
        }

        var current = context.Current;
        if (current is null || current.Kind != TenantContextKind.Tenant)
        {
            return _zone = TenantTimeZone.Default;
        }

        await using var tx = await transaction.BeginAsync(cancellationToken);
        var id = await db.Organisations.Where(o => o.Id == current.RequireTenant().Value).Select(o => o.TimeZoneId).SingleOrDefaultAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return _zone = TenantTimeZone.Resolve(id);
    }
}
