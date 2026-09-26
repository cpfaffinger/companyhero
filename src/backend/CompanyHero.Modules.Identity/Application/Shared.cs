using CompanyHero.Modules.Identity.Domain;
using CompanyHero.Modules.Identity.Infrastructure;
using CompanyHero.Platform.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace CompanyHero.Modules.Identity.Application;

/// <summary>Fachliche Ablehnung eines Zugangsvorgangs mit stabiler Kennung für Vertrag und Oberfläche (K15).</summary>
public sealed class AccessDeniedException(string reason, int status = 403) : InvalidOperationException(reason)
{
    public string Reason { get; } = reason;

    public int Status { get; } = status;
}

/// <summary>Identität eines Anbieters, geprüft durch die OIDC-Middleware; nur Schlüssel und Hash (A-007).</summary>
public sealed record ExternalIdentity(string ProviderKey, string SubjectHash);

/// <summary>Fundstelle eines Anmeldewegs vor der Tenant-Zuordnung.</summary>
public sealed record Locator(TenantId TenantId, PersonId PersonId);

/// <summary>Gemeinsame Abfragen der Anmeldewege einer Person (Zugang 4).</summary>
internal static class WayQueries
{
    public static async Task<(bool Passkey, bool Email, bool External, bool Kiosk)> WaysOfAsync(IdentityDbContext db, TenantId tenantId, PersonId personId, CancellationToken ct) =>
        (
            await db.Passkeys.AnyAsync(p => p.TenantId == tenantId && p.PersonId == personId, ct),
            await db.EmailLogins.AnyAsync(e => e.TenantId == tenantId && e.PersonId == personId, ct),
            await db.ExternalLogins.AnyAsync(e => e.TenantId == tenantId && e.PersonId == personId, ct),
            await db.KioskCredentials.AnyAsync(k => k.TenantId == tenantId && k.PersonId == personId && k.PinHash != null, ct));

    public static async Task<LoginPolicy> PolicyAsync(IdentityDbContext db, TenantId tenantId, DateTimeOffset now, CancellationToken ct) =>
        await db.LoginPolicies.SingleOrDefaultAsync(p => p.TenantId == tenantId, ct) ?? LoginPolicy.Default(tenantId, now);

    /// <summary>Kiosk-Kennung, sechs Ziffern, je Tenant eindeutig (Zugang 6.2).</summary>
    public static async Task<string> NewKioskIdAsync(IdentityDbContext db, TenantId tenantId, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var candidate = AccessCodes.NewKioskId();
            if (!await db.KioskCredentials.AnyAsync(k => k.TenantId == tenantId && k.KioskId == candidate, ct))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Keine freie Kiosk-Kennung gefunden.");
    }
}
