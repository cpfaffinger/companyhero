using System.Security.Claims;
using System.Text.Encodings.Web;
using CompanyHero.Platform.Tenancy;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CompanyHero.Integration.Tests;

/// <summary>
/// Ersatz für die serverseitige Cookie-Sitzung, die erst in Stufe 4 entsteht (A-007). Der Handler liefert nur das,
/// was eine geprüfte Sitzung liefern würde: Tenant- und Personenkennung. Mitgliedschaft, Tenant-Zustand und Rollen
/// prüft der produktive Pfad (TenantContextMiddleware, IMembershipVerification). Nur im Testprojekt registriert.
/// </summary>
internal sealed class TestSessionHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "TestSession";
    public const string Header = "X-Test-Session";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(Header, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim>();
        foreach (var part in raw.ToString().Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var kv = part.Split('=', 2);
            var (key, value) = kv.Length == 2 ? (kv[0], kv[1]) : (kv[0], string.Empty);
            switch (key)
            {
                case "tenant":
                    claims.Add(new Claim(CompanyHeroClaims.Tenant, value));
                    break;
                case "person":
                    claims.Add(new Claim(CompanyHeroClaims.Person, value));
                    break;
                case "platform":
                    claims.Add(new Claim(CompanyHeroClaims.Context, CompanyHeroClaims.ContextPlatform));
                    break;
                default:
                    return Task.FromResult(AuthenticateResult.Fail($"Unbekannter Sitzungsbestandteil '{key}'."));
            }
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

internal static class TestSession
{
    public static string For(TenantId tenant, PersonId person) => $"tenant={tenant};person={person}";

    public static string Platform() => "platform";
}
