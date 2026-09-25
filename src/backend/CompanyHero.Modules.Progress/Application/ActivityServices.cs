using CompanyHero.Modules.Privacy.Application;
using CompanyHero.Modules.Progress.Domain;
using CompanyHero.Modules.Progress.Infrastructure;
using CompanyHero.Platform.Data;
using CompanyHero.Platform.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace CompanyHero.Modules.Progress.Application;

public sealed record ActivityRecord(Guid Id, string Kind, ActivitySource Source, DateTimeOffset OccurredAt);

/// <summary>Erfasst genau ein Aktivitätsereignis je Handlung (Fortschritt 2.1).</summary>
public interface IActivityRecorder
{
    Task<Guid> RecordAsync(PersonId personId, string kind, ActivitySource source, DateTimeOffset occurredAt, CancellationToken cancellationToken);
}

/// <summary>
/// Persönlicher Fortschritt einer Person (Domänenkarte 2) hinter der zentralen Sichtbarkeitsregel. Ergebnis <c>null</c>
/// bedeutet: für die lesende Person existiert dieser Fortschritt nicht (Datenschutz 7: „nicht gefunden“, nicht „verboten“).
/// </summary>
public interface IPersonalActivityQuery
{
    Task<IReadOnlyList<ActivityRecord>?> GetForPersonAsync(PersonId subject, CancellationToken cancellationToken);
}

internal sealed class ActivityRecorder(ProgressDbContext db, IContextTransaction transaction, ITenantContextAccessor context) : IActivityRecorder
{
    public async Task<Guid> RecordAsync(PersonId personId, string kind, ActivitySource source, DateTimeOffset occurredAt, CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var activity = ActivityEvent.Record(tenantId, personId, kind, source, occurredAt);
        db.ActivityEvents.Add(activity);
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return activity.Id;
    }
}

internal sealed class PersonalActivityQuery(ProgressDbContext db, IContextTransaction transaction, ITenantContextAccessor context, IVisibilityRule visibility) : IPersonalActivityQuery
{
    public async Task<IReadOnlyList<ActivityRecord>?> GetForPersonAsync(PersonId subject, CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        if (!await visibility.MayReadIndividualValuesAsync(subject, cancellationToken))
        {
            return null;
        }

        await using var tx = await transaction.BeginAsync(cancellationToken);
        return await db.ActivityEvents
            .Where(e => e.TenantId == tenantId && e.PersonId == subject)
            .OrderBy(e => e.OccurredAt)
            .Select(e => new ActivityRecord(e.Id, e.Kind, e.Source, e.OccurredAt))
            .ToListAsync(cancellationToken);
    }
}
