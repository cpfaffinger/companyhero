using System.Text.Json.Nodes;
using CompanyHero.Modules.Organisation.Domain;
using CompanyHero.Modules.Organisation.Infrastructure;
using CompanyHero.Platform.Data;
using CompanyHero.Platform.Privacy;
using CompanyHero.Platform.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace CompanyHero.Modules.Organisation.Application;

/// <summary>Auskunft (Datenschutz 6.4): Mitgliedschaft, Rollen und Gruppen der Person; Löschung (5.2): Identitätskategorie bis Austritt, dann entfernt.</summary>
internal sealed class OrganisationPersonalData(OrganisationDbContext db, IContextTransaction transaction, ITenantContextAccessor context) : IPersonalDataExporter, IPersonalDataEraser
{
    public string Section => "organisation";

    public async Task<JsonNode> ExportAsync(PersonId personId, CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var membership = await db.Memberships.Include(m => m.Roles).AsNoTracking().SingleOrDefaultAsync(m => m.TenantId == tenantId && m.PersonId == personId, cancellationToken);
        var groups = await db.GroupMemberships.AsNoTracking().Where(g => g.TenantId == tenantId && g.PersonId == personId).ToListAsync(cancellationToken);
        var groupIds = groups.Select(g => g.GroupId).ToList();
        var names = await db.Groups.AsNoTracking().Where(g => g.TenantId == tenantId && groupIds.Contains(g.Id)).ToDictionaryAsync(g => g.Id, g => g.Name, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return new JsonObject
        {
            ["mitgliedschaft"] = membership is null ? null : new JsonObject
            {
                ["zustand"] = membership.State.ToString(),
                ["beitritt"] = membership.JoinedAt,
                ["rollen"] = new JsonArray(membership.Roles.Select(r => (JsonNode)r.Role).ToArray()),
            },
            ["gruppen"] = new JsonArray(groups.Select(g => (JsonNode)new JsonObject { ["gruppe"] = names.GetValueOrDefault(g.GroupId, g.GroupId.ToString("D")), ["seit"] = g.Since }).ToArray()),
        };
    }

    public async Task EraseAsync(PersonId personId, CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        await db.RoleAssignments.Where(r => r.TenantId == tenantId && r.PersonId == personId).ExecuteDeleteAsync(cancellationToken);
        await db.GroupMemberships.Where(g => g.TenantId == tenantId && g.PersonId == personId).ExecuteDeleteAsync(cancellationToken);
        await db.Memberships.Where(m => m.TenantId == tenantId && m.PersonId == personId && m.State != MembershipState.Active).ExecuteDeleteAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }
}
