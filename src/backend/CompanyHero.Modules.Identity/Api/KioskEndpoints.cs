using CompanyHero.Modules.Identity.Application.Kiosk;
using CompanyHero.Modules.Identity.Infrastructure.Http;
using CompanyHero.Platform.Hosting;
using CompanyHero.Platform.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CompanyHero.Modules.Identity.Api;

/// <summary>Kiosk (Zugang 6): Registrierung, Gerätezustand, Anmeldung mit Kennung und PIN, PIN, Übertragung. Keine Namensliste, keine Suche.</summary>
internal static class KioskEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var kiosk = endpoints.MapGroup("/api/kiosk").HandleAccessErrors();

        kiosk.MapPost("/register", async (KioskRegisterRequest request, HttpContext http, IKioskService kiosks, CancellationToken ct) =>
            {
                var (state, secret) = await kiosks.RegisterAsync(request.Code, ct);
                SessionCookies.SetKioskDevice(http.Response, secret);
                return Results.Ok(new KioskDeviceResponse(state.TenantId.ToString(), state.DeviceId.ToString("D"), state.Name, state.IdleSeconds));
            })
            .WithName("RegisterKioskDevice")
            .Produces<KioskDeviceResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        kiosk.MapGet("/device", async (IKioskService kiosks, CancellationToken ct) =>
            {
                var state = await kiosks.GetDeviceStateAsync(ct);
                return Results.Ok(new KioskDeviceResponse(state.TenantId.ToString(), state.DeviceId.ToString("D"), state.Name, state.IdleSeconds));
            })
            .RequireTenantContext().AllowKiosk(device: true)
            .WithName("GetKioskDevice")
            .Produces<KioskDeviceResponse>();

        kiosk.MapPost("/login", async (KioskLoginRequest request, HttpContext http, ITenantContextAccessor context, IKioskService kiosks, CancellationToken ct) =>
            {
                // Anmeldung aus der Gerätesitzung oder Personenwechsel aus einer laufenden Personensitzung (A-005); nie ohne Gerät (Zugang 6.4).
                if (context.Require().KioskDeviceId is null)
                {
                    return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Zugang abgelehnt", detail: "kiosk_device_session_required");
                }

                var deviceSecret = http.Request.Cookies[SessionCookies.KioskDevice] ?? string.Empty;
                var login = await kiosks.LoginAsync(request.KioskId ?? string.Empty, request.Pin ?? string.Empty, SessionCookies.DeviceCsrfToken(deviceSecret), ct);
                AccessEndpointHelpers.ApplySession(http.Response, login.Session);
                return Results.Ok(new KioskLoginResponse(login.Session.PersonId!.Value.ToString(), login.DisplayName, login.IdleSeconds, login.Session.SlidingUntil!.Value, login.Session.AbsoluteUntil!.Value));
            })
            .RequireTenantContext().AllowKiosk(device: true)
            .WithName("KioskLogin")
            .Produces<KioskLoginResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status423Locked);

        kiosk.MapPost("/pin", async (PinRequest request, IKioskService kiosks, CancellationToken ct) =>
            {
                await kiosks.ChangePinAsync(request.Pin ?? string.Empty, ct);
                return Results.NoContent();
            })
            .RequireTenantContext().AllowKiosk()
            .WithName("ChangeKioskPin")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        kiosk.MapPost("/pin/reset", async (PinResetRequest request, IKioskService kiosks, CancellationToken ct) =>
            {
                await kiosks.ResetPinAsync(request.KioskId ?? string.Empty, request.RecoveryCode ?? string.Empty, request.Pin ?? string.Empty, ct);
                return Results.NoContent();
            })
            .RequireTenantContext().AllowKiosk(device: true)
            .WithName("ResetKioskPin")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        kiosk.MapPost("/transfer", async (IKioskService kiosks, CancellationToken ct) =>
            {
                var transfer = await kiosks.CreateTransferAsync(ct);
                return Results.Ok(new TransferResponse(transfer.Url, transfer.ExpiresAt));
            })
            .RequireTenantContext().AllowKiosk()
            .WithName("CreateKioskTransfer")
            .Produces<TransferResponse>();
    }
}
