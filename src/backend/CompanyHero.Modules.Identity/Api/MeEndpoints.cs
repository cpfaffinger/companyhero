using CompanyHero.Modules.Identity.Application.Account;
using CompanyHero.Modules.Identity.Application.Join;
using CompanyHero.Modules.Identity.Infrastructure.Http;
using CompanyHero.Platform.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CompanyHero.Modules.Identity.Api;

/// <summary>Konto der angemeldeten Person (Zugang 4, 8): Wege, Wiederherstellungscode, Kiosk-Kennung, Austritt.</summary>
internal static class MeEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var me = endpoints.MapGroup("/api/me").HandleAccessErrors();

        me.MapGet("/access", async (IAccountService account, CancellationToken ct) =>
            {
                var o = await account.GetOverviewAsync(ct);
                return Results.Ok(new AccessOverviewResponse(
                    o.Passkeys.Select(p => new PasskeyResponse(p.Id.ToString("D"), p.DeviceName, p.CreatedAt, p.LastUsedAt)).ToList(),
                    o.Email,
                    o.Providers.Select(p => new LinkedProviderResponse(p.Id.ToString("D"), p.ProviderKey, p.DisplayName, p.CreatedAt)).ToList(),
                    o.RecoveryCodeActive,
                    o.KioskId,
                    o.KioskPinSet,
                    o.AvailableProviders.Select(p => new ProviderResponse(p.Key, p.DisplayName)).ToList()));
            })
            .RequireTenantContext()
            .WithName("GetMyAccess")
            .Produces<AccessOverviewResponse>();

        me.MapPost("/passkeys/options", async (JoinPasskeyOptionsRequest request, IAccountService account, CancellationToken ct) =>
            {
                var exclude = await account.CredentialIdsAsync(ct);
                var ceremony = account.BeginPasskey(request.DisplayName, exclude);
                return Results.Ok(new PasskeyCeremonyResponse(ceremony.Options, ceremony.State));
            })
            .RequireTenantContext()
            .WithName("BeginMyPasskey")
            .Produces<PasskeyCeremonyResponse>();

        me.MapPost("/passkeys", async (PasskeyAnswerRequest request, IAccountService account, CancellationToken ct) =>
            {
                var added = await account.AddPasskeyAsync(new PasskeyAnswer(request.State, request.Credential, request.DeviceName), ct);
                return Results.Created($"/api/me/passkeys/{added.Id:D}", new PasskeyResponse(added.Id.ToString("D"), added.DeviceName, added.CreatedAt, added.LastUsedAt));
            })
            .RequireTenantContext()
            .WithName("AddMyPasskey")
            .Produces<PasskeyResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        me.MapDelete("/passkeys/{passkeyId:guid}", async (Guid passkeyId, IAccountService account, CancellationToken ct) =>
            {
                await account.RemovePasskeyAsync(passkeyId, ct);
                return Results.NoContent();
            })
            .RequireTenantContext()
            .WithName("RemoveMyPasskey")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status409Conflict);

        me.MapPut("/email", async (EmailRequest request, IAccountService account, CancellationToken ct) =>
            {
                await account.SetEmailAsync(request.Email ?? string.Empty, ct);
                return Results.NoContent();
            })
            .RequireTenantContext()
            .WithName("SetMyEmail")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        me.MapDelete("/email", async (IAccountService account, CancellationToken ct) =>
            {
                await account.RemoveEmailAsync(ct);
                return Results.NoContent();
            })
            .RequireTenantContext()
            .WithName("RemoveMyEmail")
            .Produces(StatusCodes.Status204NoContent);

        me.MapDelete("/providers/{linkId:guid}", async (Guid linkId, IAccountService account, CancellationToken ct) =>
            {
                await account.UnlinkProviderAsync(linkId, ct);
                return Results.NoContent();
            })
            .RequireTenantContext()
            .WithName("UnlinkMyProvider")
            .Produces(StatusCodes.Status204NoContent);

        me.MapPost("/recovery-code", async (IAccountService account, CancellationToken ct) =>
                Results.Ok(new RecoveryCodeResponse(await account.RenewRecoveryCodeAsync(ct))))
            .RequireTenantContext()
            .WithName("RenewMyRecoveryCode")
            .Produces<RecoveryCodeResponse>();

        me.MapPost("/leave", async (HttpContext http, IAccountService account, CancellationToken ct) =>
            {
                await account.LeaveAsync(ct);
                SessionCookies.ClearSession(http.Response);
                return Results.NoContent();
            })
            .RequireTenantContext()
            .WithName("LeaveTenant")
            .Produces(StatusCodes.Status204NoContent);
    }
}
