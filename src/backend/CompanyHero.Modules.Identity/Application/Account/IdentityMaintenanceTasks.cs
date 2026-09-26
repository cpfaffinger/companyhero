using CompanyHero.Modules.Identity.Domain;
using CompanyHero.Modules.Identity.Infrastructure;
using CompanyHero.Modules.Organisation.Application;
using CompanyHero.Platform.Data;
using CompanyHero.Platform.Jobs;
using CompanyHero.Platform.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CompanyHero.Modules.Identity.Application.Account;

/// <summary>Aufbewahrung der Zugangsdaten ohne Fachwert (Datenschutz 5.1, Zugang 5): abgelaufene Sitzungen, Links, Codes, Fehlversuche.</summary>
public static class IdentityRetention
{
    /// <summary>Abgelaufene oder widerrufene Sitzungen bleiben kurz für die Sitzungsanzeige, dann weg.</summary>
    public static TimeSpan ExpiredSessions { get; } = TimeSpan.FromDays(7);

    /// <summary>Verbrauchte oder abgelaufene Magic-Links und Übertragungen.</summary>
    public static TimeSpan ExpiredLinks { get; } = TimeSpan.FromDays(1);

    /// <summary>Abgelaufene, widerrufene oder eingelöste Codes bleiben 30 Tage für das Prüfprotokoll nachvollziehbar.</summary>
    public static TimeSpan ExpiredCodes { get; } = TimeSpan.FromDays(30);

    /// <summary>Fehlversuche wirken 15 Minuten (Zugang 6.5); danach sind sie nur noch Ballast.</summary>
    public static TimeSpan FailedAttempts { get; } = TimeSpan.FromDays(1);

    /// <summary>Verwaiste Personen: 24 Monate ohne Anmeldung und ohne Handlung (Datenschutz 5.3).</summary>
    public static TimeSpan Orphaned { get; } = TimeSpan.FromDays(730);
}

/// <summary>
/// Aufräumlauf des Zugangs (Stufe 4, offener Punkt 2): entfernt je aktivem Tenant abgelaufene Sitzungen, Links, Codes und
/// Fehlversuche nach den Fristen aus <see cref="IdentityRetention"/>. Plattformkontext für die Tenantliste, Arbeit je Tenant in
/// dessen Kontext (Backend 5.3).
/// </summary>
internal sealed class IdentityCleanupTask(ITenantScopeFactory scopes, IOrganisationDirectory organisations, TimeProvider clock, ILogger<IdentityCleanupTask> logger) : IScheduledTask
{
    public static string Name => "identity.cleanup";

    public static TimeSpan Interval => TimeSpan.FromHours(1);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        foreach (var tenant in await organisations.ListActiveTenantsAsync(cancellationToken))
        {
            var removed = await scopes.RunAsync(TenantContext.ForTenant(tenant), (sp, ct) => CleanTenantAsync(sp, tenant, now, ct), cancellationToken);
            if (removed > 0)
            {
                logger.LogInformation("Aufräumlauf Zugang: {Count} Zeilen ohne Fachwert entfernt", removed);
            }
        }
    }

    internal static async Task<int> CleanTenantAsync(IServiceProvider sp, TenantId tenant, DateTimeOffset now, CancellationToken ct)
    {
        var db = sp.GetRequiredService<IdentityDbContext>();
        var tx = await sp.GetRequiredService<IContextTransaction>().BeginAsync(ct);
        await using (tx)
        {
            var sessionsCutoff = now - IdentityRetention.ExpiredSessions;
            var linksCutoff = now - IdentityRetention.ExpiredLinks;
            var codesCutoff = now - IdentityRetention.ExpiredCodes;
            var attemptsCutoff = now - IdentityRetention.FailedAttempts;
            var removed = 0;
            removed += await db.Sessions.Where(s => s.TenantId == tenant && ((s.RevokedAt != null && s.RevokedAt < sessionsCutoff) || (s.SlidingUntil != null && s.SlidingUntil < sessionsCutoff) || (s.AbsoluteUntil != null && s.AbsoluteUntil < sessionsCutoff))).ExecuteDeleteAsync(ct);
            removed += await db.MagicLinks.Where(m => m.TenantId == tenant && (m.UsedAt != null || m.ExpiresAt < linksCutoff)).ExecuteDeleteAsync(ct);
            removed += await db.TransferLinks.Where(t => t.TenantId == tenant && (t.UsedAt != null || t.ExpiresAt < linksCutoff)).ExecuteDeleteAsync(ct);
            removed += await db.RoleCodes.Where(r => r.TenantId == tenant && ((r.RedeemedAt != null && r.RedeemedAt < codesCutoff) || (r.RevokedAt != null && r.RevokedAt < codesCutoff) || r.ExpiresAt < codesCutoff)).ExecuteDeleteAsync(ct);
            removed += await db.JoinCodes.Where(j => j.TenantId == tenant && ((j.RevokedAt != null && j.RevokedAt < codesCutoff) || j.ExpiresAt < codesCutoff)).ExecuteDeleteAsync(ct);
            removed += await db.KioskFailedAttempts.Where(a => a.TenantId == tenant && a.AttemptedAt < attemptsCutoff).ExecuteDeleteAsync(ct);
            await tx.CommitAsync(ct);
            return removed;
        }
    }
}

/// <summary>
/// Verwaiste Personen (Datenschutz 5.3, A-024): ohne Anmeldung und ohne Handlung über 24 Monate werden Personen automatisch
/// wie ausgetreten behandelt. Jede Handlung setzt eine Sitzung voraus (auch die Kiosk-Personensitzung); der jüngste
/// Sitzungskontakt ist daher das Maß. Der Hinweis 30 Tage vorher an hinterlegte E-Mail-Adressen folgt mit den
/// Benachrichtigungen (Kategorie Konto), die die Frist über <see cref="IPersonDirectory"/> abfragen.
/// </summary>
internal sealed class OrphanedPersonsTask(ITenantScopeFactory scopes, IOrganisationDirectory organisations, TimeProvider clock, ILogger<OrphanedPersonsTask> logger) : IScheduledTask
{
    public static string Name => "identity.orphans";

    public static TimeSpan Interval => TimeSpan.FromHours(24);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var cutoff = clock.GetUtcNow() - IdentityRetention.Orphaned;
        foreach (var tenant in await organisations.ListActiveTenantsAsync(cancellationToken))
        {
            var orphans = await scopes.RunAsync(TenantContext.ForTenant(tenant), (sp, ct) => sp.GetRequiredService<IPersonDirectory>().ListOrphanedAsync(cutoff, ct), cancellationToken);
            foreach (var person in orphans)
            {
                await scopes.RunAsync(TenantContext.ForTenant(tenant), (sp, ct) => sp.GetRequiredService<IPersonLifecycle>().EndMembershipAsync(person, MembershipEndReason.Orphaned, ct), cancellationToken);
            }

            if (orphans.Count > 0)
            {
                logger.LogInformation("Verwaiste Personen: {Count} nach 24 Monaten ohne Anmeldung ausgetreten", orphans.Count);
            }
        }
    }
}
