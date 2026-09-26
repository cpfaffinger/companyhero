using CompanyHero.Modules.Organisation.Domain;
using CompanyHero.Modules.Privacy.Domain;
using CompanyHero.Modules.Privacy.Infrastructure;
using CompanyHero.Platform.Audit;
using CompanyHero.Platform.Data;
using CompanyHero.Platform.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace CompanyHero.Modules.Privacy.Application;

/// <summary>Eintrag des Prüfprotokolls aus Sicht der Einsichtsrolle und des Tenant-Admins (Datenschutz 6.3).</summary>
public sealed record AuditEntryRecord(Guid Id, DateTimeOffset OccurredAt, string? Actor, string ActorRoles, string Action, string? SubjectRef, string? Detail);

public sealed record SecurityEntryRecord(Guid Id, DateTimeOffset OccurredAt, string EventType, bool Success, string? Pseudonym, string? Detail);

/// <summary>Lesezugriff auf Prüf- und Sicherheitsprotokoll (Datenschutz 6.2, 6.3): Tenant-Admin und Einsichtsrolle.</summary>
public interface IAuditReader
{
    Task<IReadOnlyList<AuditEntryRecord>> ListAuditAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<SecurityEntryRecord>> ListSecurityAsync(CancellationToken cancellationToken);
}

/// <summary>Eigentümer des Querschnitts „Prüfprotokoll“ (Domänenkarte 3): schreibt in derselben Kontexttransaktion wie die Änderung.</summary>
internal sealed class AuditLog(PrivacyDbContext db, IContextTransaction transaction, ITenantContextAccessor context, TimeProvider clock) : IAuditLog
{
    public async Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var current = context.Require();
        var tenantId = current.RequireTenant();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        db.AuditRecords.Add(AuditRecord.Create(tenantId, clock.GetUtcNow(), current.PersonId, current.Roles, entry.Action, entry.SubjectRef, entry.Detail));
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }
}

internal sealed class SecurityLog(PrivacyDbContext db, IContextTransaction transaction, ITenantContextAccessor context, TimeProvider clock) : ISecurityLog
{
    public async Task RecordAsync(SecurityEvent securityEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(securityEvent);
        var tenantId = context.Require().RequireTenant();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        db.SecurityRecords.Add(SecurityRecord.Create(tenantId, clock.GetUtcNow(), securityEvent.EventType, securityEvent.Success, securityEvent.Pseudonym, securityEvent.Detail));
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }
}

internal sealed class AuditReader(PrivacyDbContext db, IContextTransaction transaction, ITenantContextAccessor context) : IAuditReader
{
    public async Task<IReadOnlyList<AuditEntryRecord>> ListAuditAsync(CancellationToken cancellationToken)
    {
        var tenantId = RequireReader();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var entries = await db.AuditRecords.Where(a => a.TenantId == tenantId).OrderBy(a => a.OccurredAt).ThenBy(a => a.Id).ToListAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return entries.Select(a => new AuditEntryRecord(a.Id, a.OccurredAt, a.Actor?.ToString(), a.ActorRoles, a.Action, a.SubjectRef, a.Detail)).ToList();
    }

    public async Task<IReadOnlyList<SecurityEntryRecord>> ListSecurityAsync(CancellationToken cancellationToken)
    {
        var tenantId = RequireReader();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var entries = await db.SecurityRecords.Where(a => a.TenantId == tenantId).OrderBy(a => a.OccurredAt).ThenBy(a => a.Id).ToListAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return entries.Select(a => new SecurityEntryRecord(a.Id, a.OccurredAt, a.EventType, a.Success, a.Pseudonym, a.Detail)).ToList();
    }

    private TenantId RequireReader()
    {
        var current = context.Require();
        if (!current.HasRole(Role.TenantAdmin) && !current.HasRole(Role.Insight))
        {
            throw new UnauthorizedAccessException("Das Prüfprotokoll lesen Tenant-Admin und Einsichtsrolle (Datenschutz 6.3).");
        }

        return current.RequireTenant();
    }
}
