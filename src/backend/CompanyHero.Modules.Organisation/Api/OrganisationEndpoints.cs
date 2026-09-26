using CompanyHero.Modules.Organisation.Application;
using CompanyHero.Modules.Organisation.Domain;
using CompanyHero.Platform.Hosting;
using CompanyHero.Platform.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CompanyHero.Modules.Organisation.Api;

/// <summary>Stammdaten und Zustand des Tenants (Organisation 1.2, 1.3); Sollstärke und Sperrgrund nur für berechtigte Rollen.</summary>
public sealed record TenantResponse(string TenantId, string DisplayName, string State, string TimeZone, int? Headcount, DateTimeOffset? TerminationEffectiveAt, DateTimeOffset? ReadOnlyUntil, string? SuspensionReason);

public sealed record TenantSettingsRequest(string TimeZone);

/// <summary>Gruppe einer Dimension; Sollstärke nur für Rollen mit Leserecht, mit Warnung unter fünf (Organisation 2.2).</summary>
public sealed record GroupResponse(string GroupId, string Name, bool Archived, int? Headcount, bool HeadcountWarning);

public sealed record DimensionResponse(string DimensionId, string Name, int Position, bool Active, IReadOnlyList<GroupResponse> Groups);

public sealed record DimensionRequest(string Name, bool Active);

public sealed record CreateGroupRequest(string Name, int? Headcount);

public sealed record CreatedResponse(string Id);

/// <summary>Sollstärke mit Stichtag (Organisation 2.2): gilt ab dem Tag; vergangene Quoten bleiben.</summary>
public sealed record HeadcountRequest(DateOnly EffectiveFrom, int Count);

/// <summary>Eigene Zuordnung je Dimension (Organisation 2.1); <c>rechoice</c> nach Archivierung der bisherigen Gruppe.</summary>
public sealed record GroupChoiceResponse(string DimensionId, string? GroupId, bool Rechoice);

public sealed record GroupChoiceRequest(string DimensionId, string? GroupId);

internal static class OrganisationEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var org = endpoints.MapGroup("/api/organisation").HandleOrganisationErrors();

        org.MapGet("/tenant", async (ITenantContextAccessor context, IOrganisationDirectory directory, TimeProvider clock, CancellationToken ct) =>
            {
                var current = context.Require();
                var tenant = await directory.GetCurrentTenantRecordAsync(ct);
                if (tenant is null)
                {
                    return Results.NotFound();
                }

                var mayReadHeadcount = current.HasRole(Role.TenantAdmin) || current.HasRole(Role.ProgrammeManager) || current.HasRole(Role.Insight);
                var headcount = mayReadHeadcount ? await directory.GetHeadcountAtAsync(null, TenantTimeZone.DayOf(clock.GetUtcNow(), TenantTimeZone.Resolve(tenant.TimeZoneId)), ct) : null;
                return Results.Ok(new TenantResponse(
                    tenant.TenantId.ToString(), tenant.DisplayName, StateName(tenant.State), tenant.TimeZoneId, headcount, tenant.TerminationEffectiveAt, tenant.ReadOnlyUntil,
                    current.HasRole(Role.TenantAdmin) ? tenant.SuspensionReason : null));
            })
            .RequireTenantContext().AllowKiosk(device: true)
            .WithName("GetTenant")
            .Produces<TenantResponse>()
            .Produces(StatusCodes.Status404NotFound);

        org.MapPut("/tenant", async (TenantSettingsRequest request, IOrganisationDirectory directory, CancellationToken ct) =>
            {
                if (!TenantTimeZone.IsKnown(request.TimeZone))
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["timeZone"] = ["IANA-Zeitzone erwartet."] });
                }

                await directory.UpdateTimeZoneAsync(request.TimeZone, ct);
                return Results.NoContent();
            })
            .RequireTenantContext().RequireRoles(Role.TenantAdmin)
            .WithName("UpdateTenantSettings")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem();

        org.MapPost("/tenant/terminate", async (IOrganisationDirectory directory, CancellationToken ct) =>
            {
                await directory.TerminateCurrentTenantAsync(ct);
                return Results.NoContent();
            })
            .RequireTenantContext().RequireRoles(Role.TenantAdmin).RequireFreshLogin()
            .WithName("TerminateTenant")
            .Produces(StatusCodes.Status204NoContent);

        org.MapPost("/tenant/headcount", async (HeadcountRequest request, IOrganisationDirectory directory, CancellationToken ct) =>
            {
                await directory.SetHeadcountAsync(null, request.EffectiveFrom, request.Count, ct);
                return Results.NoContent();
            })
            .RequireTenantContext().RequireRoles(Role.TenantAdmin, Role.ProgrammeManager)
            .WithName("SetTenantHeadcount")
            .Produces(StatusCodes.Status204NoContent);

        org.MapGet("/dimensions", async (ITenantContextAccessor context, IOrganisationDirectory directory, CancellationToken ct) =>
            {
                var current = context.Require();
                var mayReadHeadcount = current.HasRole(Role.TenantAdmin) || current.HasRole(Role.ProgrammeManager) || current.HasRole(Role.Insight);
                var dimensions = await directory.ListDimensionsAsync(mayReadHeadcount, ct);
                return Results.Ok(dimensions.Select(ToResponse).ToList());
            })
            .RequireTenantContext()
            .WithName("ListDimensions")
            .Produces<List<DimensionResponse>>();

        org.MapPut("/dimensions/{dimensionId:guid}", async (Guid dimensionId, DimensionRequest request, IOrganisationDirectory directory, CancellationToken ct) =>
            {
                await directory.RenameDimensionAsync(dimensionId, request.Name, request.Active, ct);
                return Results.NoContent();
            })
            .RequireTenantContext().RequireRoles(Role.TenantAdmin, Role.ProgrammeManager)
            .WithName("UpdateDimension")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        org.MapPost("/dimensions/{dimensionId:guid}/groups", async (Guid dimensionId, CreateGroupRequest request, IOrganisationDirectory directory, CancellationToken ct) =>
            {
                var id = await directory.CreateGroupAsync(dimensionId, request.Name, request.Headcount, ct);
                return Results.Created($"/api/organisation/groups/{id:D}", new CreatedResponse(id.ToString("D")));
            })
            .RequireTenantContext().RequireRoles(Role.TenantAdmin, Role.ProgrammeManager)
            .WithName("CreateGroup")
            .Produces<CreatedResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status404NotFound);

        org.MapPost("/groups/{groupId:guid}/archive", async (Guid groupId, IOrganisationDirectory directory, CancellationToken ct) =>
            {
                await directory.ArchiveGroupAsync(groupId, ct);
                return Results.NoContent();
            })
            .RequireTenantContext().RequireRoles(Role.TenantAdmin, Role.ProgrammeManager)
            .WithName("ArchiveGroup")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        org.MapPost("/groups/{groupId:guid}/headcount", async (Guid groupId, HeadcountRequest request, IOrganisationDirectory directory, CancellationToken ct) =>
            {
                await directory.SetHeadcountAsync(groupId, request.EffectiveFrom, request.Count, ct);
                return Results.NoContent();
            })
            .RequireTenantContext().RequireRoles(Role.TenantAdmin, Role.ProgrammeManager)
            .WithName("SetGroupHeadcount")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        // Eigene Zuordnung (Organisation 2.1, 4.2 „eigene Zuordnung“): jedes Mitglied wählt und ändert selbst.
        var me = endpoints.MapGroup("/api/me/groups").HandleOrganisationErrors();
        me.MapGet("/", async (ITenantContextAccessor context, IOrganisationDirectory directory, CancellationToken ct) =>
            {
                var choices = await directory.GetGroupsOfAsync(context.Require().RequirePerson(), ct);
                return Results.Ok(choices.Select(c => new GroupChoiceResponse(c.DimensionId.ToString("D"), c.GroupId?.ToString("D"), c.Rechoice)).ToList());
            })
            .RequireTenantContext()
            .WithName("GetMyGroups")
            .Produces<List<GroupChoiceResponse>>();

        me.MapPut("/", async (GroupChoiceRequest request, ITenantContextAccessor context, IOrganisationDirectory directory, CancellationToken ct) =>
            {
                if (!Guid.TryParseExact(request.DimensionId, "D", out var dimensionId))
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["dimensionId"] = ["Kennung erwartet."] });
                }

                Guid? groupId = null;
                if (request.GroupId is not null)
                {
                    if (!Guid.TryParseExact(request.GroupId, "D", out var parsed))
                    {
                        return Results.ValidationProblem(new Dictionary<string, string[]> { ["groupId"] = ["Kennung erwartet."] });
                    }

                    groupId = parsed;
                }

                await directory.ChooseGroupAsync(context.Require().RequirePerson(), dimensionId, groupId, ct);
                return Results.NoContent();
            })
            .RequireTenantContext()
            .WithName("ChooseMyGroup")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound);
    }

    private static DimensionResponse ToResponse(DimensionRecord d) =>
        new(d.DimensionId.ToString("D"), d.Name, d.Position, d.Active, d.Groups.Select(g => new GroupResponse(g.GroupId.ToString("D"), g.Name, g.Archived, g.Headcount, GroupRules.WarnsAboutHeadcount(g.Headcount))).ToList());

    private static string StateName(OrganisationState state) => state switch
    {
        OrganisationState.Provisioned => "provisioned",
        OrganisationState.Active => "active",
        OrganisationState.Suspended => "suspended",
        OrganisationState.Terminated => "terminated",
        _ => "deleted",
    };

    /// <summary>Fachliche Ablehnungen der Organisation: unbekannte Objekte als „nicht gefunden“ (Datenschutz 7), Regelverstöße als 409, Rechte als 403.</summary>
    private static TBuilder HandleOrganisationErrors<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder
    {
        builder.AddEndpointFilter(async (invocation, next) =>
        {
            try
            {
                return await next(invocation);
            }
            catch (OrganisationHierarchyException ex) when (ex.Message.StartsWith("Unbekannte", StringComparison.Ordinal) || ex.Message.StartsWith("Kein aktives", StringComparison.Ordinal))
            {
                return Results.NotFound();
            }
            catch (OrganisationHierarchyException ex)
            {
                return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Organisation", detail: ex.Message);
            }
            catch (ArgumentException ex)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { [ex.ParamName ?? "request"] = [ex.Message] });
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Forbid();
            }
        });
        return builder;
    }
}
