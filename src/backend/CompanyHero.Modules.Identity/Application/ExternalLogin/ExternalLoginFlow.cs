using System.Globalization;
using CompanyHero.Modules.Identity.Application.Providers;
using CompanyHero.Modules.Identity.Application.Sessions;
using CompanyHero.Modules.Identity.Domain;
using CompanyHero.Modules.Identity.Infrastructure;
using CompanyHero.Modules.Identity.Infrastructure.Http;
using CompanyHero.Platform.Data;
using CompanyHero.Platform.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace CompanyHero.Modules.Identity.Application.ExternalLogin;

/// <summary>Absicht eines Anbieter-Logins (in den geschützten State der Middleware): Anmeldung oder Verknüpfung aus bestehender Sitzung.</summary>
public static class ExternalIntent
{
    public const string ItemKey = "ch:intent";
    public const string ReturnKey = "ch:return";
    public const string LinkTenantKey = "ch:link_tenant";
    public const string LinkPersonKey = "ch:link_person";
    public const string Login = "login";
    public const string Link = "link";
}

/// <summary>Geprüfte, noch keiner Person zugeordnete Anbieteridentität, für den anschließenden Beitritt (Zugang 3.2).</summary>
public sealed record PendingExternal(string ProviderKey, string SubjectHash, DateTimeOffset ExpiresAt);

/// <summary>
/// Abschluss eines Anbieter-Logins nach der Prüfung durch die Middleware (Zugang 3.2, A-007): Der Identitätsindex ordnet den
/// Hash genau einer Person zu; fehlt die Zuordnung, führt der Weg in den Beitritt (Übergabe als geschütztes Cookie) oder,
/// aus einer bestehenden Sitzung, in die Verknüpfung. Claims werden verworfen; nur Issuer und Subject werden gehasht.
/// </summary>
public interface IExternalLoginFlow
{
    /// <summary>Liefert das Redirect-Ziel für den Browser und setzt die nötigen Cookies.</summary>
    Task<string> CompleteAsync(string providerKey, string issuer, string subject, AuthenticationProperties properties, HttpContext http, CancellationToken cancellationToken);

    /// <summary>Übergabe aus dem Cookie lesen; <c>null</c>, wenn keine gültige vorliegt.</summary>
    PendingExternal? ReadPending(HttpRequest request);

    void ClearPending(HttpResponse response);

    /// <summary>Plattformkontext: Person zur Anbieteridentität; <c>null</c>, wenn keine aktive Zuordnung besteht.</summary>
    Task<Locator?> FindPersonAsync(string subjectHash, CancellationToken cancellationToken);
}

internal sealed class ExternalLoginFlow(ITenantScopeFactory scopes, IProviderCatalog providers, IDataProtectionProvider dataProtection, TimeProvider clock) : IExternalLoginFlow
{
    private static readonly TimeSpan HandoffLifetime = TimeSpan.FromMinutes(15);
    private readonly IDataProtector _protector = dataProtection.CreateProtector("CompanyHero.Identity.ExternalHandoff");

    public async Task<string> CompleteAsync(string providerKey, string issuer, string subject, AuthenticationProperties properties, HttpContext http, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(properties);
        var hash = IdentityIndexEntry.HashExternal(issuer, subject);
        var intent = properties.Items.TryGetValue(ExternalIntent.ItemKey, out var i) ? i : ExternalIntent.Login;
        var returnUrl = properties.Items.TryGetValue(ExternalIntent.ReturnKey, out var r) && IsLocal(r) ? r! : "/t/_/start";

        if (intent == ExternalIntent.Link
            && properties.Items.TryGetValue(ExternalIntent.LinkTenantKey, out var t) && Guid.TryParseExact(t, "D", out var tenantGuid)
            && properties.Items.TryGetValue(ExternalIntent.LinkPersonKey, out var p) && Guid.TryParseExact(p, "D", out var personGuid))
        {
            return await LinkAsync(new TenantId(tenantGuid), new PersonId(personGuid), providerKey, hash, returnUrl, cancellationToken);
        }

        var found = await FindPersonAsync(hash, cancellationToken);
        if (found is null)
        {
            // Keine Zuordnung: der Weg führt in den Beitritt mit Code (Zugang 3.2, 10.9); die geprüfte Identität wird geschützt übergeben.
            var expires = clock.GetUtcNow() + HandoffLifetime;
            var payload = string.Join('\n', providerKey, hash, expires.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
            SessionCookies.SetHandoff(http.Response, _protector.Protect(payload), expires);
            return "/zugang/beitritt?extern=1";
        }

        var (tenantId, personId) = found;
        var session = await scopes.RunAsync(TenantContext.ForTenant(tenantId), async (sp, ct) =>
        {
            var tx = await sp.GetRequiredService<IContextTransaction>().BeginAsync(ct);
            await using (tx)
            {
                var db = sp.GetRequiredService<IdentityDbContext>();
                var policy = await WayQueries.PolicyAsync(db, tenantId, clock.GetUtcNow(), ct);
                var linked = await db.ExternalLogins.AnyAsync(e => e.TenantId == tenantId && e.PersonId == personId && e.SubjectHash == hash, ct);
                if (!linked || !policy.ProviderAllowed(providerKey))
                {
                    await Login.LoginService.Security(sp, "login.external", false, personId, "provider_not_allowed", ct);
                    await tx.CommitAsync(ct);
                    return null;
                }

                var issued = await sp.GetRequiredService<ISessionService>().IssueAsync(personId, ct);
                await Login.LoginService.Security(sp, "login.external", true, personId, providerKey, ct);
                await tx.CommitAsync(ct);
                return issued;
            }
        }, cancellationToken);

        if (session is null)
        {
            return "/zugang?fehler=weg";
        }

        SessionCookies.SetSession(http.Response, session.Token, session.CsrfToken, session.SlidingUntil);
        return returnUrl;
    }

    public PendingExternal? ReadPending(HttpRequest request)
    {
        if (!request.Cookies.TryGetValue(SessionCookies.ExternalHandoff, out var value) || string.IsNullOrEmpty(value))
        {
            return null;
        }

        try
        {
            var parts = _protector.Unprotect(value).Split('\n');
            var expires = DateTimeOffset.FromUnixTimeSeconds(long.Parse(parts[2], CultureInfo.InvariantCulture));
            return expires < clock.GetUtcNow() ? null : new PendingExternal(parts[0], parts[1], expires);
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or FormatException or IndexOutOfRangeException)
        {
            return null;
        }
    }

    public void ClearPending(HttpResponse response) => SessionCookies.ClearHandoff(response);

    public Task<Locator?> FindPersonAsync(string subjectHash, CancellationToken cancellationToken) =>
        scopes.RunAsync(TenantContext.ForPlatform(), async (sp, ct) =>
        {
            var db = sp.GetRequiredService<IdentityDbContext>();
            var tx = await sp.GetRequiredService<IContextTransaction>().BeginAsync(ct);
            await using (tx)
            {
                var row = await db.IdentityIndex.AsNoTracking().Where(x => x.Hash == subjectHash).Select(x => new Locator(x.TenantId, x.PersonId)).FirstOrDefaultAsync(ct);
                await tx.CommitAsync(ct);
                return row;
            }
        }, cancellationToken);

    private async Task<string> LinkAsync(TenantId tenantId, PersonId personId, string providerKey, string hash, string returnUrl, CancellationToken cancellationToken)
    {
        _ = providers;
        var outcome = await scopes.RunAsync(TenantContext.ForTenant(tenantId), async (sp, ct) =>
        {
            var db = sp.GetRequiredService<IdentityDbContext>();
            var tx = await sp.GetRequiredService<IContextTransaction>().BeginAsync(ct);
            await using (tx)
            {
                var now = clock.GetUtcNow();
                // Dieselbe externe Identität kann keiner zweiten aktiven Person zugeordnet werden (Zugang 10.4): der Index ist plattformweit eindeutig.
                var taken = await db.IdentityIndex.AnyAsync(x => x.Hash == hash, ct);
                if (taken)
                {
                    await tx.CommitAsync(ct);
                    return "identitaet_vergeben";
                }

                db.ExternalLogins.Add(Domain.ExternalLogin.Link(tenantId, personId, providerKey, hash, now));
                db.IdentityIndex.Add(IdentityIndexEntry.Create(hash, IdentityKind.External, tenantId, personId, now));
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return "verknuepft";
            }
        }, cancellationToken);
        return returnUrl + (returnUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?") + "anbieter=" + outcome;
    }

    private static bool IsLocal(string? url) => url is { Length: > 0 } && url[0] == '/' && (url.Length == 1 || (url[1] != '/' && url[1] != '\\'));
}
