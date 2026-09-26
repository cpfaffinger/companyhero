using CompanyHero.Modules.Identity.Application.Providers;
using CompanyHero.Modules.Identity.Application.Sessions;
using CompanyHero.Modules.Identity.Domain;
using CompanyHero.Modules.Identity.Infrastructure;
using CompanyHero.Modules.Identity.Infrastructure.Oidc;
using CompanyHero.Modules.Organisation.Application;
using CompanyHero.Modules.Organisation.Domain;
using CompanyHero.Platform.Audit;
using CompanyHero.Platform.Data;
using CompanyHero.Platform.Messaging;
using CompanyHero.Platform.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CompanyHero.Modules.Identity.Application.Access;

public sealed record JoinCodeRecord(Guid Id, string Code, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, int? UsageLimit, int UsedCount, DateTimeOffset? RevokedAt, string JoinUrl);

public sealed record RoleCodeIssued(Guid Id, string Code, string Role, DateTimeOffset ExpiresAt);

public sealed record LoginPolicyRecord(bool MagicLink, bool Passkey, bool Kiosk, IReadOnlyList<string> DisabledProviderKeys, IReadOnlyList<string> ForcedProviderKeys, int KioskIdleSeconds, IReadOnlyList<ProviderRecord> AvailableProviders);

public sealed record LoginPolicyChange(bool MagicLink, bool Passkey, bool Kiosk, IReadOnlyList<string> DisabledProviderKeys, IReadOnlyList<string> ForcedProviderKeys, int KioskIdleSeconds);

/// <summary>Auswirkung einer Änderung: Personen, die danach keinen aktiven Weg mehr hätten, als Zahl ohne Namen (Zugang 3.4).</summary>
public sealed record LoginPolicyImpact(int PersonsWithoutWay);

public sealed record TenantProviderRecord(Guid Id, string Key, string DisplayName, string Issuer, string ClientId, DateTimeOffset? ValidatedAt, DateTimeOffset? DisabledAt);

/// <summary>Verwaltung des Zugangs durch den Tenant-Admin (Zugang 2, 3.2, 3.4): Beitritts- und Rollencodes, Anmeldewege, Anbieter.</summary>
public interface IAccessAdministration
{
    Task<IReadOnlyList<JoinCodeRecord>> ListJoinCodesAsync(CancellationToken cancellationToken);

    Task<JoinCodeRecord> CreateJoinCodeAsync(int? usageLimit, int? validDays, CancellationToken cancellationToken);

    Task<bool> RevokeJoinCodeAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Rollencode ausstellen (Aussteller je Rolle, Zugang 2.3); optional per E-Mail an eine eingegebene Adresse, die nicht gespeichert wird.</summary>
    Task<RoleCodeIssued> IssueRoleCodeAsync(string role, string? email, CancellationToken cancellationToken);

    /// <summary>Bestehende Person löst einen Rollencode ein; Funktionsrollen setzen den Klarnamen als Anzeigenamen.</summary>
    Task RedeemRoleCodeAsync(string code, string? realName, CancellationToken cancellationToken);

    Task<LoginPolicyRecord> GetPolicyAsync(CancellationToken cancellationToken);

    Task<LoginPolicyImpact> PreviewPolicyAsync(LoginPolicyChange change, CancellationToken cancellationToken);

    Task<LoginPolicyRecord> UpdatePolicyAsync(LoginPolicyChange change, CancellationToken cancellationToken);

    Task<IReadOnlyList<TenantProviderRecord>> ListProvidersAsync(CancellationToken cancellationToken);

    /// <summary>Anbieter konfigurieren; aktiv erst nach erfolgreicher Discovery-Validierung. Liefert die Gründe, wenn sie scheitert.</summary>
    Task<(TenantProviderRecord? Provider, IReadOnlyList<string> Problems)> ConfigureProviderAsync(string displayName, string issuer, string clientId, string clientSecret, CancellationToken cancellationToken);

    /// <summary>Widerruf eines Anbieters beendet die Sitzungen der Personen, die nur über ihn angemeldet sind (Zugang 5).</summary>
    Task<bool> DisableProviderAsync(Guid id, CancellationToken cancellationToken);
}

/// <summary>Beitrittslink des Tenants ohne Rollenprüfung für Aushang und Kiosk-Anzeige (Zugang 2.1): der Code steht ohnehin auf dem Zettel.</summary>
public interface IJoinLinks
{
    /// <summary>Der jüngste gültige, nicht widerrufene Beitrittscode des Tenants oder <c>null</c>.</summary>
    Task<JoinCodeRecord?> GetCurrentAsync(CancellationToken cancellationToken);
}

internal sealed class AccessAdministration(
    IdentityDbContext db,
    IContextTransaction transaction,
    ITenantContextAccessor context,
    IOrganisationDirectory organisations,
    IProviderCatalog providers,
    IDiscoveryDocumentReader discovery,
    ISessionService sessions,
    IAuditLog audit,
    IAccountMailSender mail,
    IOptions<IdentityOptions> options,
    TimeProvider clock) : IAccessAdministration, IJoinLinks
{
    public async Task<JoinCodeRecord?> GetCurrentAsync(CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        var now = clock.GetUtcNow();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var codes = await db.JoinCodes.AsNoTracking().Where(j => j.TenantId == tenantId).OrderByDescending(j => j.Id).ToListAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return codes.Select(ToRecord).FirstOrDefault(c => c.RevokedAt is null && c.ExpiresAt > now && (c.UsageLimit is null || c.UsedCount < c.UsageLimit));
    }

    public async Task<IReadOnlyList<JoinCodeRecord>> ListJoinCodesAsync(CancellationToken cancellationToken)
    {
        var tenantId = RequireRole(Role.TenantAdmin, Role.ProgrammeManager);
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var codes = await db.JoinCodes.AsNoTracking().Where(j => j.TenantId == tenantId).OrderBy(j => j.Id).ToListAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return codes.Select(ToRecord).ToList();
    }

    public async Task<JoinCodeRecord> CreateJoinCodeAsync(int? usageLimit, int? validDays, CancellationToken cancellationToken)
    {
        var tenantId = RequireRole(Role.TenantAdmin, Role.ProgrammeManager);
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var code = JoinCode.Create(tenantId, context.Require().PersonId, clock.GetUtcNow(), validDays is { } d ? TimeSpan.FromDays(Math.Clamp(d, 1, 730)) : null, usageLimit);
        db.JoinCodes.Add(code);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditEntry("access.join_code.created", code.Id.ToString("D"), null), cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return ToRecord(code);
    }

    public async Task<bool> RevokeJoinCodeAsync(Guid id, CancellationToken cancellationToken)
    {
        var tenantId = RequireRole(Role.TenantAdmin, Role.ProgrammeManager);
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var code = await db.JoinCodes.SingleOrDefaultAsync(j => j.TenantId == tenantId && j.Id == id, cancellationToken);
        if (code is null)
        {
            await tx.CommitAsync(cancellationToken);
            return false;
        }

        code.Revoke(clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditEntry("access.join_code.revoked", id.ToString("D"), null), cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<RoleCodeIssued> IssueRoleCodeAsync(string role, string? email, CancellationToken cancellationToken)
    {
        var current = context.Require();
        var tenantId = current.RequireTenant();
        // Aussteller je Rolle (Zugang 2.3): Tenant-Admin für alle Tenant-Rollen, Programm-Manager nur für Botschafter.
        var allowed = current.HasRole(Role.TenantAdmin) ? Role.TenantRoles.Where(r => r != Role.Member) : current.HasRole(Role.ProgrammeManager) ? [Role.HealthAmbassador] : [];
        if (!allowed.Contains(role, StringComparer.Ordinal))
        {
            throw new AccessDeniedException("role_not_issuable");
        }

        await using var tx = await transaction.BeginAsync(cancellationToken);
        var (entry, code) = RoleCode.Issue(tenantId, role, current.PersonId, clock.GetUtcNow());
        db.RoleCodes.Add(entry);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditEntry("access.role_code.issued", entry.Id.ToString("D"), role), cancellationToken);
        await tx.CommitAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(email))
        {
            // Die Adresse wird nur für den Versand verwendet und nicht gespeichert (Zugang 2.3).
            await mail.SendAsync(new AccountMail(email.Trim(), "konto.rollencode", AccessCodes.Grouped(code, 4), null), cancellationToken);
        }

        return new RoleCodeIssued(entry.Id, AccessCodes.Grouped(code, 4), role, entry.ExpiresAt);
    }

    public async Task RedeemRoleCodeAsync(string code, string? realName, CancellationToken cancellationToken)
    {
        var current = context.Require();
        if (current.IsKiosk)
        {
            throw new AccessDeniedException("kiosk_not_allowed");
        }

        var tenantId = current.RequireTenant();
        var personId = current.RequirePerson();
        var normalized = AccessCodes.Normalize(code ?? string.Empty);
        var hash = AccessCodes.Hash(normalized);
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var now = clock.GetUtcNow();
        var roleCode = await db.RoleCodes.SingleOrDefaultAsync(r => r.TenantId == tenantId && r.CodeHash == hash, cancellationToken);
        if (roleCode is null || !roleCode.IsRedeemableAt(now))
        {
            throw new AccessDeniedException("role_code_invalid", 404);
        }

        var ways = await WayQueries.WaysOfAsync(db, tenantId, personId, cancellationToken);
        if (AccessRules.RequiresRealName(roleCode.Role))
        {
            if (string.IsNullOrWhiteSpace(realName))
            {
                throw new AccessDeniedException("real_name_required", 422);
            }

            if (!ways.Email && !ways.External)
            {
                throw new AccessDeniedException("email_or_provider_required", 422);
            }

            if (AccessRules.RequiresPhishingResistantWay(roleCode.Role) && !ways.Passkey && !ways.External)
            {
                throw new AccessDeniedException("passkey_or_provider_required", 422);
            }

            var person = await db.Persons.SingleAsync(p => p.TenantId == tenantId && p.Id == personId, cancellationToken);
            if (await db.Persons.AnyAsync(p => p.TenantId == tenantId && p.Id != personId && p.DisplayName == realName.Trim(), cancellationToken))
            {
                throw new AccessDeniedException("display_name_taken", 409);
            }

            person.AssumeRealName(realName);
        }

        roleCode.Redeem(now);
        await db.SaveChangesAsync(cancellationToken);
        await organisations.AssignRoleAsync(personId, roleCode.Role, cancellationToken);
        if (AccessRules.IsPrivileged([roleCode.Role]))
        {
            // Die Sitzungsart folgt den Rollen (Zugang 5): bestehende lange Mitgliedssitzungen enden, die Person meldet sich neu an.
            await sessions.RevokeAllForPersonAsync(personId, cancellationToken);
        }

        await audit.RecordAsync(new AuditEntry("access.role_code.redeemed", roleCode.Id.ToString("D"), roleCode.Role), cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    public async Task<LoginPolicyRecord> GetPolicyAsync(CancellationToken cancellationToken)
    {
        var tenantId = RequireRole(Role.TenantAdmin, Role.Insight);
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var policy = await WayQueries.PolicyAsync(db, tenantId, clock.GetUtcNow(), cancellationToken);
        var available = await AvailableProvidersAsync(tenantId, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return ToRecord(policy, available);
    }

    public async Task<LoginPolicyImpact> PreviewPolicyAsync(LoginPolicyChange change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);
        var tenantId = RequireRole(Role.TenantAdmin);
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var impact = await ImpactAsync(tenantId, change, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return impact;
    }

    public async Task<LoginPolicyRecord> UpdatePolicyAsync(LoginPolicyChange change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);
        var tenantId = RequireRole(Role.TenantAdmin);
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var now = clock.GetUtcNow();
        var policy = await db.LoginPolicies.SingleOrDefaultAsync(p => p.TenantId == tenantId, cancellationToken);
        if (policy is null)
        {
            policy = LoginPolicy.Default(tenantId, now);
            db.LoginPolicies.Add(policy);
        }

        var available = await AvailableProvidersAsync(tenantId, cancellationToken);
        var known = available.Select(p => p.Key).ToHashSet(StringComparer.Ordinal);
        if (change.DisabledProviderKeys.Concat(change.ForcedProviderKeys).Any(k => !known.Contains(k)))
        {
            throw new AccessDeniedException("provider_unknown", 422);
        }

        try
        {
            policy.Update(change.MagicLink, change.Passkey, change.Kiosk, change.DisabledProviderKeys, change.ForcedProviderKeys, change.KioskIdleSeconds, available.Count, now);
        }
        catch (LoginPolicyException ex)
        {
            throw new AccessDeniedException(ex.Message, 422);
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditEntry("access.login_ways.changed", null, $"magic_link={change.MagicLink};passkey={change.Passkey};kiosk={change.Kiosk};forced={string.Join('|', change.ForcedProviderKeys)};idle={change.KioskIdleSeconds}"), cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return ToRecord(policy, available);
    }

    public async Task<IReadOnlyList<TenantProviderRecord>> ListProvidersAsync(CancellationToken cancellationToken)
    {
        var tenantId = RequireRole(Role.TenantAdmin, Role.Insight);
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var list = await db.ExternalProviders.AsNoTracking().Where(p => p.TenantId == tenantId).OrderBy(p => p.Id).ToListAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return list.Select(ToRecord).ToList();
    }

    public async Task<(TenantProviderRecord? Provider, IReadOnlyList<string> Problems)> ConfigureProviderAsync(string displayName, string issuer, string clientId, string clientSecret, CancellationToken cancellationToken)
    {
        var tenantId = RequireRole(Role.TenantAdmin);
        ExternalProvider provider;
        try
        {
            provider = ExternalProvider.Configure(tenantId, displayName, issuer, clientId, providers.ProtectSecret(clientSecret ?? string.Empty), clock.GetUtcNow());
        }
        catch (ArgumentException ex)
        {
            return (null, [ex.ParamName ?? "invalid"]);
        }

        // Discovery außerhalb der Transaktion (Backend 5.2: keine externen Aufrufe in offenen Transaktionen).
        var document = await discovery.ReadAsync(provider.Issuer, cancellationToken);
        var problems = document is null ? ["discovery_unreachable"] : DiscoveryValidation.Problems(provider.Issuer, document);
        if (problems.Count > 0)
        {
            return (null, problems);
        }

        await using var tx = await transaction.BeginAsync(cancellationToken);
        provider.MarkValidated(clock.GetUtcNow());
        db.ExternalProviders.Add(provider);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditEntry("access.provider.configured", provider.Id.ToString("D"), provider.Issuer), cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return (ToRecord(provider), []);
    }

    public async Task<bool> DisableProviderAsync(Guid id, CancellationToken cancellationToken)
    {
        var tenantId = RequireRole(Role.TenantAdmin);
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var provider = await db.ExternalProviders.SingleOrDefaultAsync(p => p.TenantId == tenantId && p.Id == id, cancellationToken);
        if (provider is null)
        {
            await tx.CommitAsync(cancellationToken);
            return false;
        }

        var now = clock.GetUtcNow();
        provider.Disable(now);
        // Widerruf eines Anbieters beendet betroffene Sitzungen sofort (Zugang 5): Personen mit Verknüpfung zu diesem Anbieter.
        var affected = await db.ExternalLogins.Where(e => e.TenantId == tenantId && e.ProviderKey == provider.Key).Select(e => e.PersonId).Distinct().ToListAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        foreach (var person in affected)
        {
            await sessions.RevokeAllForPersonAsync(person, cancellationToken);
        }

        await audit.RecordAsync(new AuditEntry("access.provider.disabled", id.ToString("D"), null), cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return true;
    }

    private async Task<LoginPolicyImpact> ImpactAsync(TenantId tenantId, LoginPolicyChange change, CancellationToken cancellationToken)
    {
        // Personen, die nach der Änderung keinen aktiven Weg mehr hätten: nur eine Zahl, keine Namen (Zugang 3.4).
        var persons = await db.Persons.AsNoTracking().Where(p => p.TenantId == tenantId && p.State == PersonState.Active).Select(p => p.Id).ToListAsync(cancellationToken);
        var passkeys = await db.Passkeys.AsNoTracking().Where(p => p.TenantId == tenantId).Select(p => p.PersonId).Distinct().ToListAsync(cancellationToken);
        var emails = await db.EmailLogins.AsNoTracking().Where(e => e.TenantId == tenantId).Select(e => e.PersonId).ToListAsync(cancellationToken);
        var externals = await db.ExternalLogins.AsNoTracking().Where(e => e.TenantId == tenantId).Select(e => new { e.PersonId, e.ProviderKey }).ToListAsync(cancellationToken);
        var disabled = change.DisabledProviderKeys.ToHashSet(StringComparer.Ordinal);
        var forced = change.ForcedProviderKeys.ToHashSet(StringComparer.Ordinal);
        var without = persons.Count(p =>
            !(change.Passkey && passkeys.Contains(p))
            && !(change.MagicLink && emails.Contains(p))
            && !externals.Any(e => e.PersonId == p && !disabled.Contains(e.ProviderKey) && (forced.Count == 0 || forced.Contains(e.ProviderKey))));
        return new LoginPolicyImpact(without);
    }

    private async Task<IReadOnlyList<ProviderRecord>> AvailableProvidersAsync(TenantId tenantId, CancellationToken cancellationToken)
    {
        var own = await db.ExternalProviders.AsNoTracking().Where(p => p.TenantId == tenantId && p.ValidatedAt != null && p.DisabledAt == null).OrderBy(p => p.Id).ToListAsync(cancellationToken);
        return providers.Platform().Concat(own.Select(p => new ProviderRecord(p.Key, p.DisplayName, p.Issuer, p.ClientId, string.Empty, null, p.TenantId, p.UpdatedAt))).ToList();
    }

    private TenantId RequireRole(params string[] roles)
    {
        var current = context.Require();
        if (current.IsKiosk || !roles.Any(current.HasRole))
        {
            throw new AccessDeniedException("role_required");
        }

        return current.RequireTenant();
    }

    private JoinCodeRecord ToRecord(JoinCode j) =>
        new(j.Id, j.Code, j.CreatedAt, j.ExpiresAt, j.UsageLimit, j.UsedCount, j.RevokedAt, options.Value.PublicOriginUri.GetLeftPart(UriPartial.Authority) + "/join/" + j.Code);

    private static LoginPolicyRecord ToRecord(LoginPolicy p, IReadOnlyList<ProviderRecord> available) =>
        new(p.MagicLinkEnabled, p.PasskeyEnabled, p.KioskEnabled, p.DisabledProviderKeys, p.ForcedProviderKeys, p.KioskIdleSeconds, available);

    private static TenantProviderRecord ToRecord(ExternalProvider p) => new(p.Id, p.Key, p.DisplayName, p.Issuer, p.ClientId, p.ValidatedAt, p.DisabledAt);
}
