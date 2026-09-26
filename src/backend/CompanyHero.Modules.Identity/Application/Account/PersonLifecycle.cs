using CompanyHero.Modules.Identity.Application.Sessions;
using CompanyHero.Modules.Identity.Domain;
using CompanyHero.Modules.Identity.Infrastructure;
using CompanyHero.Modules.Organisation.Application;
using CompanyHero.Platform.Audit;
using CompanyHero.Platform.Data;
using CompanyHero.Platform.Jobs;
using CompanyHero.Platform.Privacy;
using CompanyHero.Platform.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace CompanyHero.Modules.Identity.Application.Account;

/// <summary>Anlass des Endes einer Mitgliedschaft (Organisation 3.1, Datenschutz 5.2, 5.3).</summary>
public enum MembershipEndReason
{
    /// <summary>Austritt durch die Person selbst (Zugang 8).</summary>
    Left = 1,

    /// <summary>Entfernt durch einen Tenant-Admin; wirkt wie Austritt, protokolliert.</summary>
    Removed = 2,

    /// <summary>Verwaiste Person: 24 Monate ohne Anmeldung und ohne Handlung (Datenschutz 5.3).</summary>
    Orphaned = 3,

    /// <summary>Löschung des gekündigten Tenants nach der Lesefrist (Organisation 1.3, Datenschutz 5.4).</summary>
    TenantDeleted = 4,
}

/// <summary>
/// Ende einer Mitgliedschaft in einer Transaktion (Zugang 8, Organisation 3.1): Sitzungen, Anmeldewege, Codes, Kennung,
/// PIN und Indexeinträge werden sofort entfernt, die Mitgliedschaft endet, und die Löschfolgen der übrigen Domänen
/// (Datenschutz 5.2) laufen als Job derselben Kontexttransaktion (A-006).
/// </summary>
public interface IPersonLifecycle
{
    Task EndMembershipAsync(PersonId personId, MembershipEndReason reason, CancellationToken cancellationToken);
}

internal sealed class PersonLifecycle(
    IdentityDbContext db,
    IContextTransaction transaction,
    ITenantContextAccessor context,
    ISessionService sessions,
    IOrganisationDirectory organisations,
    IJobQueue jobs,
    IAuditLog audit,
    ISecurityLog security,
    TimeProvider clock) : IPersonLifecycle
{
    public async Task EndMembershipAsync(PersonId personId, MembershipEndReason reason, CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var now = clock.GetUtcNow();
        var person = await db.Persons.SingleOrDefaultAsync(p => p.TenantId == tenantId && p.Id == personId, cancellationToken)
            ?? throw new AccessDeniedException("person_unknown", 404);
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

        if (reason == MembershipEndReason.Removed)
        {
            await organisations.RemoveMemberAsync(personId, cancellationToken);
        }
        else
        {
            await organisations.LeaveAsync(personId, cancellationToken);
        }

        // Löschfolgen der übrigen Domänen binnen 30 Tagen (Datenschutz 5.2): der Job entsteht mit dem Austritt, nie ohne ihn.
        await jobs.EnqueueAsync(new JobRequest(PersonalData.ErasureJobType, personId.ToString(), $"erase:{personId}"), cancellationToken);

        await audit.RecordAsync(new AuditEntry(reason switch
        {
            MembershipEndReason.Removed => "access.person.removed",
            MembershipEndReason.Orphaned => "access.person.orphaned",
            MembershipEndReason.TenantDeleted => "access.person.tenant_deleted",
            _ => "access.person.left",
        }, reason == MembershipEndReason.Left ? null : personId.ToString(), null), cancellationToken);
        await security.RecordAsync(new SecurityEvent(reason == MembershipEndReason.Left ? "leave" : reason == MembershipEndReason.Removed ? "leave.removed" : "leave.orphaned", true, personId.ToString(), null), cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }
}
