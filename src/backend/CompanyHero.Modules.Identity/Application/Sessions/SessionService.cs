using CompanyHero.Modules.Identity.Domain;
using CompanyHero.Modules.Identity.Infrastructure;
using CompanyHero.Modules.Organisation.Application;
using CompanyHero.Platform.Data;
using CompanyHero.Platform.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace CompanyHero.Modules.Identity.Application.Sessions;

/// <summary>Neu ausgestellte Sitzung: das Geheimnis für das Cookie wird genau einmal zurückgegeben.</summary>
public sealed record IssuedSession(Guid Id, string Token, string CsrfToken, SessionKind Kind, TenantId TenantId, PersonId? PersonId, DateTimeOffset AuthenticatedAt, DateTimeOffset? SlidingUntil, DateTimeOffset? AbsoluteUntil, Guid? KioskDeviceId);

/// <summary>Geprüfte Sitzung eines Requests (nach Ablauf- und Widerrufsprüfung, mit Verlängerung).</summary>
public sealed record AuthenticatedSession(Guid Id, TenantId TenantId, PersonId? PersonId, SessionKind Kind, DateTimeOffset AuthenticatedAt, DateTimeOffset? SlidingUntil, DateTimeOffset? AbsoluteUntil, string CsrfToken, Guid? KioskDeviceId);

/// <summary>
/// Serverseitige Sitzungen in PostgreSQL (A-007, A-017, A-005): Ausstellen im Tenant-Kontext, Prüfen im Plattformkontext
/// (vor der Tenant-Zuordnung), Widerruf sofort über alle Instanzen. Die Sitzungsart folgt den Rollen laut Organisation.
/// </summary>
public interface ISessionService
{
    /// <summary>Tenant-Kontext: Sitzung für eine Person; privilegierte Rollen erhalten die kurze Sitzung (Zugang 5).</summary>
    Task<IssuedSession> IssueAsync(PersonId personId, CancellationToken cancellationToken);

    /// <summary>Tenant-Kontext: Kiosk-Personensitzung innerhalb einer Gerätesitzung; beendet vorherige Personensitzungen desselben Geräts (A-005 Personenwechsel).</summary>
    /// <summary>Kiosk-Personensitzung innerhalb der Gerätesitzung; sie übernimmt das CSRF-Token des Geräts (A-005, A-007).</summary>
    Task<IssuedSession> IssueKioskPersonAsync(PersonId personId, Guid kioskDeviceId, int idleSeconds, string deviceCsrfToken, CancellationToken cancellationToken);

    /// <summary>Plattformkontext: Sitzung zum Cookie-Geheimnis prüfen und gleitend verlängern; <c>null</c>, wenn ungültig.</summary>
    Task<AuthenticatedSession?> AuthenticateAsync(string token, CancellationToken cancellationToken);

    /// <summary>Tenant-Kontext: eine Sitzung beenden.</summary>
    Task RevokeAsync(Guid sessionId, CancellationToken cancellationToken);

    /// <summary>Tenant-Kontext: „Abmeldung aus allen Sitzungen“ einschließlich Kiosk-Personensitzungen (Zugang 5).</summary>
    Task<int> RevokeAllForPersonAsync(PersonId personId, CancellationToken cancellationToken);

    /// <summary>Tenant-Kontext: Widerruf eines Geräts beendet alle Personensitzungen auf diesem Gerät (Zugang 6.5).</summary>
    Task<int> RevokeForDeviceAsync(Guid kioskDeviceId, CancellationToken cancellationToken);

    /// <summary>Tenant-Kontext: Sitzung nach Kennung, etwa für die Sitzungsanzeige.</summary>
    Task<AuthenticatedSession?> GetAsync(Guid sessionId, CancellationToken cancellationToken);
}

internal sealed class SessionService(IdentityDbContext db, IContextTransaction transaction, ITenantContextAccessor context, IOrganisationDirectory organisations, TimeProvider clock) : ISessionService
{
    /// <summary>Verlängerungen werden höchstens einmal je Minute geschrieben; Kiosk-Personensitzungen bei jedem Request (Idle-Countdown).</summary>
    private static readonly TimeSpan TouchInterval = TimeSpan.FromMinutes(1);

    public async Task<IssuedSession> IssueAsync(PersonId personId, CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var roles = await organisations.GetRolesAsync(personId, cancellationToken);
        var (session, token) = Session.Issue(tenantId, personId, AccessRules.SessionKindFor(roles), clock.GetUtcNow());
        db.Sessions.Add(session);
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return ToIssued(session, token);
    }

    public async Task<IssuedSession> IssueKioskPersonAsync(PersonId personId, Guid kioskDeviceId, int idleSeconds, string deviceCsrfToken, CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var now = clock.GetUtcNow();
        // Personenwechsel: keine zweite aktive Personensitzung auf demselben Gerät; die vorherige endet sofort (A-005).
        await db.Sessions
            .Where(s => s.TenantId == tenantId && s.KioskDeviceId == kioskDeviceId && s.Kind == SessionKind.KioskPerson && s.RevokedAt == null)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.RevokedAt, now), cancellationToken);
        var (session, token) = Session.Issue(tenantId, personId, SessionKind.KioskPerson, now, kioskDeviceId, idleSeconds, deviceCsrfToken);
        db.Sessions.Add(session);
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return ToIssued(session, token);
    }

    public async Task<AuthenticatedSession?> AuthenticateAsync(string token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 128)
        {
            return null;
        }

        var hash = AccessCodes.Hash(token);
        var now = clock.GetUtcNow();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var session = await db.Sessions.AsNoTracking().SingleOrDefaultAsync(s => s.TokenHash == hash, cancellationToken);
        if (session is null || !session.IsValidAt(now))
        {
            await tx.CommitAsync(cancellationToken);
            return null;
        }

        var idleSeconds = SessionPolicy.KioskIdleDefaultSeconds;
        if (session.Kind == SessionKind.KioskPerson)
        {
            idleSeconds = await db.LoginPolicies.Where(p => p.TenantId == session.TenantId).Select(p => (int?)p.KioskIdleSeconds).SingleOrDefaultAsync(cancellationToken) ?? SessionPolicy.KioskIdleDefaultSeconds;
        }

        if (session.Kind == SessionKind.KioskPerson || now - session.LastSeenAt >= TouchInterval)
        {
            session.Touch(now, idleSeconds);
            // Plattformkontext: Verlängerung als Befehl im eigenen Schema; die Policy erlaubt der Plattform die Zeile.
            await db.Database.ExecuteSqlAsync(
                $"update identity.session set last_seen_at = {session.LastSeenAt}, sliding_until = {session.SlidingUntil} where id = {session.Id} and revoked_at is null",
                cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);
        return new AuthenticatedSession(session.Id, session.TenantId, session.PersonId, session.Kind, session.AuthenticatedAt, session.SlidingUntil, session.AbsoluteUntil, session.CsrfToken, session.KioskDeviceId);
    }

    public async Task RevokeAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var session = await db.Sessions.SingleOrDefaultAsync(s => s.TenantId == tenantId && s.Id == sessionId, cancellationToken);
        session?.Revoke(clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    public async Task<int> RevokeAllForPersonAsync(PersonId personId, CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var now = clock.GetUtcNow();
        var count = await db.Sessions
            .Where(s => s.TenantId == tenantId && s.PersonId == personId && s.RevokedAt == null)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.RevokedAt, now), cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return count;
    }

    public async Task<int> RevokeForDeviceAsync(Guid kioskDeviceId, CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var now = clock.GetUtcNow();
        var count = await db.Sessions
            .Where(s => s.TenantId == tenantId && s.KioskDeviceId == kioskDeviceId && s.RevokedAt == null)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.RevokedAt, now), cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return count;
    }

    public async Task<AuthenticatedSession?> GetAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var s = await db.Sessions.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == sessionId, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return s is null ? null : new AuthenticatedSession(s.Id, s.TenantId, s.PersonId, s.Kind, s.AuthenticatedAt, s.SlidingUntil, s.AbsoluteUntil, s.CsrfToken, s.KioskDeviceId);
    }

    private static IssuedSession ToIssued(Session session, string token) =>
        new(session.Id, token, session.CsrfToken, session.Kind, session.TenantId, session.PersonId, session.AuthenticatedAt, session.SlidingUntil, session.AbsoluteUntil, session.KioskDeviceId);
}
