using CompanyHero.Modules.Identity.Application.Account;
using CompanyHero.Modules.Identity.Application.ExternalLogin;
using CompanyHero.Modules.Identity.Application.Login;
using CompanyHero.Modules.Identity.Application.Providers;
using CompanyHero.Modules.Identity.Application.Sessions;
using CompanyHero.Modules.Identity.Infrastructure.Http;
using CompanyHero.Modules.Identity.Infrastructure.Oidc;
using CompanyHero.Platform.Hosting;
using CompanyHero.Platform.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CompanyHero.Modules.Identity.Api;

/// <summary>Anmeldung und Sitzung (Zugang 3, 5): Passkey, Wiederherstellungscode, Magic-Link, Übertragung, externe Anbieter, Abmeldung.</summary>
internal static class AuthEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var auth = endpoints.MapGroup("/api/auth").HandleAccessErrors();

        auth.MapGet("/session", async (HttpContext http, ITenantContextAccessor context, ISessionService sessions, CancellationToken ct) =>
            {
                if (context.Current is null)
                {
                    // Geprüfte Sitzung ohne Kontext: gesperrter oder gekündigter Tenant zeigt Mitgliedern einen neutralen Hinweis (Organisation 1.3).
                    return http.Items.TryGetValue(TenantContextMiddleware.DeniedReasonItem, out var reason) && reason is string denied
                        ? Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Kein Zugang", detail: denied)
                        : Results.Unauthorized();
                }

                var current = context.Require();
                if (current.Kind != TenantContextKind.Tenant)
                {
                    return Results.Forbid();
                }

                if (current.Session == SessionKind.KioskDevice)
                {
                    return Results.Ok(new SessionResponse(SessionKindDto.KioskDevice, current.RequireTenant().ToString(), null, DateTimeOffset.MinValue, null, null, DateTimeOffset.MinValue, current.KioskDeviceId?.ToString("D")));
                }

                var id = AccessEndpointHelpers.SessionId(http);
                var session = id is null ? null : await sessions.GetAsync(id.Value, ct);
                if (session is null)
                {
                    return Results.Unauthorized();
                }

                return Results.Ok(new SessionResponse(
                    session.Kind.ToDto(), session.TenantId.ToString(), session.PersonId?.ToString(), session.AuthenticatedAt, session.SlidingUntil, session.AbsoluteUntil,
                    session.AuthenticatedAt + TenantContextEndpointExtensions.FreshLoginMaxAge, session.KioskDeviceId?.ToString("D")));
            })
            .WithName("GetSession")
            .Produces<SessionResponse>()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        auth.MapPost("/logout", async (HttpContext http, ISessionService sessions, CancellationToken ct) =>
            {
                if (AccessEndpointHelpers.SessionId(http) is { } id)
                {
                    await sessions.RevokeAsync(id, ct);
                }

                SessionCookies.ClearSessionKeepingDevice(http.Request, http.Response);
                return Results.NoContent();
            })
            .RequireTenantContext().AllowKiosk()
            .WithName("Logout")
            .Produces(StatusCodes.Status204NoContent);

        auth.MapPost("/logout-all", async (HttpContext http, IAccountService account, CancellationToken ct) =>
            {
                var count = await account.LogoutEverywhereAsync(ct);
                SessionCookies.ClearSession(http.Response);
                return Results.Ok(new CountResponse(count));
            })
            .RequireTenantContext()
            .WithName("LogoutEverywhere")
            .Produces<CountResponse>();

        auth.MapGet("/providers", async (string? tenant, IProviderCatalog providers, ITenantScopeFactory scopes, CancellationToken ct) =>
            {
                IReadOnlyList<ProviderRecord> list = Guid.TryParseExact(tenant, "D", out var tenantId)
                    ? await scopes.RunAsync(TenantContext.ForPlatform(), (sp, c) => sp.GetRequiredService<IProviderCatalog>().ListForTenantAsync(new TenantId(tenantId), c), ct)
                    : providers.Platform();
                return Results.Ok(list.Select(p => new ProviderResponse(p.Key, p.DisplayName)).ToList());
            })
            .WithName("ListLoginProviders")
            .Produces<List<ProviderResponse>>();

        // Start des Anbieter-Logins: die Middleware leitet zum Anbieter (Authorization Code Flow mit PKCE); Callback unter /api/auth/oidc/{key}/callback.
        auth.MapGet("/oidc/{key}/start", async (string key, string? intent, string? returnUrl, HttpContext http, ITenantContextAccessor context, OidcSchemeRegistry registry, CancellationToken ct) =>
            {
                if (!AccessEndpointHelpers.ValidKey(key) || !await registry.EnsureAsync(key, ct))
                {
                    return Results.NotFound();
                }

                var properties = new AuthenticationProperties();
                properties.Items[ExternalIntent.ReturnKey] = AccessEndpointHelpers.IsLocalUrl(returnUrl) ? returnUrl : "/t/_/start";
                if (string.Equals(intent, ExternalIntent.Link, StringComparison.Ordinal))
                {
                    var current = context.Current;
                    if (current is null || current.IsKiosk || current.PersonId is null)
                    {
                        return Results.Unauthorized();
                    }

                    properties.Items[ExternalIntent.ItemKey] = ExternalIntent.Link;
                    properties.Items[ExternalIntent.LinkTenantKey] = current.RequireTenant().ToString();
                    properties.Items[ExternalIntent.LinkPersonKey] = current.PersonId.Value.ToString();
                }
                else
                {
                    properties.Items[ExternalIntent.ItemKey] = ExternalIntent.Login;
                }

                await http.ChallengeAsync(OidcPaths.Scheme(key), properties);
                return Results.Empty;
            })
            .WithName("StartExternalLogin")
            .Produces(StatusCodes.Status302Found)
            .Produces(StatusCodes.Status404NotFound);

        auth.MapPost("/passkey/options", (ILoginService login) =>
            {
                var ceremony = login.BeginPasskey();
                return Results.Ok(new PasskeyCeremonyResponse(ceremony.Options, ceremony.State));
            })
            .WithName("BeginPasskeyLogin")
            .Produces<PasskeyCeremonyResponse>();

        auth.MapPost("/passkey", async (PasskeyAnswerRequest request, HttpContext http, ILoginService login, CancellationToken ct) =>
            {
                var result = await login.CompletePasskeyAsync(request.State, request.Credential, ct);
                AccessEndpointHelpers.ApplySession(http.Response, result.Session);
                return Results.Ok(AccessEndpointHelpers.ToLoginResponse(result.Session, null, false));
            })
            .WithName("CompletePasskeyLogin")
            .Produces<LoginResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        auth.MapPost("/recovery", async (RecoveryLoginRequest request, HttpContext http, ILoginService login, CancellationToken ct) =>
            {
                var result = await login.RecoveryAsync(request.Code, ct);
                AccessEndpointHelpers.ApplySession(http.Response, result.Session);
                return Results.Ok(AccessEndpointHelpers.ToLoginResponse(result.Session, result.NewRecoveryCode, result.MustSetUpAccess));
            })
            .WithName("RecoveryCodeLogin")
            .Produces<LoginResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        auth.MapPost("/magic-link", async (MagicLinkRequest request, ILoginService login, CancellationToken ct) =>
            {
                await login.RequestMagicLinkAsync(request.Email ?? string.Empty, ct);
                // Keine Auskunft, ob die Adresse bekannt ist.
                return Results.Accepted();
            })
            .WithName("RequestMagicLink")
            .Produces(StatusCodes.Status202Accepted);

        auth.MapPost("/magic-link/consume", async (TokenRequest request, HttpContext http, ILoginService login, CancellationToken ct) =>
            {
                var result = await login.ConsumeMagicLinkAsync(request.Token, ct);
                AccessEndpointHelpers.ApplySession(http.Response, result.Session);
                return Results.Ok(AccessEndpointHelpers.ToLoginResponse(result.Session, null, false));
            })
            .WithName("ConsumeMagicLink")
            .Produces<LoginResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        auth.MapPost("/transfer", async (TokenRequest request, HttpContext http, ILoginService login, CancellationToken ct) =>
            {
                var result = await login.ConsumeTransferAsync(request.Token, ct);
                AccessEndpointHelpers.ApplySession(http.Response, result.Session);
                return Results.Ok(AccessEndpointHelpers.ToLoginResponse(result.Session, null, result.MustSetUpAccess));
            })
            .WithName("ConsumeTransfer")
            .Produces<LoginResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);
    }
}
