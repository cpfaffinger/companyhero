using System.Text.Json.Serialization;
using CompanyHero.Modules.Entitlements.Application;
using CompanyHero.Modules.Entitlements.Domain;
using CompanyHero.Modules.Organisation.Domain;
using CompanyHero.Platform.Entitlements;
using CompanyHero.Platform.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CompanyHero.Modules.Entitlements.Api;

/// <summary>Navigation der Mitglieder-App aus aktiven Entitlements (Entitlements 2.2, 5; A-064): keine Einträge und keine Hinweise für inaktive Module.</summary>
public sealed record NavigationResponse(IReadOnlyList<string> Modules, IReadOnlyList<string> Areas);

[JsonConverter(typeof(JsonStringEnumConverter<EntitlementStateDto>))]
public enum EntitlementStateDto
{
    [JsonStringEnumMemberName("trial")] Trial,
    [JsonStringEnumMemberName("active")] Active,
    [JsonStringEnumMemberName("expiring")] Expiring,
    [JsonStringEnumMemberName("inactive")] Inactive,
}

/// <summary>Modul des Katalogs mit Zustand (Entitlements 2.2, 3.1); Texte als Schlüssel des Katalogs (A-080).</summary>
public sealed record ModuleStatusResponse(string Module, string NameKey, EntitlementStateDto State, DateTimeOffset? ActiveFrom, DateTimeOffset? ActiveUntil, DateTimeOffset? TrialUntil, bool TrialAvailable, IReadOnlyList<string> Dependencies, string PrivacyHintKey);

/// <summary>Ergebnis einer Buchung: <c>booked</c>, <c>bundle_suggested</c> (Vorschlag statt Ablehnung, Entitlements 4.2) oder <c>already_active</c>.</summary>
public sealed record BookingResponse(string Outcome, IReadOnlyList<string> Suggestion, IReadOnlyList<string> Booked);

public sealed record BundleRequest(IReadOnlyList<string> Modules);

public sealed record TrialResponse(string Outcome, DateTimeOffset? TrialUntil);

public sealed record CancellationResponse(IReadOnlyList<string> Affected, DateTimeOffset? ActiveUntil);

public sealed record EntitlementHistoryResponse(string Id, string Module, DateTimeOffset OccurredAt, EntitlementStateDto? FromState, EntitlementStateDto ToState, string ActorRoles, string Source, string Reason);

internal static class EntitlementEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/entitlements").HandleEntitlementErrors();

        group.MapGet("/me", async (IModuleEntitlements entitlements, CancellationToken ct) =>
            {
                var active = await entitlements.ActiveModulesAsync(ct);
                return Results.Ok(new NavigationResponse(ModuleCatalog.Modules.Select(m => m.Code).Where(active.Contains).ToList(), ModuleCatalog.NavigationAreas(active)));
            })
            .RequireTenantContext().AllowKiosk(device: true)
            .WithName("GetMyEntitlements")
            .Produces<NavigationResponse>();

        group.MapGet("/modules", async (IEntitlementService service, CancellationToken ct) =>
                Results.Ok((await service.CatalogAsync(ct)).Select(ToResponse).ToList()))
            .RequireTenantContext().RequireRoles(Role.TenantAdmin, Role.Insight)
            .WithName("ListModules")
            .Produces<List<ModuleStatusResponse>>();

        // Buchung ist eine sensible Aktion mit frischer Anmeldung (Entitlements 4.2 Nr. 4, A-015).
        group.MapPost("/modules/{module}/book", async (string module, IEntitlementService service, CancellationToken ct) =>
                ModuleCatalog.IsKnown(module) ? Results.Ok(ToResponse(await service.BookAsync(module, ct))) : Results.NotFound())
            .RequireTenantContext().RequireRoles(Role.TenantAdmin).RequireFreshLogin()
            .WithName("BookModule")
            .Produces<BookingResponse>()
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/bundles/book", async (BundleRequest request, IEntitlementService service, CancellationToken ct) =>
            {
                if (request.Modules is not { Count: > 0 } || request.Modules.Any(m => !ModuleCatalog.IsKnown(m)))
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["modules"] = ["Bekannte Modulcodes erwartet."] });
                }

                return Results.Ok(ToResponse(await service.BookBundleAsync(request.Modules, ct)));
            })
            .RequireTenantContext().RequireRoles(Role.TenantAdmin).RequireFreshLogin()
            .WithName("BookBundle")
            .Produces<BookingResponse>()
            .ProducesValidationProblem();

        group.MapPost("/modules/{module}/trial", async (string module, IEntitlementService service, CancellationToken ct) =>
            {
                if (!ModuleCatalog.IsKnown(module))
                {
                    return Results.NotFound();
                }

                var outcome = await service.StartTrialAsync(module, ct);
                var name = outcome.Result switch
                {
                    TrialResult.Started => "started",
                    TrialResult.TrialUsed => "trial_used",
                    TrialResult.AlreadyActive => "already_active",
                    _ => "dependency_missing",
                };
                return Results.Ok(new TrialResponse(name, outcome.TrialUntil));
            })
            .RequireTenantContext().RequireRoles(Role.TenantAdmin).RequireFreshLogin()
            .WithName("StartModuleTrial")
            .Produces<TrialResponse>()
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/modules/{module}/cancel", async (string module, IEntitlementService service, CancellationToken ct) =>
            {
                if (!ModuleCatalog.IsKnown(module))
                {
                    return Results.NotFound();
                }

                var outcome = await service.CancelAsync(module, ct);
                return Results.Ok(new CancellationResponse(outcome.Affected, outcome.ActiveUntil));
            })
            .RequireTenantContext().RequireRoles(Role.TenantAdmin).RequireFreshLogin()
            .WithName("CancelModule")
            .Produces<CancellationResponse>()
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/modules/{module}/revoke-cancellation", async (string module, IEntitlementService service, CancellationToken ct) =>
            {
                if (!ModuleCatalog.IsKnown(module))
                {
                    return Results.NotFound();
                }

                var outcome = await service.RevokeCancellationAsync(module, ct);
                return Results.Ok(new CancellationResponse(outcome.Affected, outcome.ActiveUntil));
            })
            .RequireTenantContext().RequireRoles(Role.TenantAdmin).RequireFreshLogin()
            .WithName("RevokeModuleCancellation")
            .Produces<CancellationResponse>()
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/history", async (IEntitlementService service, CancellationToken ct) =>
                Results.Ok((await service.HistoryAsync(ct)).Select(h => new EntitlementHistoryResponse(h.Id.ToString("D"), h.Module, h.OccurredAt, h.FromState is { } f ? ToDto(f) : null, ToDto(h.ToState), h.ActorRoles, SourceName(h.Source), h.Reason)).ToList()))
            .RequireTenantContext().RequireRoles(Role.TenantAdmin, Role.Insight)
            .WithName("ListEntitlementHistory")
            .Produces<List<EntitlementHistoryResponse>>();
    }

    private static ModuleStatusResponse ToResponse(ModuleStatus s) =>
        new(s.Module, s.NameKey, ToDto(s.State), s.ActiveFrom, s.ActiveUntil, s.TrialUntil, s.TrialAvailable, s.Dependencies, s.PrivacyHintKey);

    private static BookingResponse ToResponse(BookingOutcome outcome) =>
        new(outcome.Result switch { BookingResult.Booked => "booked", BookingResult.BundleSuggested => "bundle_suggested", _ => "already_active" }, outcome.Suggestion, outcome.Booked);

    private static EntitlementStateDto ToDto(EntitlementState state) => state switch
    {
        EntitlementState.Trial => EntitlementStateDto.Trial,
        EntitlementState.Active => EntitlementStateDto.Active,
        EntitlementState.Expiring => EntitlementStateDto.Expiring,
        _ => EntitlementStateDto.Inactive,
    };

    private static string SourceName(EntitlementSource source) => source switch
    {
        EntitlementSource.TenantAdmin => "tenant_admin",
        EntitlementSource.PartnerAdmin => "partner_admin",
        EntitlementSource.OperatorAdmin => "operator_admin",
        _ => "preset",
    };

    private static TBuilder HandleEntitlementErrors<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder
    {
        builder.AddEndpointFilter(async (invocation, next) =>
        {
            try
            {
                return await next(invocation);
            }
            catch (EntitlementException ex)
            {
                return ex.Reason == "unknown_module"
                    ? Results.NotFound()
                    : Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Entitlement", detail: ex.Reason);
            }
        });
        return builder;
    }
}
