using System.Globalization;
using System.Security.Claims;
using System.Text.Encodings.Web;
using CompanyHero.Modules.Identity.Application.Kiosk;
using CompanyHero.Modules.Identity.Application.Sessions;
using CompanyHero.Platform.Tenancy;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CompanyHero.Modules.Identity.Infrastructure.Http;

/// <summary>
/// Die serverseitige Cookie-Sitzung (A-007): liest das Sitzungs- beziehungsweise Gerätegeheimnis aus dem Cookie, prüft es im
/// Plattformkontext gegen PostgreSQL (Ablauf, Widerruf, Verlängerung) und hängt nur Tenant, Person, Sitzungsart,
/// Anmeldezeitpunkt und Gerät als Claims an. Mitgliedschaft und Rollen prüft danach die Plattform-Middleware.
/// Zustandsändernde Requests (POST, PUT, PATCH, DELETE) gelten nur mit passendem CSRF-Header als angemeldet (A-007).
/// Anmelden über dieses Schema gibt es nicht; Sitzungen entstehen ausschließlich über die Anmeldewege des Moduls.
/// </summary>
internal sealed class SessionAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, ITenantScopeFactory scopes)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder), IAuthenticationSignOutHandler
{
    public const string SessionIdClaim = "ch:session_id";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var unsafeMethod = !HttpMethods.IsGet(Request.Method) && !HttpMethods.IsHead(Request.Method) && !HttpMethods.IsOptions(Request.Method);
        var csrfHeader = Request.Headers[SessionCookies.CsrfHeader].ToString();

        if (Request.Cookies.TryGetValue(SessionCookies.Session, out var token) && !string.IsNullOrEmpty(token))
        {
            var session = await scopes.RunAsync(TenantContext.ForPlatform(), (sp, ct) => sp.GetRequiredService<ISessionService>().AuthenticateAsync(token, ct), Context.RequestAborted);
            if (session is not null)
            {
                if (unsafeMethod && !string.Equals(csrfHeader, session.CsrfToken, StringComparison.Ordinal))
                {
                    Logger.LogWarning("Zustandsändernder Request ohne gültiges CSRF-Token; Sitzung nicht angenommen.");
                    return AuthenticateResult.Fail("CSRF-Token fehlt oder passt nicht.");
                }

                var claims = new List<Claim>
                {
                    new(CompanyHeroClaims.Tenant, session.TenantId.ToString()),
                    new(CompanyHeroClaims.Session, session.Kind.ToString()),
                    new(CompanyHeroClaims.AuthenticatedAt, session.AuthenticatedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)),
                    new(SessionIdClaim, session.Id.ToString("D")),
                };
                if (session.PersonId is { } person)
                {
                    claims.Add(new Claim(CompanyHeroClaims.Person, person.ToString()));
                }

                if (session.KioskDeviceId is { } device)
                {
                    claims.Add(new Claim(CompanyHeroClaims.KioskDevice, device.ToString("D")));
                }

                return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name));
            }
        }

        if (Request.Cookies.TryGetValue(SessionCookies.KioskDevice, out var secret) && !string.IsNullOrEmpty(secret))
        {
            var device = await scopes.RunAsync(TenantContext.ForPlatform(), (sp, ct) => sp.GetRequiredService<IKioskService>().AuthenticateDeviceAsync(secret, ct), Context.RequestAborted);
            if (device is not null)
            {
                if (unsafeMethod && !string.Equals(csrfHeader, SessionCookies.DeviceCsrfToken(secret), StringComparison.Ordinal))
                {
                    return AuthenticateResult.Fail("CSRF-Token fehlt oder passt nicht.");
                }

                if (!string.IsNullOrEmpty(token))
                {
                    // Die Personensitzung ist beendet (Auto-Logout, absolutes Ende, Widerruf): ihr Cookie fällt, das Gerät bleibt angemeldet.
                    SessionCookies.ClearStaleSession(Response);
                }

                if (device.RotatedSecret is not null)
                {
                    // Automatische Rotation alle 30 Tage (Zugang 6.1): neues Geheimnis mit dieser Antwort.
                    SessionCookies.SetKioskDevice(Response, device.RotatedSecret);
                }

                var claims = new[]
                {
                    new Claim(CompanyHeroClaims.Tenant, device.TenantId.ToString()),
                    new Claim(CompanyHeroClaims.Session, SessionKind.KioskDevice.ToString()),
                    new Claim(CompanyHeroClaims.KioskDevice, device.DeviceId.ToString("D")),
                };
                return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name));
            }
        }

        return AuthenticateResult.NoResult();
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        // Die Anmeldeseite gehört dem Frontend; die API antwortet mit 401 (K11: der Client behandelt 401).
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }

    public Task SignOutAsync(AuthenticationProperties? properties)
    {
        SessionCookies.ClearSession(Response);
        return Task.CompletedTask;
    }
}
