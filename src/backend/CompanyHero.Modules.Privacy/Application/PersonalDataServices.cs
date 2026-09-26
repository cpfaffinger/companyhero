using System.Text.Json.Nodes;
using CompanyHero.Modules.Identity.Application.Account;
using CompanyHero.Modules.Organisation.Application;
using CompanyHero.Modules.Privacy.Domain;
using CompanyHero.Modules.Privacy.Infrastructure;
using CompanyHero.Platform.Data;
using CompanyHero.Platform.Jobs;
using CompanyHero.Platform.Privacy;
using CompanyHero.Platform.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CompanyHero.Modules.Privacy.Application;

public sealed record ConsentRecord(Guid Id, DateTimeOffset OccurredAt, string Kind, string? Previous, string Next);

/// <summary>Auskunft und Zustimmungsprotokoll der angemeldeten Person (Datenschutz 6.1, 6.4, A-025).</summary>
public interface IPersonalDataService
{
    /// <summary>Selbstexport: alle Daten mit Personenbezug über alle Domänen, das Zustimmungsprotokoll, die Anmeldewege ohne Geheimnisse; keine Daten anderer Personen.</summary>
    Task<JsonObject> ExportAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<ConsentRecord>> ListConsentsAsync(CancellationToken cancellationToken);
}

internal sealed class PersonalDataService(PrivacyDbContext db, IContextTransaction transaction, ITenantContextAccessor context, IEnumerable<IPersonalDataExporter> exporters, TimeProvider clock) : IPersonalDataService
{
    public async Task<JsonObject> ExportAsync(CancellationToken cancellationToken)
    {
        var current = context.Require();
        if (current.IsKiosk)
        {
            throw new UnauthorizedAccessException("Der Export läuft nur auf dem eigenen Gerät (Datenschutz 6.4).");
        }

        var personId = current.RequirePerson();
        var export = new JsonObject
        {
            ["format"] = "companyhero-selbstexport/1",
            ["erstellt"] = clock.GetUtcNow(),
            ["tenant"] = current.RequireTenant().ToString(),
            ["person"] = personId.ToString(),
        };
        foreach (var exporter in exporters.OrderBy(e => e.Section, StringComparer.Ordinal))
        {
            export[exporter.Section] = await exporter.ExportAsync(personId, cancellationToken);
        }

        return export;
    }

    public async Task<IReadOnlyList<ConsentRecord>> ListConsentsAsync(CancellationToken cancellationToken)
    {
        var current = context.Require();
        var tenantId = current.RequireTenant();
        var subjectRef = ConsentEntry.SubjectRefOf(current.RequirePerson());
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var entries = await db.Consents.AsNoTracking().Where(c => c.TenantId == tenantId && c.SubjectRef == subjectRef).OrderBy(c => c.OccurredAt).ThenBy(c => c.Id).ToListAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return entries.Select(c => new ConsentRecord(c.Id, c.OccurredAt, c.Kind, c.Previous, c.Next)).ToList();
    }
}

/// <summary>Eigene Abschnitte der Domäne Datenschutz im Export und in der Löschung: Sichtbarkeit und Zustimmungsprotokoll.</summary>
internal sealed class PrivacyPersonalData(PrivacyDbContext db, IContextTransaction transaction, ITenantContextAccessor context) : IPersonalDataExporter, IPersonalDataEraser
{
    public string Section => "datenschutz";

    public async Task<JsonNode> ExportAsync(PersonId personId, CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        var subjectRef = ConsentEntry.SubjectRefOf(personId);
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var level = await db.VisibilitySettings.AsNoTracking().Where(v => v.TenantId == tenantId && v.PersonId == personId).Select(v => (VisibilityLevel?)v.Level).SingleOrDefaultAsync(cancellationToken);
        var consents = await db.Consents.AsNoTracking().Where(c => c.TenantId == tenantId && c.SubjectRef == subjectRef).OrderBy(c => c.OccurredAt).ToListAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return new JsonObject
        {
            ["sichtbarkeit"] = level?.ToString(),
            ["zustimmungen"] = new JsonArray(consents.Select(c => (JsonNode)new JsonObject { ["zeitpunkt"] = c.OccurredAt, ["art"] = c.Kind, ["vorher"] = c.Previous, ["nachher"] = c.Next }).ToArray()),
        };
    }

    public async Task EraseAsync(PersonId personId, CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        var subjectRef = ConsentEntry.SubjectRefOf(personId);
        await using var tx = await transaction.BeginAsync(cancellationToken);
        await db.VisibilitySettings.Where(v => v.TenantId == tenantId && v.PersonId == personId).ExecuteDeleteAsync(cancellationToken);
        // Zustimmungsprotokoll bleibt 3 Jahre mit nicht rückführbarer Kennung (Datenschutz 5.2).
        var anonymous = ConsentEntry.AnonymousRef(Guid.NewGuid());
        await db.Consents.Where(c => c.TenantId == tenantId && c.SubjectRef == subjectRef).ExecuteUpdateAsync(u => u.SetProperty(c => c.SubjectRef, anonymous), cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }
}

/// <summary>
/// Löschfolgen des Austritts (Datenschutz 5.2, A-024) als Job im Tenant-Kontext: jedes Modul führt seine Löschung aus,
/// idempotent; Kollektivsummen bleiben, Zustimmungsprotokoll und Metering behalten nicht rückführbare Kennungen.
/// </summary>
internal sealed class PersonErasureHandler(IEnumerable<IPersonalDataEraser> erasers, ILogger<PersonErasureHandler> logger) : IJobHandler
{
    public static string JobType => PersonalData.ErasureJobType;

    public async Task HandleAsync(JobExecution job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        var personId = new PersonId(Guid.ParseExact(job.Reference, "D"));
        foreach (var eraser in erasers)
        {
            await eraser.EraseAsync(personId, cancellationToken);
        }

        logger.LogInformation("Löschfolgen des Austritts ausgeführt ({Count} Domänen)", erasers.Count());
    }
}

/// <summary>Fristen der Domäne Datenschutz (Datenschutz 5.1, A-024).</summary>
public static class PrivacyRetention
{
    public static TimeSpan SecurityLog { get; } = TimeSpan.FromDays(365);

    public static TimeSpan AuditLog { get; } = TimeSpan.FromDays(3 * 365);

    public static TimeSpan Consents { get; } = TimeSpan.FromDays(3 * 365);
}

/// <summary>
/// Zeitgesteuerter Aufräumlauf der Fristen (Datenschutz 5.1, 5.4): Sicherheitsprotokoll 12 Monate, Prüf- und
/// Zustimmungsprotokoll 3 Jahre; gekündigte Tenants nach Ablauf der Lesefrist: Austritt aller Personen mit Löschfolgen,
/// dann Zustand „Gelöscht“. Plattformkontext für die Tenantliste, Arbeit je Tenant in dessen Kontext.
/// </summary>
internal sealed class PrivacyRetentionTask(ITenantScopeFactory scopes, IOrganisationDirectory organisations, TimeProvider clock, ILogger<PrivacyRetentionTask> logger) : IScheduledTask
{
    public static string Name => "privacy.retention";

    public static TimeSpan Interval => TimeSpan.FromHours(24);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        foreach (var tenant in await organisations.ListActiveTenantsAsync(cancellationToken))
        {
            var removed = await scopes.RunAsync(TenantContext.ForTenant(tenant), (sp, ct) => CleanTenantAsync(sp, tenant, now, ct), cancellationToken);
            if (removed > 0)
            {
                logger.LogInformation("Fristenlauf: {Count} Protokolleinträge nach Ablauf entfernt", removed);
            }
        }

        foreach (var tenant in await organisations.ListTenantsDueForDeletionAsync(cancellationToken))
        {
            var members = await scopes.RunAsync(TenantContext.ForTenant(tenant), (sp, ct) => sp.GetRequiredService<IOrganisationDirectory>().ListActiveMemberIdsAsync(ct), cancellationToken);
            foreach (var person in members)
            {
                await scopes.RunAsync(TenantContext.ForTenant(tenant), (sp, ct) => sp.GetRequiredService<IPersonLifecycle>().EndMembershipAsync(person, MembershipEndReason.TenantDeleted, ct), cancellationToken);
            }

            await organisations.MarkTenantDeletedAsync(tenant, cancellationToken);
            logger.LogInformation("Gekündigter Tenant nach Ablauf der Lesefrist gelöscht; {Count} Personen mit Löschfolgen", members.Count);
        }
    }

    internal static async Task<int> CleanTenantAsync(IServiceProvider sp, TenantId tenant, DateTimeOffset now, CancellationToken ct)
    {
        var db = sp.GetRequiredService<PrivacyDbContext>();
        var tx = await sp.GetRequiredService<IContextTransaction>().BeginAsync(ct);
        await using (tx)
        {
            var removed = 0;
            var securityCutoff = now - PrivacyRetention.SecurityLog;
            var auditCutoff = now - PrivacyRetention.AuditLog;
            var consentCutoff = now - PrivacyRetention.Consents;
            removed += await db.SecurityRecords.Where(s => s.TenantId == tenant && s.OccurredAt < securityCutoff).ExecuteDeleteAsync(ct);
            removed += await db.AuditRecords.Where(a => a.TenantId == tenant && a.OccurredAt < auditCutoff).ExecuteDeleteAsync(ct);
            removed += await db.Consents.Where(c => c.TenantId == tenant && c.OccurredAt < consentCutoff).ExecuteDeleteAsync(ct);
            await tx.CommitAsync(ct);
            return removed;
        }
    }
}
