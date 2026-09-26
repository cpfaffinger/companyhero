using System.Text.Json;
using CompanyHero.Modules.Identity.Application.Passkeys;
using CompanyHero.Modules.Identity.Application.Sessions;
using CompanyHero.Modules.Identity.Domain;
using CompanyHero.Modules.Identity.Infrastructure;
using CompanyHero.Modules.Organisation.Application;
using CompanyHero.Platform.Audit;
using CompanyHero.Platform.Data;
using CompanyHero.Platform.Messaging;
using CompanyHero.Platform.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CompanyHero.Modules.Identity.Application.Login;

/// <summary>Ergebnis einer Anmeldung: Sitzung plus Hinweise für die Oberfläche (neuer Wiederherstellungscode, Einrichtung nötig).</summary>
public sealed record LoginResult(IssuedSession Session, string? NewRecoveryCode, bool MustSetUpAccess);

/// <summary>
/// Anmeldewege ohne Anbieter (Zugang 3.1): Passkey, Wiederherstellungscode, Magic-Link, Übertragung vom Kiosk. Der Weg wird
/// im Plattformkontext gefunden (die Person ist noch keinem Tenant zugeordnet) und im Tenant-Kontext abgeschlossen.
/// </summary>
public interface ILoginService
{
    PasskeyCeremony BeginPasskey();

    Task<LoginResult> CompletePasskeyAsync(string state, JsonElement credential, CancellationToken cancellationToken);

    Task<LoginResult> RecoveryAsync(string code, CancellationToken cancellationToken);

    /// <summary>Immer erfolgreich aus Sicht des Aufrufers (keine Auskunft, ob die Adresse bekannt ist).</summary>
    Task RequestMagicLinkAsync(string email, CancellationToken cancellationToken);

    Task<LoginResult> ConsumeMagicLinkAsync(string token, CancellationToken cancellationToken);

    Task<LoginResult> ConsumeTransferAsync(string token, CancellationToken cancellationToken);
}

internal sealed class LoginService(
    ITenantScopeFactory scopes,
    IPasskeyService passkeys,
    IAccountMailSender mail,
    IOptions<IdentityOptions> options,
    TimeProvider clock) : ILoginService
{
    public PasskeyCeremony BeginPasskey() => passkeys.BeginAssertion();

    public async Task<LoginResult> CompletePasskeyAsync(string state, JsonElement credential, CancellationToken cancellationToken)
    {
        var credentialId = passkeys.CredentialIdOf(credential);
        var (tenantId, personId) = await FindAsync(
            q => q.Passkeys.Where(p => p.CredentialId == credentialId).Select(p => new Locator(p.TenantId, p.PersonId)),
            cancellationToken) ?? throw new AccessDeniedException("passkey_unknown", 401);

        return await scopes.RunAsync(TenantContext.ForTenant(tenantId), async (sp, ct) =>
        {
            var tdb = sp.GetRequiredService<IdentityDbContext>();
            var tx = await sp.GetRequiredService<IContextTransaction>().BeginAsync(ct);
            await using (tx)
            {
                var now = clock.GetUtcNow();
                var passkey = await tdb.Passkeys.SingleAsync(p => p.TenantId == tenantId && p.CredentialId == credentialId, ct);
                var handle = personId.Value.ToByteArray();
                var (_, count) = await sp.GetRequiredService<IPasskeyService>().CompleteAssertionAsync(state, credential, (_, _) => Task.FromResult<StoredPasskey?>(new StoredPasskey(passkey.PublicKey, passkey.SignCount, handle)), ct);
                var roles = await sp.GetRequiredService<IOrganisationDirectory>().GetRolesAsync(personId, ct);
                var policy = await WayQueries.PolicyAsync(tdb, tenantId, now, ct);
                var ways = await WayQueries.WaysOfAsync(tdb, tenantId, personId, ct);
                // Passkey bleibt für privilegierte Rollen immer erlaubt (Zugang 3.4).
                if (!AccessRules.IsPrivileged(roles) && !policy.WayOpen(LoginWay.Passkey, ways is { Email: false, External: false }, now))
                {
                    await Security(sp, "login.passkey", false, personId, "way_disabled", ct);
                    await tx.CommitAsync(ct);
                    throw new AccessDeniedException("way_disabled");
                }

                passkey.Used(count, now);
                await tdb.SaveChangesAsync(ct);
                var session = await sp.GetRequiredService<ISessionService>().IssueAsync(personId, ct);
                await Security(sp, "login.passkey", true, personId, null, ct);
                await tx.CommitAsync(ct);
                return new LoginResult(session, null, false);
            }
        }, cancellationToken);
    }

    public async Task<LoginResult> RecoveryAsync(string code, CancellationToken cancellationToken)
    {
        var normalized = AccessCodes.Normalize(code ?? string.Empty);
        if (!AccessCodes.IsWellFormed(normalized, AccessCodes.RecoveryCodeLength))
        {
            throw new AccessDeniedException("recovery_code_invalid", 401);
        }

        var hash = AccessCodes.Hash(normalized);
        var (tenantId, personId) = await FindAsync(q => q.RecoveryCodes.Where(r => r.CodeHash == hash).Select(r => new Locator(r.TenantId, r.PersonId)), cancellationToken)
            ?? throw new AccessDeniedException("recovery_code_invalid", 401);

        return await scopes.RunAsync(TenantContext.ForTenant(tenantId), async (sp, ct) =>
        {
            var tdb = sp.GetRequiredService<IdentityDbContext>();
            var tx = await sp.GetRequiredService<IContextTransaction>().BeginAsync(ct);
            await using (tx)
            {
                var now = clock.GetUtcNow();
                var entry = await tdb.RecoveryCodes.SingleAsync(r => r.TenantId == tenantId && r.PersonId == personId, ct);
                if (!entry.Matches(normalized))
                {
                    throw new AccessDeniedException("recovery_code_invalid", 401);
                }

                // Einmalig: nach Verwendung wird sofort ein neuer Code erzeugt und angezeigt (Zugang 3.1).
                var renewed = entry.Renew(now);
                await tdb.SaveChangesAsync(ct);
                var session = await sp.GetRequiredService<ISessionService>().IssueAsync(personId, ct);
                await Security(sp, "login.recovery_code", true, personId, null, ct);
                await tx.CommitAsync(ct);
                return new LoginResult(session, AccessCodes.Grouped(renewed), MustSetUpAccess: true);
            }
        }, cancellationToken);
    }

    public async Task RequestMagicLinkAsync(string email, CancellationToken cancellationToken)
    {
        string hash;
        try
        {
            hash = IdentityIndexEntry.HashEmail(email);
        }
        catch (ArgumentException)
        {
            return;
        }

        var found = await FindAsync(q => q.IdentityIndex.Where(i => i.Hash == hash).Select(i => new Locator(i.TenantId, i.PersonId)), cancellationToken);
        if (found is null)
        {
            return;
        }

        var (tenantId, personId) = found;
        var link = await scopes.RunAsync(TenantContext.ForTenant(tenantId), async (sp, ct) =>
        {
            var tdb = sp.GetRequiredService<IdentityDbContext>();
            var tx = await sp.GetRequiredService<IContextTransaction>().BeginAsync(ct);
            await using (tx)
            {
                var now = clock.GetUtcNow();
                var login = await tdb.EmailLogins.SingleOrDefaultAsync(e => e.TenantId == tenantId && e.PersonId == personId, ct);
                var person = await tdb.Persons.SingleOrDefaultAsync(p => p.TenantId == tenantId && p.Id == personId, ct);
                if (login is null || person is null || person.State != PersonState.Active)
                {
                    await tx.CommitAsync(ct);
                    return null;
                }

                var (entry, token) = MagicLink.Issue(tenantId, personId, now);
                tdb.MagicLinks.Add(entry);
                await tdb.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return new { login.Email, Token = token, person.DisplayName };
            }
        }, cancellationToken);

        if (link is not null)
        {
            // Versand außerhalb der Transaktion; die Nachricht enthält nur Anrede und Link (Zugang 9).
            await mail.SendAsync(new AccountMail(link.Email, "konto.magic_link", options.Value.PublicOriginUri.GetLeftPart(UriPartial.Authority) + "/zugang/magic?token=" + Uri.EscapeDataString(link.Token), link.DisplayName), cancellationToken);
        }
    }

    public async Task<LoginResult> ConsumeMagicLinkAsync(string token, CancellationToken cancellationToken)
    {
        var hash = AccessCodes.Hash(token ?? string.Empty);
        var (tenantId, personId) = await FindAsync(q => q.MagicLinks.Where(m => m.TokenHash == hash).Select(m => new Locator(m.TenantId, m.PersonId)), cancellationToken)
            ?? throw new AccessDeniedException("magic_link_invalid", 401);

        return await scopes.RunAsync(TenantContext.ForTenant(tenantId), async (sp, ct) =>
        {
            var tdb = sp.GetRequiredService<IdentityDbContext>();
            var tx = await sp.GetRequiredService<IContextTransaction>().BeginAsync(ct);
            await using (tx)
            {
                var now = clock.GetUtcNow();
                var link = await tdb.MagicLinks.SingleAsync(m => m.TenantId == tenantId && m.TokenHash == hash, ct);
                if (!link.IsUsableAt(now))
                {
                    throw new AccessDeniedException("magic_link_invalid", 401);
                }

                var roles = await sp.GetRequiredService<IOrganisationDirectory>().GetRolesAsync(personId, ct);
                if (!AccessRules.MagicLinkAllowedFor(roles))
                {
                    // Magic-Link allein ist für privilegierte Rollen kein Anmeldeweg (Zugang 3.3).
                    link.Use(now);
                    await tdb.SaveChangesAsync(ct);
                    await Security(sp, "login.magic_link", false, personId, "magic_link_not_allowed", ct);
                    await tx.CommitAsync(ct);
                    throw new AccessDeniedException("magic_link_not_allowed");
                }

                var policy = await WayQueries.PolicyAsync(tdb, tenantId, now, ct);
                var ways = await WayQueries.WaysOfAsync(tdb, tenantId, personId, ct);
                if (!policy.WayOpen(LoginWay.MagicLink, ways is { Passkey: false, External: false }, now))
                {
                    await Security(sp, "login.magic_link", false, personId, "way_disabled", ct);
                    await tx.CommitAsync(ct);
                    throw new AccessDeniedException("way_disabled");
                }

                link.Use(now);
                await tdb.SaveChangesAsync(ct);
                var session = await sp.GetRequiredService<ISessionService>().IssueAsync(personId, ct);
                await Security(sp, "login.magic_link", true, personId, null, ct);
                await tx.CommitAsync(ct);
                return new LoginResult(session, null, false);
            }
        }, cancellationToken);
    }

    public async Task<LoginResult> ConsumeTransferAsync(string token, CancellationToken cancellationToken)
    {
        var hash = AccessCodes.Hash(token ?? string.Empty);
        var (tenantId, personId) = await FindAsync(q => q.TransferLinks.Where(t => t.TokenHash == hash).Select(t => new Locator(t.TenantId, t.PersonId)), cancellationToken)
            ?? throw new AccessDeniedException("transfer_invalid", 401);

        return await scopes.RunAsync(TenantContext.ForTenant(tenantId), async (sp, ct) =>
        {
            var tdb = sp.GetRequiredService<IdentityDbContext>();
            var tx = await sp.GetRequiredService<IContextTransaction>().BeginAsync(ct);
            await using (tx)
            {
                var now = clock.GetUtcNow();
                var link = await tdb.TransferLinks.SingleAsync(t => t.TenantId == tenantId && t.TokenHash == hash, ct);
                if (!link.IsUsableAt(now))
                {
                    throw new AccessDeniedException("transfer_invalid", 401);
                }

                link.Use(now);
                await tdb.SaveChangesAsync(ct);
                // Das Öffnen auf dem eigenen Gerät eröffnet dort eine Mitgliedssitzung und führt sofort zur Einrichtung (Zugang 6.4).
                var session = await sp.GetRequiredService<ISessionService>().IssueAsync(personId, ct);
                await Security(sp, "login.transfer", true, personId, null, ct);
                await tx.CommitAsync(ct);
                return new LoginResult(session, null, MustSetUpAccess: true);
            }
        }, cancellationToken);
    }

    /// <summary>Plattformkontext (eigener Scope): den Weg über seinen Hash finden, bevor der Tenant bekannt ist.</summary>
    private Task<Locator?> FindAsync(Func<IdentityDbContext, IQueryable<Locator>> query, CancellationToken cancellationToken) =>
        scopes.RunAsync(TenantContext.ForPlatform(), async (sp, ct) =>
        {
            var pdb = sp.GetRequiredService<IdentityDbContext>();
            var tx = await sp.GetRequiredService<IContextTransaction>().BeginAsync(ct);
            await using (tx)
            {
                var row = await query(pdb).AsNoTracking().FirstOrDefaultAsync(ct);
                await tx.CommitAsync(ct);
                return row;
            }
        }, cancellationToken);

    internal static Task Security(IServiceProvider sp, string eventType, bool success, PersonId? person, string? detail, CancellationToken ct) =>
        sp.GetRequiredService<ISecurityLog>().RecordAsync(new SecurityEvent(eventType, success, person?.ToString(), detail), ct);
}
