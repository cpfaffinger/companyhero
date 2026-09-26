using CompanyHero.Modules.Organisation.Domain;
using CompanyHero.Modules.Organisation.Infrastructure;
using CompanyHero.Platform.Data;
using CompanyHero.Platform.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace CompanyHero.Modules.Organisation.Application;

internal sealed class OrganisationDirectory(OrganisationDbContext db, IContextTransaction transaction, ITenantContextAccessor context, TimeProvider clock) : IOrganisationDirectory
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

    public async Task<TenantId> CreateTenantAsync(string displayName, Guid parentId, CancellationToken cancellationToken)
    {
        RequirePlatform();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var parent = await db.Organisations.SingleOrDefaultAsync(o => o.Id == parentId, cancellationToken)
            ?? throw new OrganisationHierarchyException("Übergeordnete Organisation nicht gefunden.");
        var tenant = Domain.Organisation.CreateTenant(displayName, parent, clock.GetUtcNow());
        db.Organisations.Add(tenant);
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return new TenantId(tenant.Id);
    }

    public async Task AddMemberAsync(PersonId personId, CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        db.Memberships.Add(Membership.Join(tenantId, personId, clock.GetUtcNow()));
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
        await tx.CommitAsync(cancellationToken);
        return members.Select(m => new MemberRecord(m.PersonId, m.JoinedAt, m.Roles.Select(r => r.Role).Order(StringComparer.Ordinal).ToList())).ToList();
    }

    public async Task<(string DisplayName, OrganisationState State)?> GetCurrentTenantAsync(CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var tenant = await db.Organisations.SingleOrDefaultAsync(o => o.Id == tenantId.Value, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return tenant is null ? null : (tenant.DisplayName, tenant.State);
    }

    public async Task<(string DisplayName, OrganisationState State)?> GetTenantAsync(TenantId tenantId, CancellationToken cancellationToken)
    {
        RequirePlatform();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var tenant = await db.Organisations.SingleOrDefaultAsync(o => o.Id == tenantId.Value && o.Type == OrganisationType.Tenant, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return tenant is null ? null : (tenant.DisplayName, tenant.State);
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

    private void RequirePlatform()
    {
        if (context.Require().Kind != TenantContextKind.Platform)
        {
            throw new TenantContextMissingException("Organisationen werden nur im Plattformkontext angelegt (Backend 5.3).");
        }
    }
}

/// <summary>Mitgliedschaftsprüfung je Request (Backend 5.1 Nr. 1): aktives Mitglied eines aktiven Tenants, Rollen aus dem Katalog.</summary>
internal sealed class MembershipVerification(OrganisationDbContext db, IContextTransaction transaction) : IMembershipVerification
{
    public async Task<MembershipVerdict> VerifyAsync(TenantId tenantId, PersonId personId, CancellationToken cancellationToken)
    {
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var tenant = await db.Organisations.SingleOrDefaultAsync(o => o.Id == tenantId.Value, cancellationToken);
        var membership = tenant is null || !tenant.GrantsMemberAccess
            ? null
            : await db.Memberships.Include(m => m.Roles).SingleOrDefaultAsync(m => m.TenantId == tenantId && m.PersonId == personId, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        if (membership is null || membership.State != MembershipState.Active)
        {
            return MembershipVerdict.Denied;
        }

        return new MembershipVerdict(true, new HashSet<string>(membership.Roles.Select(r => r.Role), StringComparer.Ordinal));
    }

    public async Task<bool> VerifyTenantAsync(TenantId tenantId, CancellationToken cancellationToken)
    {
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var tenant = await db.Organisations.SingleOrDefaultAsync(o => o.Id == tenantId.Value, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return tenant is not null && tenant.GrantsMemberAccess;
    }
}
