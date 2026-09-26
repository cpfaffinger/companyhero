using System.Text.Json;
using CompanyHero.Modules.Identity.Application.Passkeys;
using CompanyHero.Modules.Identity.Application.Providers;
using CompanyHero.Modules.Identity.Application.Sessions;
using CompanyHero.Modules.Identity.Domain;
using CompanyHero.Modules.Identity.Infrastructure;
using CompanyHero.Modules.Organisation.Application;
using CompanyHero.Modules.Organisation.Domain;
using CompanyHero.Platform.Audit;
using CompanyHero.Platform.Data;
using CompanyHero.Platform.Metering;
using CompanyHero.Platform.Privacy;
using CompanyHero.Platform.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;

namespace CompanyHero.Modules.Identity.Application.Join;

/// <summary>Anmeldewege, die der Tenant Beitretenden anbietet (Zugang 3.4), plus Anbieter mit Anzeigenamen.</summary>
public sealed record JoinWays(bool Passkey, bool MagicLink, bool Kiosk, IReadOnlyList<ProviderRecord> Providers, IReadOnlyList<string> ForcedProviderKeys);

/// <summary>Tenant-Vorschau beim Beitritt: nur Name (Logo über Marke) und Wege (Zugang 2.1).</summary>
public sealed record JoinPreview(TenantId TenantId, string TenantName, JoinWays Ways);

/// <summary>Passkey-Antwort des Browsers mit dem Zustand der Zeremonie.</summary>
public sealed record PasskeyAnswer(string State, JsonElement Credential, string? DeviceName);

/// <summary>
/// Beitritt (Zugang 2.2): Code, Anzeigename, Sichtbarkeit, Zugang sichern. Eigenes Gerät: Passkey, E-Mail oder externer
/// Anbieter (aus der Übergabe); Kiosk: PIN. Ohne E-Mail und Anbieter entsteht ein Wiederherstellungscode.
/// </summary>
public sealed record JoinCommand(
    string Code,
    string DisplayName,
    VisibilityChoice Visibility,
    PasskeyAnswer? Passkey,
    string? Email,
    ExternalIdentity? External,
    string? KioskPin,
    string? RealName = null,
    string? Role = null);

/// <summary>Ergebnis: Person, Kiosk-Kennung, einmalig angezeigter Wiederherstellungscode, Sitzung (nicht am Kiosk).</summary>
public sealed record JoinResult(TenantId TenantId, PersonId PersonId, string KioskId, string? RecoveryCode, IssuedSession? Session);

public interface IJoinService
{
    /// <summary>Plattformkontext: Vorschau zu einem Beitrittscode; ungültige Codes geben keine Auskunft (Zugang 2.1).</summary>
    Task<JoinPreview?> PreviewAsync(string code, CancellationToken cancellationToken);

    /// <summary>Plattformkontext: Vorschau zu einem Rollencode (neue Person mit Funktionsrolle, Zugang 2.3).</summary>
    Task<(JoinPreview Preview, string Role)?> PreviewRoleCodeAsync(string code, CancellationToken cancellationToken);

    /// <summary>Zeremonie für den Passkey einer Person, die erst mit dem Beitritt entsteht; die Benutzerkennung wird der Person.</summary>
    PasskeyCeremony BeginPasskeyForNewPerson(string displayName);

    /// <summary>Beitritt über Beitrittscode auf dem eigenen Gerät; alles in einer Transaktion (A-014).</summary>
    Task<JoinResult> JoinAsync(JoinCommand command, CancellationToken cancellationToken);

    /// <summary>Beitritt am Kiosk (Gerätesitzung): Kennung wird angezeigt, PIN gewählt; kein Beitritt bei erzwungenem Anbieter (Zugang 3.4).</summary>
    Task<JoinResult> JoinAtKioskAsync(JoinCommand command, CancellationToken cancellationToken);

    /// <summary>Neue Person mit Funktionsrolle über Rollencode (erster Tenant-Admin, Zugang 2.3): Klarname, E-Mail oder Anbieter, Passkey oder Anbieter.</summary>
    Task<JoinResult> JoinWithRoleCodeAsync(JoinCommand command, CancellationToken cancellationToken);
}

internal sealed class JoinService(ITenantScopeFactory scopes, IPasskeyService passkeys, TimeProvider clock) : IJoinService
{
    public async Task<JoinPreview?> PreviewAsync(string code, CancellationToken cancellationToken)
    {
        var normalized = AccessCodes.Normalize(code ?? string.Empty);
        if (!AccessCodes.IsWellFormed(normalized, AccessCodes.JoinCodeLength))
        {
            return null;
        }

        return await scopes.RunAsync(TenantContext.ForPlatform(), async (sp, ct) =>
        {
            var db = sp.GetRequiredService<IdentityDbContext>();
            var tx = await sp.GetRequiredService<IContextTransaction>().BeginAsync(ct);
            await using (tx)
            {
                var now = clock.GetUtcNow();
                var join = await db.JoinCodes.AsNoTracking().SingleOrDefaultAsync(j => j.Code == normalized, ct);
                var preview = join is null || !join.IsRedeemableAt(now) ? null : await PreviewTenantAsync(sp, db, join.TenantId, now, ct);
                await tx.CommitAsync(ct);
                return preview;
            }
        }, cancellationToken);
    }

    public async Task<(JoinPreview Preview, string Role)?> PreviewRoleCodeAsync(string code, CancellationToken cancellationToken)
    {
        var normalized = AccessCodes.Normalize(code ?? string.Empty);
        if (!AccessCodes.IsWellFormed(normalized, AccessCodes.RoleCodeLength))
        {
            return null;
        }

        var hash = AccessCodes.Hash(normalized);
        return await scopes.RunAsync(TenantContext.ForPlatform(), async (sp, ct) =>
        {
            var db = sp.GetRequiredService<IdentityDbContext>();
            var tx = await sp.GetRequiredService<IContextTransaction>().BeginAsync(ct);
            await using (tx)
            {
                var now = clock.GetUtcNow();
                var role = await db.RoleCodes.AsNoTracking().SingleOrDefaultAsync(r => r.CodeHash == hash, ct);
                (JoinPreview, string)? result = null;
                if (role is not null && role.IsRedeemableAt(now) && await PreviewTenantAsync(sp, db, role.TenantId, now, ct, allowProvisioned: true) is { } preview)
                {
                    result = (preview, role.Role);
                }

                await tx.CommitAsync(ct);
                return result;
            }
        }, cancellationToken);
    }

    public PasskeyCeremony BeginPasskeyForNewPerson(string displayName) =>
        passkeys.BeginRegistration(PersonId.New().Value.ToByteArray(), string.IsNullOrWhiteSpace(displayName) ? "CompanyHero" : displayName.Trim(), []);

    public Task<JoinResult> JoinAsync(JoinCommand command, CancellationToken cancellationToken) => JoinCoreAsync(command, atKiosk: false, cancellationToken);

    public Task<JoinResult> JoinAtKioskAsync(JoinCommand command, CancellationToken cancellationToken) => JoinCoreAsync(command, atKiosk: true, cancellationToken);

    public async Task<JoinResult> JoinWithRoleCodeAsync(JoinCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var normalized = AccessCodes.Normalize(command.Code);
        var hash = AccessCodes.Hash(normalized);
        var role = await scopes.RunAsync(TenantContext.ForPlatform(), async (sp, ct) =>
        {
            var db = sp.GetRequiredService<IdentityDbContext>();
            var tx = await sp.GetRequiredService<IContextTransaction>().BeginAsync(ct);
            await using (tx)
            {
                var r = await db.RoleCodes.AsNoTracking().SingleOrDefaultAsync(x => x.CodeHash == hash, ct);
                await tx.CommitAsync(ct);
                return r;
            }
        }, cancellationToken);
        if (role is null || !role.IsRedeemableAt(clock.GetUtcNow()))
        {
            throw new AccessDeniedException("role_code_invalid", 404);
        }

        if (string.IsNullOrWhiteSpace(command.RealName) && AccessRules.RequiresRealName(role.Role))
        {
            throw new AccessDeniedException("real_name_required", 422);
        }

        return await scopes.RunAsync(TenantContext.ForTenant(role.TenantId), (sp, ct) =>
            CreatePersonAsync(sp, role.TenantId, command with { Role = role.Role }, atKiosk: false, roleCodeId: role.Id, ct), cancellationToken);
    }

    private async Task<JoinResult> JoinCoreAsync(JoinCommand command, bool atKiosk, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var normalized = AccessCodes.Normalize(command.Code);
        var join = await scopes.RunAsync(TenantContext.ForPlatform(), async (sp, ct) =>
        {
            var db = sp.GetRequiredService<IdentityDbContext>();
            var tx = await sp.GetRequiredService<IContextTransaction>().BeginAsync(ct);
            await using (tx)
            {
                var j = await db.JoinCodes.AsNoTracking().SingleOrDefaultAsync(x => x.Code == normalized, ct);
                await tx.CommitAsync(ct);
                return j;
            }
        }, cancellationToken);
        if (join is null || !join.IsRedeemableAt(clock.GetUtcNow()))
        {
            throw new AccessDeniedException("join_code_invalid", 404);
        }

        return await scopes.RunAsync(TenantContext.ForTenant(join.TenantId), (sp, ct) => CreatePersonAsync(sp, join.TenantId, command, atKiosk, null, ct), cancellationToken);
    }

    /// <summary>Tenant-Kontext, eine Transaktion: Person, Mitgliedschaft, Rolle, Sichtbarkeit, Kiosk-Kennung, Wege, Code, Metering, Protokoll.</summary>
    private async Task<JoinResult> CreatePersonAsync(IServiceProvider sp, TenantId tenantId, JoinCommand command, bool atKiosk, Guid? roleCodeId, CancellationToken ct)
    {
        var db = sp.GetRequiredService<IdentityDbContext>();
        var tx = await sp.GetRequiredService<IContextTransaction>().BeginAsync(ct);
        await using (tx)
        {
            var now = clock.GetUtcNow();
            var policy = await WayQueries.PolicyAsync(db, tenantId, now, ct);
            ValidateWays(command, policy, atKiosk);

            var normalizedCode = AccessCodes.Normalize(command.Code);
            JoinCode? joinCode = null;
            RoleCode? roleCode = null;
            if (roleCodeId is null)
            {
                joinCode = await db.JoinCodes.SingleAsync(j => j.TenantId == tenantId && j.Code == normalizedCode, ct);
                if (!joinCode.IsRedeemableAt(now))
                {
                    throw new AccessDeniedException("join_code_invalid", 404);
                }

                joinCode.Use();
            }
            else
            {
                roleCode = await db.RoleCodes.SingleAsync(r => r.TenantId == tenantId && r.Id == roleCodeId, ct);
                if (!roleCode.IsRedeemableAt(now))
                {
                    throw new AccessDeniedException("role_code_invalid", 404);
                }

                roleCode.Redeem(now);
            }

            var displayName = command.DisplayName.Trim();
            if (await db.Persons.AnyAsync(p => p.TenantId == tenantId && p.DisplayName == displayName, ct))
            {
                throw new AccessDeniedException("display_name_taken", 409);
            }

            // Der Passkey wird zuerst geprüft: sein User-Handle wird die Personenkennung, damit spätere Anmeldungen
            // den Inhaber der Credential eindeutig prüfen können.
            VerifiedPasskey? verifiedPasskey = null;
            var personId = PersonId.New();
            if (command.Passkey is { } answer)
            {
                var (registered, handle) = await passkeys.CompleteRegistrationAsync(answer.State, answer.Credential, ct);
                verifiedPasskey = registered;
                personId = new PersonId(new Guid(handle));
            }

            var person = Person.Create(tenantId, personId, displayName, now);
            if (command.Role is { } role && AccessRules.RequiresRealName(role))
            {
                person.AssumeRealName(command.RealName!);
            }

            db.Persons.Add(person);

            var organisations = sp.GetRequiredService<IOrganisationDirectory>();
            await organisations.AddMemberAsync(person.Id, ct);
            await organisations.AssignRoleAsync(person.Id, Role.Member, ct);
            if (command.Role is { } assigned && assigned != Role.Member)
            {
                await organisations.AssignRoleAsync(person.Id, assigned, ct);
            }

            await sp.GetRequiredService<IVisibilityChoiceRecorder>().RecordAsync(person.Id, command.Visibility, ct);

            var kioskId = await WayQueries.NewKioskIdAsync(db, tenantId, ct);
            db.KioskCredentials.Add(KioskCredential.Create(tenantId, person.Id, kioskId, command.KioskPin, now));

            var hasEmail = false;
            var hasExternal = false;
            if (verifiedPasskey is { } verified)
            {
                db.Passkeys.Add(Passkey.Register(tenantId, person.Id, verified.CredentialId, verified.PublicKey, verified.SignCount, verified.AaGuid, command.Passkey!.DeviceName ?? "Passkey", now));
            }

            if (!string.IsNullOrWhiteSpace(command.Email))
            {
                await AddIndexAsync(db, IdentityIndexEntry.HashEmail(command.Email), IdentityKind.Email, tenantId, person.Id, now, ct);
                db.EmailLogins.Add(EmailLogin.Add(tenantId, person.Id, command.Email, now));
                hasEmail = true;
            }

            if (command.External is { } external)
            {
                await AddIndexAsync(db, external.SubjectHash, IdentityKind.External, tenantId, person.Id, now, ct);
                db.ExternalLogins.Add(Domain.ExternalLogin.Link(tenantId, person.Id, external.ProviderKey, external.SubjectHash, now));
                hasExternal = true;
            }

            string? recovery = null;
            if (!hasEmail && !hasExternal)
            {
                // Ohne E-Mail und Anbieter bleibt der Wiederherstellungscode immer aktiv (Zugang 2.2, 4).
                var (entry, code) = RecoveryCode.Issue(tenantId, person.Id, now);
                db.RecoveryCodes.Add(entry);
                recovery = AccessCodes.Grouped(code);
            }

            await db.SaveChangesAsync(ct);

            await sp.GetRequiredService<IMeteringEmitter>().EmitAsync(
                new MeteringEmission("Kern", "member.joined", roleCodeId is null ? "join-code:" + joinCode!.Id.ToString("D") : "role-code:" + roleCodeId.Value.ToString("D"), 1m, MeteringSource.Self, now, $"{person.Id}:member.joined"),
                ct);

            if (roleCode is not null)
            {
                await sp.GetRequiredService<IAuditLog>().RecordAsync(new AuditEntry("access.role_code.redeemed", roleCode.Id.ToString("D"), roleCode.Role), ct);
            }

            await sp.GetRequiredService<ISecurityLog>().RecordAsync(new SecurityEvent(atKiosk ? "join.kiosk" : "join", true, person.Id.ToString(), null), ct);

            IssuedSession? session = atKiosk ? null : await sp.GetRequiredService<ISessionService>().IssueAsync(person.Id, ct);
            await tx.CommitAsync(ct);
            return new JoinResult(tenantId, person.Id, kioskId, recovery, session);
        }
    }

    private static void ValidateWays(JoinCommand command, LoginPolicy policy, bool atKiosk)
    {
        if (string.IsNullOrWhiteSpace(command.DisplayName) || command.DisplayName.Trim().Length > 80)
        {
            throw new AccessDeniedException("display_name_invalid", 422);
        }

        if (atKiosk)
        {
            if (!policy.KioskJoinAllowed)
            {
                throw new AccessDeniedException("kiosk_join_not_allowed");
            }

            if (command.KioskPin is null || !KioskPin.IsAcceptable(command.KioskPin))
            {
                throw new AccessDeniedException("pin_trivial", 422);
            }

            if (command.Passkey is not null || command.Email is not null || command.External is not null)
            {
                throw new AccessDeniedException("kiosk_join_pin_only", 422);
            }

            return;
        }

        if (command.KioskPin is not null && !KioskPin.IsAcceptable(command.KioskPin))
        {
            throw new AccessDeniedException("pin_trivial", 422);
        }

        if (command.Passkey is null && string.IsNullOrWhiteSpace(command.Email) && command.External is null)
        {
            throw new AccessDeniedException("access_way_required", 422);
        }

        if (command.Passkey is not null && !policy.PasskeyEnabled)
        {
            throw new AccessDeniedException("way_disabled");
        }

        if (!string.IsNullOrWhiteSpace(command.Email) && !policy.MagicLinkEnabled)
        {
            throw new AccessDeniedException("way_disabled");
        }

        if (command.External is { } external && !policy.ProviderAllowed(external.ProviderKey))
        {
            throw new AccessDeniedException("provider_not_allowed");
        }

        if (policy.ProviderForced && command.Role is null && command.External is null)
        {
            throw new AccessDeniedException("provider_required");
        }

        if (command.Role is { } role && AccessRules.RequiresRealName(role))
        {
            if (string.IsNullOrWhiteSpace(command.Email) && command.External is null)
            {
                throw new AccessDeniedException("email_or_provider_required", 422);
            }

            if (AccessRules.RequiresPhishingResistantWay(role) && command.Passkey is null && command.External is null)
            {
                throw new AccessDeniedException("passkey_or_provider_required", 422);
            }
        }
    }

    private static async Task AddIndexAsync(IdentityDbContext db, string hash, IdentityKind kind, TenantId tenantId, PersonId personId, DateTimeOffset now, CancellationToken ct)
    {
        // Eine Login-Identität ist mit höchstens einer aktiven Person verknüpft (Zugang 1, 10.4); zusätzlich sichert der Primärschlüssel.
        if (await db.IdentityIndex.AnyAsync(i => i.Hash == hash, ct))
        {
            throw new AccessDeniedException("identity_taken", 409);
        }

        db.IdentityIndex.Add(IdentityIndexEntry.Create(hash, kind, tenantId, personId, now));
    }

    private static async Task<JoinPreview?> PreviewTenantAsync(IServiceProvider sp, IdentityDbContext db, TenantId tenantId, DateTimeOffset now, CancellationToken ct, bool allowProvisioned = false)
    {
        var tenant = await sp.GetRequiredService<IOrganisationDirectory>().GetTenantAsync(tenantId, ct);
        if (tenant is null || (tenant.Value.State != OrganisationState.Active && !(allowProvisioned && tenant.Value.State == OrganisationState.Provisioned)))
        {
            return null;
        }

        var policy = await WayQueries.PolicyAsync(db, tenantId, now, ct);
        var available = await sp.GetRequiredService<IProviderCatalog>().ListForTenantAsync(tenantId, ct);
        return new JoinPreview(tenantId, tenant.Value.DisplayName, new JoinWays(policy.PasskeyEnabled, policy.MagicLinkEnabled, policy.KioskJoinAllowed, available, policy.ForcedProviderKeys));
    }
}
