using System.Text.Json;
using CompanyHero.Modules.Identity.Application.Join;
using CompanyHero.Modules.Identity.Application.Passkeys;
using CompanyHero.Modules.Identity.Application.Providers;
using CompanyHero.Modules.Identity.Application.Sessions;
using CompanyHero.Modules.Identity.Domain;
using CompanyHero.Modules.Identity.Infrastructure;
using CompanyHero.Modules.Organisation.Application;
using CompanyHero.Platform.Audit;
using CompanyHero.Platform.Data;
using CompanyHero.Platform.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace CompanyHero.Modules.Identity.Application.Account;

public sealed record PasskeyRecord(Guid Id, string DeviceName, DateTimeOffset CreatedAt, DateTimeOffset? LastUsedAt);

public sealed record LinkedProviderRecord(Guid Id, string ProviderKey, string DisplayName, DateTimeOffset CreatedAt);

/// <summary>Anmeldewege der Person für die Profilansicht (Zugang 4): ohne Geheimnisse, ohne Hashes.</summary>
public sealed record AccessOverview(IReadOnlyList<PasskeyRecord> Passkeys, string? Email, IReadOnlyList<LinkedProviderRecord> Providers, bool RecoveryCodeActive, string KioskId, bool KioskPinSet, IReadOnlyList<ProviderRecord> AvailableProviders);

/// <summary>Konto der angemeldeten Person (Zugang 4, 8): Wege verknüpfen und trennen, Wiederherstellungscode, Abmeldung aus allen Sitzungen, Austritt.</summary>
public interface IAccountService
{
    Task<AccessOverview> GetOverviewAsync(CancellationToken cancellationToken);

    PasskeyCeremony BeginPasskey(string displayName, IReadOnlyList<byte[]> excludeCredentialIds);

    Task<IReadOnlyList<byte[]>> CredentialIdsAsync(CancellationToken cancellationToken);

    Task<PasskeyRecord> AddPasskeyAsync(PasskeyAnswer answer, CancellationToken cancellationToken);

    /// <summary>Trennen nur, solange mindestens ein Weg verbleibt (Zugang 4).</summary>
    Task RemovePasskeyAsync(Guid passkeyId, CancellationToken cancellationToken);

    Task SetEmailAsync(string email, CancellationToken cancellationToken);

    Task RemoveEmailAsync(CancellationToken cancellationToken);

    Task UnlinkProviderAsync(Guid externalLoginId, CancellationToken cancellationToken);

    /// <summary>Neuer Wiederherstellungscode; der alte wird ungültig; Klartext genau einmal.</summary>
    Task<string> RenewRecoveryCodeAsync(CancellationToken cancellationToken);

    Task<int> LogoutEverywhereAsync(CancellationToken cancellationToken);

    /// <summary>Austritt (Zugang 8): Sitzungen, Identitäten, Codes, Passkeys, Kennung, PIN, Indexeinträge; Mitgliedschaft endet.</summary>
    Task LeaveAsync(CancellationToken cancellationToken);
}

internal sealed class AccountService(
    IdentityDbContext db,
    IContextTransaction transaction,
    ITenantContextAccessor context,
    IPasskeyService passkeys,
    IProviderCatalog providers,
    ISessionService sessions,
    IOrganisationDirectory organisations,
    IAuditLog audit,
    ISecurityLog security,
    TimeProvider clock) : IAccountService
{
    public async Task<AccessOverview> GetOverviewAsync(CancellationToken cancellationToken)
    {
        var (tenantId, personId) = RequirePerson();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var keys = await db.Passkeys.AsNoTracking().Where(p => p.TenantId == tenantId && p.PersonId == personId).OrderBy(p => p.CreatedAt).ToListAsync(cancellationToken);
        var email = await db.EmailLogins.AsNoTracking().Where(e => e.TenantId == tenantId && e.PersonId == personId).Select(e => e.Email).SingleOrDefaultAsync(cancellationToken);
        var externals = await db.ExternalLogins.AsNoTracking().Where(e => e.TenantId == tenantId && e.PersonId == personId).OrderBy(e => e.CreatedAt).ToListAsync(cancellationToken);
        var recovery = await db.RecoveryCodes.AnyAsync(r => r.TenantId == tenantId && r.PersonId == personId, cancellationToken);
        var kiosk = await EnsureKioskCredentialAsync(tenantId, personId, cancellationToken);
        var available = await providers.ListForTenantAsync(tenantId, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        var names = available.ToDictionary(p => p.Key, p => p.DisplayName, StringComparer.Ordinal);
        return new AccessOverview(
            keys.Select(k => new PasskeyRecord(k.Id, k.DeviceName, k.CreatedAt, k.LastUsedAt)).ToList(),
            email,
            externals.Select(e => new LinkedProviderRecord(e.Id, e.ProviderKey, names.GetValueOrDefault(e.ProviderKey, e.ProviderKey), e.CreatedAt)).ToList(),
            recovery,
            kiosk.KioskId,
            kiosk.PinHash is not null,
            available);
    }

    public PasskeyCeremony BeginPasskey(string displayName, IReadOnlyList<byte[]> excludeCredentialIds) =>
        passkeys.BeginRegistration(RequirePerson().PersonId.Value.ToByteArray(), displayName, excludeCredentialIds);

    public async Task<IReadOnlyList<byte[]>> CredentialIdsAsync(CancellationToken cancellationToken)
    {
        var (tenantId, personId) = RequirePerson();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var ids = await db.Passkeys.AsNoTracking().Where(p => p.TenantId == tenantId && p.PersonId == personId).Select(p => p.CredentialId).ToListAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return ids;
    }

    public async Task<PasskeyRecord> AddPasskeyAsync(PasskeyAnswer answer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(answer);
        var (tenantId, personId) = RequirePerson();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var now = clock.GetUtcNow();
        var (verified, handle) = await passkeys.CompleteRegistrationAsync(answer.State, answer.Credential, cancellationToken);
        if (!handle.AsSpan().SequenceEqual(personId.Value.ToByteArray()))
        {
            throw new PasskeyRejectedException("Die Zeremonie gehört zu einer anderen Person.");
        }

        var passkey = Passkey.Register(tenantId, personId, verified.CredentialId, verified.PublicKey, verified.SignCount, verified.AaGuid, answer.DeviceName ?? "Passkey", now);
        db.Passkeys.Add(passkey);
        await db.SaveChangesAsync(cancellationToken);
        await security.RecordAsync(new SecurityEvent("account.passkey.added", true, personId.ToString(), null), cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return new PasskeyRecord(passkey.Id, passkey.DeviceName, passkey.CreatedAt, null);
    }

    public async Task RemovePasskeyAsync(Guid passkeyId, CancellationToken cancellationToken)
    {
        var (tenantId, personId) = RequirePerson();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var passkey = await db.Passkeys.SingleOrDefaultAsync(p => p.TenantId == tenantId && p.PersonId == personId && p.Id == passkeyId, cancellationToken)
            ?? throw new AccessDeniedException("passkey_unknown", 404);
        await EnsureAnotherWayRemainsAsync(tenantId, personId, removingPasskey: passkey, cancellationToken: cancellationToken);
        db.Passkeys.Remove(passkey);
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    public async Task SetEmailAsync(string email, CancellationToken cancellationToken)
    {
        var (tenantId, personId) = RequirePerson();
        string hash;
        try
        {
            hash = IdentityIndexEntry.HashEmail(email);
        }
        catch (ArgumentException)
        {
            throw new AccessDeniedException("email_invalid", 422);
        }

        await using var tx = await transaction.BeginAsync(cancellationToken);
        var now = clock.GetUtcNow();
        var policy = await WayQueries.PolicyAsync(db, tenantId, now, cancellationToken);
        if (!policy.MagicLinkEnabled)
        {
            throw new AccessDeniedException("way_disabled");
        }

        if (await db.IdentityIndex.AnyAsync(i => i.Hash == hash, cancellationToken))
        {
            // Keine automatische Verknüpfung über gleiche E-Mail; eine Adresse gehört höchstens einer aktiven Person (A-016).
            throw new AccessDeniedException("identity_taken", 409);
        }

        var existing = await db.EmailLogins.SingleOrDefaultAsync(e => e.TenantId == tenantId && e.PersonId == personId, cancellationToken);
        if (existing is not null)
        {
            db.EmailLogins.Remove(existing);
            db.IdentityIndex.RemoveRange(db.IdentityIndex.Where(i => i.TenantId == tenantId && i.PersonId == personId && i.Kind == IdentityKind.Email));
        }

        db.EmailLogins.Add(EmailLogin.Add(tenantId, personId, email, now));
        db.IdentityIndex.Add(IdentityIndexEntry.Create(hash, IdentityKind.Email, tenantId, personId, now));
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    public async Task RemoveEmailAsync(CancellationToken cancellationToken)
    {
        var (tenantId, personId) = RequirePerson();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var existing = await db.EmailLogins.SingleOrDefaultAsync(e => e.TenantId == tenantId && e.PersonId == personId, cancellationToken)
            ?? throw new AccessDeniedException("email_unknown", 404);
        await EnsureAnotherWayRemainsAsync(tenantId, personId, removingEmail: true, cancellationToken: cancellationToken);
        db.EmailLogins.Remove(existing);
        db.IdentityIndex.RemoveRange(db.IdentityIndex.Where(i => i.TenantId == tenantId && i.PersonId == personId && i.Kind == IdentityKind.Email));
        await EnsureRecoveryCodeAsync(tenantId, personId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    public async Task UnlinkProviderAsync(Guid externalLoginId, CancellationToken cancellationToken)
    {
        var (tenantId, personId) = RequirePerson();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var login = await db.ExternalLogins.SingleOrDefaultAsync(e => e.TenantId == tenantId && e.PersonId == personId && e.Id == externalLoginId, cancellationToken)
            ?? throw new AccessDeniedException("provider_unknown", 404);
        await EnsureAnotherWayRemainsAsync(tenantId, personId, removingExternal: login, cancellationToken: cancellationToken);
        db.ExternalLogins.Remove(login);
        db.IdentityIndex.RemoveRange(db.IdentityIndex.Where(i => i.Hash == login.SubjectHash));
        await EnsureRecoveryCodeAsync(tenantId, personId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    public async Task<string> RenewRecoveryCodeAsync(CancellationToken cancellationToken)
    {
        var (tenantId, personId) = RequirePerson();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var now = clock.GetUtcNow();
        var entry = await db.RecoveryCodes.SingleOrDefaultAsync(r => r.TenantId == tenantId && r.PersonId == personId, cancellationToken);
        string code;
        if (entry is null)
        {
            (entry, code) = RecoveryCode.Issue(tenantId, personId, now);
            db.RecoveryCodes.Add(entry);
        }
        else
        {
            code = entry.Renew(now);
        }

        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return AccessCodes.Grouped(code);
    }

    public async Task<int> LogoutEverywhereAsync(CancellationToken cancellationToken)
    {
        var (_, personId) = RequirePerson();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var count = await sessions.RevokeAllForPersonAsync(personId, cancellationToken);
        await security.RecordAsync(new SecurityEvent("account.logout_all", true, personId.ToString(), null), cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return count;
    }

    public async Task LeaveAsync(CancellationToken cancellationToken)
    {
        var (tenantId, personId) = RequirePerson();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var now = clock.GetUtcNow();
        var person = await db.Persons.SingleAsync(p => p.TenantId == tenantId && p.Id == personId, cancellationToken);
        person.Leave(now);
        db.Passkeys.RemoveRange(db.Passkeys.Where(p => p.TenantId == tenantId && p.PersonId == personId));
        db.EmailLogins.RemoveRange(db.EmailLogins.Where(e => e.TenantId == tenantId && e.PersonId == personId));
        db.ExternalLogins.RemoveRange(db.ExternalLogins.Where(e => e.TenantId == tenantId && e.PersonId == personId));
        db.RecoveryCodes.RemoveRange(db.RecoveryCodes.Where(r => r.TenantId == tenantId && r.PersonId == personId));
        db.KioskCredentials.RemoveRange(db.KioskCredentials.Where(k => k.TenantId == tenantId && k.PersonId == personId));
        db.IdentityIndex.RemoveRange(db.IdentityIndex.Where(i => i.TenantId == tenantId && i.PersonId == personId));
        db.MagicLinks.RemoveRange(db.MagicLinks.Where(m => m.TenantId == tenantId && m.PersonId == personId));
        db.TransferLinks.RemoveRange(db.TransferLinks.Where(t => t.TenantId == tenantId && t.PersonId == personId));
        await db.SaveChangesAsync(cancellationToken);
        await sessions.RevokeAllForPersonAsync(personId, cancellationToken);
        await organisations.LeaveAsync(personId, cancellationToken);
        await audit.RecordAsync(new AuditEntry("access.person.left", null, null), cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    /// <summary>Die Kiosk-Kennung entsteht mit dem Beitritt (Zugang 2.2); Personen aus älteren Beständen erhalten sie beim ersten Aufruf.</summary>
    private async Task<KioskCredential> EnsureKioskCredentialAsync(TenantId tenantId, PersonId personId, CancellationToken cancellationToken)
    {
        var existing = await db.KioskCredentials.SingleOrDefaultAsync(k => k.TenantId == tenantId && k.PersonId == personId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var created = KioskCredential.Create(tenantId, personId, await WayQueries.NewKioskIdAsync(db, tenantId, cancellationToken), null, clock.GetUtcNow());
        db.KioskCredentials.Add(created);
        await db.SaveChangesAsync(cancellationToken);
        return created;
    }

    private async Task EnsureAnotherWayRemainsAsync(TenantId tenantId, PersonId personId, Passkey? removingPasskey = null, bool removingEmail = false, Domain.ExternalLogin? removingExternal = null, CancellationToken cancellationToken = default)
    {
        var passkeyCount = await db.Passkeys.CountAsync(p => p.TenantId == tenantId && p.PersonId == personId, cancellationToken) - (removingPasskey is null ? 0 : 1);
        var emailCount = await db.EmailLogins.CountAsync(e => e.TenantId == tenantId && e.PersonId == personId, cancellationToken) - (removingEmail ? 1 : 0);
        var externalCount = await db.ExternalLogins.CountAsync(e => e.TenantId == tenantId && e.PersonId == personId, cancellationToken) - (removingExternal is null ? 0 : 1);
        if (passkeyCount + emailCount + externalCount <= 0)
        {
            throw new AccessDeniedException("last_way", 409);
        }
    }

    /// <summary>Für Personen ohne E-Mail und ohne Anbieter bleibt der Wiederherstellungscode immer aktiv (Zugang 4).</summary>
    private async Task EnsureRecoveryCodeAsync(TenantId tenantId, PersonId personId, CancellationToken cancellationToken)
    {
        if (!await db.RecoveryCodes.AnyAsync(r => r.TenantId == tenantId && r.PersonId == personId, cancellationToken))
        {
            var (entry, _) = RecoveryCode.Issue(tenantId, personId, clock.GetUtcNow());
            db.RecoveryCodes.Add(entry);
        }
    }

    private (TenantId TenantId, PersonId PersonId) RequirePerson()
    {
        var current = context.Require();
        if (current.IsKiosk)
        {
            throw new AccessDeniedException("kiosk_not_allowed");
        }

        return (current.RequireTenant(), current.RequirePerson());
    }
}
