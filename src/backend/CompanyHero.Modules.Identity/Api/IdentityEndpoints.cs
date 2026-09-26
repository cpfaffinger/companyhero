using CompanyHero.Modules.Identity.Application;
using CompanyHero.Modules.Organisation.Application;
using CompanyHero.Modules.Organisation.Domain;
using CompanyHero.Platform.Hosting;
using CompanyHero.Platform.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CompanyHero.Modules.Identity.Api;

/// <summary>Die angemeldete Person: Kennung, Anzeigename, Tenant und Rollen laut Organisation.</summary>
public sealed record MeResponse(string PersonId, string DisplayName, string TenantId, string TenantName, IReadOnlyList<string> Roles);

/// <summary>Eintrag der Mitgliederliste (Organisation 3.2): Anzeigename, Rollen, Beitritt. Keine Aktivitätsdaten.</summary>
public sealed record MemberResponse(string PersonId, string DisplayName, IReadOnlyList<string> Roles, DateTimeOffset JoinedAt);

internal static class IdentityEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/me", async (ITenantContextAccessor context, IPersonDirectory persons, IOrganisationDirectory organisations, CancellationToken ct) =>
            {
                var current = context.Require();
                var person = await persons.GetAsync(current.RequirePerson(), ct);
                var tenant = await organisations.GetCurrentTenantAsync(ct);
                if (person is null || tenant is null)
                {
                    return Results.NotFound();
                }

                return Results.Ok(new MeResponse(
                    person.PersonId.ToString(),
                    person.DisplayName,
                    current.RequireTenant().ToString(),
                    tenant.Value.DisplayName,
                    current.Roles.Order(StringComparer.Ordinal).ToList()));
            })
            .RequireTenantContext()
            .WithName("GetMe")
            .Produces<MeResponse>()
            .Produces(StatusCodes.Status404NotFound);

        // Rechtematrix (Organisation 4.2): Mitgliederliste nur für den Tenant-Admin; jede andere Rolle wird abgelehnt.
        endpoints.MapGet("/api/members", async (ITenantContextAccessor context, IPersonDirectory persons, IOrganisationDirectory organisations, CancellationToken ct) =>
            {
                if (!context.Require().HasRole(Role.TenantAdmin))
                {
                    return Results.Forbid();
                }

                var members = await organisations.ListMembersAsync(ct);
                var names = await persons.GetDisplayNamesAsync(members.Select(m => m.PersonId), ct);
                var response = members
                    .Select(m => new MemberResponse(m.PersonId.ToString(), names.GetValueOrDefault(m.PersonId, string.Empty), m.Roles, m.JoinedAt))
                    .OrderBy(m => m.DisplayName, StringComparer.Ordinal)
                    .ToList();
                return Results.Ok(response);
            })
            .RequireTenantContext()
            .WithName("ListMembers")
            .Produces<List<MemberResponse>>()
            .Produces(StatusCodes.Status403Forbidden);
    }
}
