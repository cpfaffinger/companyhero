using CompanyHero.Modules.Identity.Application;
using CompanyHero.Modules.Identity.Application.ExternalLogin;
using CompanyHero.Modules.Identity.Application.Join;
using CompanyHero.Modules.Identity.Application.Providers;
using CompanyHero.Platform.Hosting;
using CompanyHero.Platform.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CompanyHero.Modules.Identity.Api;

/// <summary>Beitritt (Zugang 2): Vorschau, Passkey-Zeremonie, Beitritt auf eigenem Gerät, am Kiosk und über Rollencode.</summary>
internal static class JoinEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var join = endpoints.MapGroup("/api/join").HandleAccessErrors();

        join.MapGet("/{code}", async (string code, HttpRequest request, IJoinService joins, IExternalLoginFlow external, IProviderCatalog providers, CancellationToken ct) =>
            {
                var preview = await joins.PreviewAsync(code, ct);
                return preview is null ? Results.NotFound() : Results.Ok(await ToResponseAsync(preview, null, request, external, providers, ct));
            })
            .WithName("PreviewJoin")
            .Produces<JoinPreviewResponse>()
            .Produces(StatusCodes.Status404NotFound);

        join.MapPost("/{code}/passkey-options", async (string code, JoinPasskeyOptionsRequest request, IJoinService joins, CancellationToken ct) =>
            {
                if (await joins.PreviewAsync(code, ct) is null)
                {
                    return Results.NotFound();
                }

                var ceremony = joins.BeginPasskeyForNewPerson(request.DisplayName);
                return Results.Ok(new PasskeyCeremonyResponse(ceremony.Options, ceremony.State));
            })
            .WithName("BeginJoinPasskey")
            .Produces<PasskeyCeremonyResponse>()
            .Produces(StatusCodes.Status404NotFound);

        join.MapPost("/{code}", async (string code, JoinRequest request, HttpContext http, IJoinService joins, IExternalLoginFlow external, CancellationToken ct) =>
            {
                var pending = request.UseExternal ? external.ReadPending(http.Request) : null;
                if (request.UseExternal && pending is null)
                {
                    return Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "Zugang abgelehnt", detail: "external_pending_missing");
                }

                var result = await joins.JoinAsync(new JoinCommand(
                    code, request.DisplayName, request.Visibility.ToChoice(),
                    request.Passkey is null ? null : new PasskeyAnswer(request.Passkey.State, request.Passkey.Credential, request.Passkey.DeviceName),
                    request.Email, pending is null ? null : new ExternalIdentity(pending.ProviderKey, pending.SubjectHash), request.KioskPin), ct);
                if (pending is not null)
                {
                    external.ClearPending(http.Response);
                }

                AccessEndpointHelpers.ApplySession(http.Response, result.Session!);
                return Results.Created($"/api/me", new JoinResponse(result.TenantId.ToString(), result.PersonId.ToString(), result.KioskId, result.RecoveryCode, result.Session!.Kind.ToDto()));
            })
            .WithName("Join")
            .Produces<JoinResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        join.MapPost("/{code}/kiosk", async (string code, KioskJoinRequest request, ITenantContextAccessor context, IJoinService joins, CancellationToken ct) =>
            {
                var current = context.Require();
                var preview = await joins.PreviewAsync(code, ct);
                if (preview is null || preview.TenantId != current.RequireTenant())
                {
                    // Der Code bestimmt den Tenant; ein Kiosk eines anderen Tenants sieht ihn als unbekannt (Datenschutz 7).
                    return Results.NotFound();
                }

                var result = await joins.JoinAtKioskAsync(new JoinCommand(code, request.DisplayName, request.Visibility.ToChoice(), null, null, null, request.Pin), ct);
                return Results.Created($"/api/kiosk/device", new JoinResponse(result.TenantId.ToString(), result.PersonId.ToString(), result.KioskId, result.RecoveryCode, null));
            })
            .RequireTenantContext().AllowKiosk(device: true)
            .WithName("JoinAtKiosk")
            .Produces<JoinResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        join.MapGet("/role/{code}", async (string code, HttpRequest request, IJoinService joins, IExternalLoginFlow external, IProviderCatalog providers, CancellationToken ct) =>
            {
                var preview = await joins.PreviewRoleCodeAsync(code, ct);
                return preview is null ? Results.NotFound() : Results.Ok(await ToResponseAsync(preview.Value.Preview, preview.Value.Role, request, external, providers, ct));
            })
            .WithName("PreviewRoleJoin")
            .Produces<JoinPreviewResponse>()
            .Produces(StatusCodes.Status404NotFound);

        join.MapPost("/role/{code}/passkey-options", async (string code, JoinPasskeyOptionsRequest request, IJoinService joins, CancellationToken ct) =>
            {
                if (await joins.PreviewRoleCodeAsync(code, ct) is null)
                {
                    return Results.NotFound();
                }

                var ceremony = joins.BeginPasskeyForNewPerson(request.DisplayName);
                return Results.Ok(new PasskeyCeremonyResponse(ceremony.Options, ceremony.State));
            })
            .WithName("BeginRoleJoinPasskey")
            .Produces<PasskeyCeremonyResponse>()
            .Produces(StatusCodes.Status404NotFound);

        join.MapPost("/role/{code}", async (string code, RoleJoinRequest request, HttpContext http, IJoinService joins, IExternalLoginFlow external, CancellationToken ct) =>
            {
                var pending = request.UseExternal ? external.ReadPending(http.Request) : null;
                if (request.UseExternal && pending is null)
                {
                    return Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "Zugang abgelehnt", detail: "external_pending_missing");
                }

                var result = await joins.JoinWithRoleCodeAsync(new JoinCommand(
                    code, request.RealName, request.Visibility.ToChoice(),
                    request.Passkey is null ? null : new PasskeyAnswer(request.Passkey.State, request.Passkey.Credential, request.Passkey.DeviceName),
                    request.Email, pending is null ? null : new ExternalIdentity(pending.ProviderKey, pending.SubjectHash), null, request.RealName), ct);
                if (pending is not null)
                {
                    external.ClearPending(http.Response);
                }

                AccessEndpointHelpers.ApplySession(http.Response, result.Session!);
                return Results.Created($"/api/me", new JoinResponse(result.TenantId.ToString(), result.PersonId.ToString(), result.KioskId, result.RecoveryCode, result.Session!.Kind.ToDto()));
            })
            .WithName("JoinWithRoleCode")
            .Produces<JoinResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
    }

    private static async Task<JoinPreviewResponse> ToResponseAsync(JoinPreview preview, string? role, HttpRequest request, IExternalLoginFlow external, IProviderCatalog providers, CancellationToken ct)
    {
        var pending = external.ReadPending(request);
        PendingExternalResponse? pendingResponse = null;
        if (pending is not null)
        {
            var provider = preview.Ways.Providers.FirstOrDefault(p => p.Key == pending.ProviderKey) ?? await providers.FindAsync(pending.ProviderKey, ct);
            pendingResponse = new PendingExternalResponse(pending.ProviderKey, provider?.DisplayName ?? pending.ProviderKey);
        }

        return new JoinPreviewResponse(
            preview.TenantId.ToString(),
            preview.TenantName,
            new JoinWaysResponse(preview.Ways.Passkey, preview.Ways.MagicLink, preview.Ways.Kiosk, preview.Ways.Providers.Select(p => new ProviderResponse(p.Key, p.DisplayName)).ToList(), preview.Ways.ForcedProviderKeys),
            pendingResponse,
            role);
    }
}
