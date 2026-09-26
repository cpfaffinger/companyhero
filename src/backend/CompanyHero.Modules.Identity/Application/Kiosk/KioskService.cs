using CompanyHero.Modules.Identity.Application.Sessions;
using CompanyHero.Modules.Identity.Domain;
using CompanyHero.Modules.Identity.Infrastructure;
using CompanyHero.Platform.Audit;
using CompanyHero.Platform.Data;
using CompanyHero.Platform.Metering;
using CompanyHero.Platform.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CompanyHero.Modules.Identity.Application.Kiosk;

/// <summary>Geprüfte Gerätesitzung; <see cref="RotatedSecret"/> ist gesetzt, wenn das Geheimnis mit dieser Antwort rotiert.</summary>
public sealed record DeviceAuthentication(TenantId TenantId, Guid DeviceId, string? RotatedSecret);

internal sealed record DeviceLocator(TenantId TenantId, Guid Id);

public sealed record KioskDeviceRecord(Guid Id, string Name, DateTimeOffset CreatedAt, DateTimeOffset? RegisteredAt, DateTimeOffset? RevokedAt, DateTimeOffset? LastSeenAt, int LoginCount);

public sealed record KioskDeviceState(TenantId TenantId, Guid DeviceId, string Name, int IdleSeconds);

public sealed record KioskLogin(IssuedSession Session, string DisplayName, int IdleSeconds);

public sealed record KioskTransfer(string Url, DateTimeOffset ExpiresAt);

/// <summary>Kiosk (Zugang 6, A-018): Geräteregistrierung, Gerätesitzung, persönliche Anmeldung mit Drosselung, PIN, Übertragung.</summary>
public interface IKioskService
{
    /// <summary>Tenant-Admin, sensible Aktion: Gerät anlegen; Registrierungscode einmalig, 15 Minuten.</summary>
    Task<(KioskDeviceRecord Device, string RegistrationCode)> CreateDeviceAsync(string name, CancellationToken cancellationToken);

    Task<IReadOnlyList<KioskDeviceRecord>> ListDevicesAsync(CancellationToken cancellationToken);

    /// <summary>Widerruf beendet alle Personensitzungen des Geräts (Zugang 6.5).</summary>
    Task<bool> RevokeDeviceAsync(Guid deviceId, CancellationToken cancellationToken);

    /// <summary>Ohne Sitzung: Registrierungscode einlösen; liefert das Gerätegeheimnis für das Cookie.</summary>
    Task<(KioskDeviceState State, string Secret)> RegisterAsync(string registrationCode, CancellationToken cancellationToken);

    /// <summary>Plattformkontext: Gerätegeheimnis prüfen; rotiert nach 30 Tagen.</summary>
    Task<DeviceAuthentication?> AuthenticateDeviceAsync(string secret, CancellationToken cancellationToken);

    /// <summary>Gerätesitzung: Zustand des Geräts für die Anzeige (nie Personen).</summary>
    Task<KioskDeviceState> GetDeviceStateAsync(CancellationToken cancellationToken);

    /// <summary>Gerätesitzung: Anmeldung mit Kennung und PIN; Drosselung je Kennung und Gerät.</summary>
    Task<KioskLogin> LoginAsync(string kioskId, string pin, string deviceCsrfToken, CancellationToken cancellationToken);

    /// <summary>Kiosk-Personensitzung oder Mitgliedssitzung: PIN ändern.</summary>
    Task ChangePinAsync(string pin, CancellationToken cancellationToken);

    /// <summary>Gerätesitzung: PIN mit Wiederherstellungscode neu setzen (Personen ohne eigenes Gerät, Zugang 6.2).</summary>
    Task ResetPinAsync(string kioskId, string recoveryCode, string pin, CancellationToken cancellationToken);

    /// <summary>Kiosk-Personensitzung: einmaliger Link, fünf Minuten, als QR für das eigene Gerät (Zugang 6.4).</summary>
    Task<KioskTransfer> CreateTransferAsync(CancellationToken cancellationToken);
}

internal sealed class KioskService(
    IdentityDbContext db,
    IContextTransaction transaction,
    ITenantContextAccessor context,
    ITenantScopeFactory scopes,
    ISessionService sessions,
    IAuditLog audit,
    ISecurityLog security,
    IOptions<IdentityOptions> options,
    TimeProvider clock) : IKioskService
{
    public async Task<(KioskDeviceRecord Device, string RegistrationCode)> CreateDeviceAsync(string name, CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var (device, code) = KioskDevice.Create(tenantId, name, clock.GetUtcNow());
        db.KioskDevices.Add(device);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditEntry("access.kiosk_device.created", device.Id.ToString("D"), null), cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return (ToRecord(device), AccessCodes.Grouped(code, 4));
    }

    public async Task<IReadOnlyList<KioskDeviceRecord>> ListDevicesAsync(CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var devices = await db.KioskDevices.AsNoTracking().Where(d => d.TenantId == tenantId).OrderBy(d => d.Id).ToListAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return devices.Select(ToRecord).ToList();
    }

    public async Task<bool> RevokeDeviceAsync(Guid deviceId, CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var device = await db.KioskDevices.SingleOrDefaultAsync(d => d.TenantId == tenantId && d.Id == deviceId, cancellationToken);
        if (device is null)
        {
            await tx.CommitAsync(cancellationToken);
            return false;
        }

        device.Revoke(clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        await sessions.RevokeForDeviceAsync(deviceId, cancellationToken);
        await audit.RecordAsync(new AuditEntry("access.kiosk_device.revoked", deviceId.ToString("D"), null), cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<(KioskDeviceState State, string Secret)> RegisterAsync(string registrationCode, CancellationToken cancellationToken)
    {
        var normalized = AccessCodes.Normalize(registrationCode ?? string.Empty);
        if (!AccessCodes.IsWellFormed(normalized, AccessCodes.KioskRegistrationCodeLength))
        {
            throw new AccessDeniedException("registration_code_invalid", 404);
        }

        var hash = AccessCodes.Hash(normalized);
        var found = await scopes.RunAsync<DeviceLocator?>(TenantContext.ForPlatform(), async (sp, ct) =>
        {
            var pdb = sp.GetRequiredService<IdentityDbContext>();
            var tx = await sp.GetRequiredService<IContextTransaction>().BeginAsync(ct);
            await using (tx)
            {
                var row = await pdb.KioskDevices.AsNoTracking().Where(d => d.RegistrationCodeHash == hash).Select(d => new DeviceLocator(d.TenantId, d.Id)).FirstOrDefaultAsync(ct);
                await tx.CommitAsync(ct);
                return row;
            }
        }, cancellationToken) ?? throw new AccessDeniedException("registration_code_invalid", 404);

        return await scopes.RunAsync(TenantContext.ForTenant(found.TenantId), async (sp, ct) =>
        {
            var tdb = sp.GetRequiredService<IdentityDbContext>();
            var tx = await sp.GetRequiredService<IContextTransaction>().BeginAsync(ct);
            await using (tx)
            {
                var now = clock.GetUtcNow();
                var device = await tdb.KioskDevices.SingleAsync(d => d.TenantId == found.TenantId && d.Id == found.Id, ct);
                if (!device.RegistrationCodeMatches(normalized, now))
                {
                    throw new AccessDeniedException("registration_code_invalid", 404);
                }

                var secret = device.Register(now);
                await tdb.SaveChangesAsync(ct);
                await sp.GetRequiredService<IMeteringEmitter>().EmitAsync(DeviceMonth(device, now), ct);
                await sp.GetRequiredService<IAuditLog>().RecordAsync(new AuditEntry("access.kiosk_device.registered", device.Id.ToString("D"), null), ct);
                var policy = await WayQueries.PolicyAsync(tdb, device.TenantId, now, ct);
                await tx.CommitAsync(ct);
                return (new KioskDeviceState(device.TenantId, device.Id, device.Name, policy.KioskIdleSeconds), secret);
            }
        }, cancellationToken);
    }

    /// <summary>Metering-Ereignis je Gerät und Monat (Zugang 9: <c>kiosk.device_month</c>), ohne Personenbezug, dedupliziert je Periode.</summary>
    public static MeteringEmission DeviceMonth(KioskDevice device, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(device);
        var period = TenantTimeZone.PeriodOf(now);
        return new MeteringEmission("Kern", "kiosk.device_month", "kiosk-device:" + device.Id.ToString("D"), 1m, MeteringSource.Platform, now, $"{device.Id:D}:{period}:kiosk.device_month");
    }

    public async Task<DeviceAuthentication?> AuthenticateDeviceAsync(string secret, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(secret) || secret.Length > 128)
        {
            return null;
        }

        var hash = AccessCodes.Hash(secret);
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var device = await db.KioskDevices.AsNoTracking().SingleOrDefaultAsync(d => d.SecretHash == hash, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        if (device is null || !device.IsActive)
        {
            return null;
        }

        var now = clock.GetUtcNow();
        string? rotated = null;
        if (device.NeedsRotation(now) || device.LastSeenAt is null || now - device.LastSeenAt.Value >= TimeSpan.FromMinutes(1))
        {
            rotated = await scopes.RunAsync(TenantContext.ForTenant(device.TenantId), async (sp, ct) =>
            {
                var tdb = sp.GetRequiredService<IdentityDbContext>();
                var ttx = await sp.GetRequiredService<IContextTransaction>().BeginAsync(ct);
                await using (ttx)
                {
                    var tracked = await tdb.KioskDevices.SingleAsync(d => d.TenantId == device.TenantId && d.Id == device.Id, ct);
                    string? newSecret = tracked.NeedsRotation(now) ? tracked.Rotate(now) : null;
                    tracked.Seen(now);
                    await tdb.SaveChangesAsync(ct);
                    await ttx.CommitAsync(ct);
                    return newSecret;
                }
            }, cancellationToken);
        }

        return new DeviceAuthentication(device.TenantId, device.Id, rotated);
    }

    public async Task<KioskDeviceState> GetDeviceStateAsync(CancellationToken cancellationToken)
    {
        var (tenantId, deviceId) = RequireDevice();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var device = await db.KioskDevices.AsNoTracking().SingleOrDefaultAsync(d => d.TenantId == tenantId && d.Id == deviceId, cancellationToken)
            ?? throw new AccessDeniedException("device_unknown", 401);
        var policy = await WayQueries.PolicyAsync(db, tenantId, clock.GetUtcNow(), cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return new KioskDeviceState(tenantId, deviceId, device.Name, policy.KioskIdleSeconds);
    }

    public async Task<KioskLogin> LoginAsync(string kioskId, string pin, string deviceCsrfToken, CancellationToken cancellationToken)
    {
        var (tenantId, deviceId) = RequireDevice();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var now = clock.GetUtcNow();
        var policy = await WayQueries.PolicyAsync(db, tenantId, now, cancellationToken);
        var windowStart = now - KioskThrottle.Window;
        var attempts = await db.KioskFailedAttempts.AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.DeviceId == deviceId && a.AttemptedAt > windowStart)
            .Select(a => new { a.KioskIdHash, a.AttemptedAt })
            .ToListAsync(cancellationToken);
        var pseudonym = KioskFailedAttempt.PseudonymFor(tenantId, kioskId ?? string.Empty);
        var verdict = KioskThrottle.Evaluate(attempts.Select(a => a.AttemptedAt).ToList(), attempts.Where(a => a.KioskIdHash == pseudonym).Select(a => a.AttemptedAt).ToList(), now);
        if (verdict != KioskThrottle.Verdict.Open)
        {
            await security.RecordAsync(new SecurityEvent("kiosk.login", false, pseudonym, verdict == KioskThrottle.Verdict.DeviceLocked ? "device_locked" : "kiosk_id_locked"), cancellationToken);
            await tx.CommitAsync(cancellationToken);
            throw new AccessDeniedException(verdict == KioskThrottle.Verdict.DeviceLocked ? "device_locked" : "kiosk_id_locked", 423);
        }

        var credential = await db.KioskCredentials.SingleOrDefaultAsync(k => k.TenantId == tenantId && k.KioskId == kioskId, cancellationToken);
        var person = credential is null ? null : await db.Persons.SingleOrDefaultAsync(p => p.TenantId == tenantId && p.Id == credential.PersonId && p.State == PersonState.Active, cancellationToken);
        var kioskOpen = credential is not null && policy.WayOpen(LoginWay.Kiosk, personHasOnlyThisWay: false, now);
        if (credential is null || person is null || credential.PinHash is null || !credential.PinMatches(pin ?? string.Empty) || !kioskOpen)
        {
            // Fehlversuch pseudonymisiert erfassen (Zugang 6.2); gleiche Antwort für unbekannte Kennung und falsche PIN.
            db.KioskFailedAttempts.Add(KioskFailedAttempt.Record(tenantId, deviceId, kioskId ?? string.Empty, now));
            await db.SaveChangesAsync(cancellationToken);
            await security.RecordAsync(new SecurityEvent("kiosk.login", false, pseudonym, kioskOpen || credential is null ? "wrong_credentials" : "way_disabled"), cancellationToken);
            await tx.CommitAsync(cancellationToken);
            throw new AccessDeniedException("kiosk_login_failed", 401);
        }

        var device = await db.KioskDevices.SingleAsync(d => d.TenantId == tenantId && d.Id == deviceId, cancellationToken);
        device.CountLogin(now);
        await db.SaveChangesAsync(cancellationToken);
        var session = await sessions.IssueKioskPersonAsync(person.Id, deviceId, policy.KioskIdleSeconds, deviceCsrfToken, cancellationToken);
        await security.RecordAsync(new SecurityEvent("kiosk.login", true, person.Id.ToString(), null), cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return new KioskLogin(session, person.DisplayName, policy.KioskIdleSeconds);
    }

    public async Task ChangePinAsync(string pin, CancellationToken cancellationToken)
    {
        var current = context.Require();
        var tenantId = current.RequireTenant();
        var personId = current.RequirePerson();
        if (!KioskPin.IsAcceptable(pin))
        {
            throw new AccessDeniedException("pin_trivial", 422);
        }

        await using var tx = await transaction.BeginAsync(cancellationToken);
        var credential = await db.KioskCredentials.SingleOrDefaultAsync(k => k.TenantId == tenantId && k.PersonId == personId, cancellationToken);
        if (credential is null)
        {
            credential = KioskCredential.Create(tenantId, personId, await WayQueries.NewKioskIdAsync(db, tenantId, cancellationToken), null, clock.GetUtcNow());
            db.KioskCredentials.Add(credential);
        }

        credential.ChangePin(pin, clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    public async Task ResetPinAsync(string kioskId, string recoveryCode, string pin, CancellationToken cancellationToken)
    {
        var (tenantId, deviceId) = RequireDevice();
        if (!KioskPin.IsAcceptable(pin))
        {
            throw new AccessDeniedException("pin_trivial", 422);
        }

        await using var tx = await transaction.BeginAsync(cancellationToken);
        var now = clock.GetUtcNow();
        var credential = await db.KioskCredentials.SingleOrDefaultAsync(k => k.TenantId == tenantId && k.KioskId == kioskId, cancellationToken);
        var recovery = credential is null ? null : await db.RecoveryCodes.SingleOrDefaultAsync(r => r.TenantId == tenantId && r.PersonId == credential.PersonId, cancellationToken);
        if (credential is null || recovery is null || !recovery.Matches(recoveryCode ?? string.Empty))
        {
            db.KioskFailedAttempts.Add(KioskFailedAttempt.Record(tenantId, deviceId, kioskId ?? string.Empty, now));
            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            throw new AccessDeniedException("kiosk_login_failed", 401);
        }

        credential.ChangePin(pin, now);
        recovery.Renew(now);
        await db.SaveChangesAsync(cancellationToken);
        await security.RecordAsync(new SecurityEvent("kiosk.pin_reset", true, credential.PersonId.ToString(), null), cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    public async Task<KioskTransfer> CreateTransferAsync(CancellationToken cancellationToken)
    {
        var current = context.Require();
        if (current.Session != SessionKind.KioskPerson)
        {
            throw new AccessDeniedException("kiosk_person_session_required");
        }

        var tenantId = current.RequireTenant();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var (link, token) = TransferLink.Issue(tenantId, current.RequirePerson(), clock.GetUtcNow());
        db.TransferLinks.Add(link);
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return new KioskTransfer(options.Value.PublicOriginUri.GetLeftPart(UriPartial.Authority) + "/zugang/transfer?token=" + Uri.EscapeDataString(token), link.ExpiresAt);
    }

    private (TenantId TenantId, Guid DeviceId) RequireDevice()
    {
        var current = context.Require();
        return current.KioskDeviceId is { } device ? (current.RequireTenant(), device) : throw new AccessDeniedException("kiosk_device_session_required", 401);
    }

    private static KioskDeviceRecord ToRecord(KioskDevice d) => new(d.Id, d.Name, d.CreatedAt, d.RegisteredAt, d.RevokedAt, d.LastSeenAt, d.LoginCount);
}

/// <summary>Zeitgesteuert: <c>kiosk.device_month</c> je aktivem Gerät und Periode (Zugang 9); dedupliziert über den Idempotenzschlüssel.</summary>
internal sealed class KioskDeviceMonthTask(ITenantScopeFactory scopes, Modules.Organisation.Application.IOrganisationDirectory organisations, TimeProvider clock) : Platform.Jobs.IScheduledTask
{
    public static string Name => "identity.kiosk.device_month";

    public static TimeSpan Interval => TimeSpan.FromHours(6);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var tenants = await organisations.ListActiveTenantsAsync(cancellationToken);
        var now = clock.GetUtcNow();
        foreach (var tenant in tenants)
        {
            await scopes.RunAsync(TenantContext.ForTenant(tenant), async (sp, ct) =>
            {
                var db = sp.GetRequiredService<IdentityDbContext>();
                var tx = await sp.GetRequiredService<IContextTransaction>().BeginAsync(ct);
                await using (tx)
                {
                    var devices = await db.KioskDevices.AsNoTracking().Where(d => d.TenantId == tenant && d.RegisteredAt != null && d.RevokedAt == null).ToListAsync(ct);
                    var metering = sp.GetRequiredService<IMeteringEmitter>();
                    foreach (var device in devices)
                    {
                        await metering.EmitAsync(KioskService.DeviceMonth(device, now), ct);
                    }

                    await tx.CommitAsync(ct);
                }
            }, cancellationToken);
        }
    }
}
