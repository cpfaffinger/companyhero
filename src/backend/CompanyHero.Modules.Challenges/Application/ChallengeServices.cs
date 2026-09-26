using CompanyHero.Modules.Challenges.Domain;
using CompanyHero.Modules.Challenges.Infrastructure;
using CompanyHero.Modules.Organisation.Domain;
using CompanyHero.Platform.Data;
using CompanyHero.Platform.Jobs;
using CompanyHero.Platform.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace CompanyHero.Modules.Challenges.Application;

public sealed record ChallengeRecord(Guid Id, string Title, ChallengeMetric Metric, decimal Target, ChallengeState State, DateTimeOffset StartsAt, DateTimeOffset EndsAt);

/// <summary>Kollektivstand ohne Personenbezug (Challenges 2.3); <c>null</c>, solange kein Job ihn berechnet hat.</summary>
public sealed record CollectiveRecord(decimal Total, int ContributionCount, int ContributorCount, DateTimeOffset UpdatedAt);

/// <summary>Daten der Challenge-Karte einer Person (Marke 2.5 Fortschritt): Challenge, Kollektivstand mit Prozent und Altersangabe, eigener Beitrag heute.</summary>
public sealed record ChallengeCardRecord(ChallengeRecord Challenge, CollectiveRecord? Collective, int Percent, bool ContributedToday);

/// <summary>Öffentliche Anwendungsfunktionen der Challenges (Domänenkarte 2); Tenant-Kontext.</summary>
public interface IChallengeCatalog
{
    /// <summary>Programm-Manager oder Tenant-Admin (Challenges 4.2): laufende Challenge mit Sammelziel für den Durchstich.</summary>
    Task<Guid> StartRunningAsync(string title, ChallengeMetric metric, decimal target, DateTimeOffset startsAt, DateTimeOffset endsAt, CancellationToken cancellationToken);

    Task<ChallengeRecord?> GetAsync(Guid challengeId, CancellationToken cancellationToken);

    Task<CollectiveRecord?> GetCollectiveAsync(Guid challengeId, CancellationToken cancellationToken);

    /// <summary>Laufende Challenges des Tenants mit den Daten der Challenge-Karte für die angemeldete Person; keine Werte anderer Personen.</summary>
    Task<IReadOnlyList<ChallengeCardRecord>> ListRunningAsync(CancellationToken cancellationToken);
}

/// <summary>Fachereignisse des Moduls für Abonnenten (Backend 6.4): Lesen über die Schnittstelle, nie über die Tabelle.</summary>
public interface IChallengeEvents
{
    Task<ContributionRecordedPayload?> GetContributionRecordedAsync(Guid eventId, CancellationToken cancellationToken);
}

internal sealed class ChallengeCatalog(ChallengesDbContext db, IContextTransaction transaction, ITenantContextAccessor context, TimeProvider clock) : IChallengeCatalog
{
    public async Task<Guid> StartRunningAsync(string title, ChallengeMetric metric, decimal target, DateTimeOffset startsAt, DateTimeOffset endsAt, CancellationToken cancellationToken)
    {
        var current = context.Require();
        var tenantId = current.RequireTenant();
        if (!current.HasRole(Role.ProgrammeManager) && !current.HasRole(Role.TenantAdmin))
        {
            throw new UnauthorizedAccessException("Challenges legen Programm-Manager oder Tenant-Admin an (Challenges 4.2).");
        }

        await using var tx = await transaction.BeginAsync(cancellationToken);
        var challenge = Challenge.StartRunning(tenantId, title, metric, target, startsAt, endsAt, clock.GetUtcNow());
        db.Challenges.Add(challenge);
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return challenge.Id;
    }

    public async Task<ChallengeRecord?> GetAsync(Guid challengeId, CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var record = await db.Challenges
            .Where(c => c.TenantId == tenantId && c.Id == challengeId)
            .Select(c => new ChallengeRecord(c.Id, c.Title, c.Metric, c.Target, c.State, c.StartsAt, c.EndsAt))
            .SingleOrDefaultAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return record;
    }

    public async Task<CollectiveRecord?> GetCollectiveAsync(Guid challengeId, CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var record = await db.CollectiveStates
            .Where(s => s.TenantId == tenantId && s.ChallengeId == challengeId)
            .Select(s => new CollectiveRecord(s.Total, s.ContributionCount, s.ContributorCount, s.UpdatedAt))
            .SingleOrDefaultAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return record;
    }

    public async Task<IReadOnlyList<ChallengeCardRecord>> ListRunningAsync(CancellationToken cancellationToken)
    {
        var current = context.Require();
        var tenantId = current.RequireTenant();
        var personId = current.RequirePerson();
        var today = TenantTimeZone.DayOf(clock.GetUtcNow());
        var from = TenantTimeZone.StartOfDay(today);
        var to = TenantTimeZone.StartOfDay(today.AddDays(1));

        await using var tx = await transaction.BeginAsync(cancellationToken);
        var challenges = await db.Challenges
            .Where(c => c.TenantId == tenantId && c.State == ChallengeState.Running)
            .OrderBy(c => c.EndsAt).ThenBy(c => c.Id)
            .ToListAsync(cancellationToken);
        var ids = challenges.Select(c => c.Id).ToList();
        var collectives = await db.CollectiveStates
            .Where(s => s.TenantId == tenantId && ids.Contains(s.ChallengeId))
            .ToDictionaryAsync(s => s.ChallengeId, cancellationToken);
        var contributedToday = await db.Contributions
            .Where(c => c.TenantId == tenantId && c.PersonId == personId && ids.Contains(c.ChallengeId) && c.RecordedAt >= from && c.RecordedAt < to)
            .Select(c => c.ChallengeId)
            .Distinct()
            .ToListAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        return challenges.Select(c =>
        {
            var collective = collectives.TryGetValue(c.Id, out var s) ? new CollectiveRecord(s.Total, s.ContributionCount, s.ContributorCount, s.UpdatedAt) : null;
            return new ChallengeCardRecord(
                new ChallengeRecord(c.Id, c.Title, c.Metric, c.Target, c.State, c.StartsAt, c.EndsAt),
                collective,
                c.PercentOf(collective?.Total ?? 0m),
                contributedToday.Contains(c.Id));
        }).ToList();
    }
}

internal sealed class ChallengeEvents(ChallengesDbContext db, IContextTransaction transaction, ITenantContextAccessor context) : IChallengeEvents
{
    public async Task<ContributionRecordedPayload?> GetContributionRecordedAsync(Guid eventId, CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var domainEvent = await db.Events.SingleOrDefaultAsync(e => e.TenantId == tenantId && e.Id == eventId && e.Type == ChallengeEventTypes.ContributionRecorded, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return domainEvent?.ReadContributionRecorded();
    }
}

/// <summary>
/// Folgejob eines Beitrags (Challenges 3): berechnet den Kollektivstand aus den Beiträgen neu. Idempotent, weil er nichts
/// addiert, sondern den Stand aus der Quelle ableitet; mehrfache Zustellung ergibt denselben Stand (Backend 6.3).
/// </summary>
internal sealed class CollectiveRecalculateHandler(ChallengesDbContext db, IContextTransaction transaction, ITenantContextAccessor context, TimeProvider clock) : IJobHandler
{
    public static string JobType => "challenges.collective.recalculate";

    public async Task HandleAsync(JobExecution job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        var tenantId = context.Require().RequireTenant();
        var challengeId = Guid.ParseExact(job.Reference, "D");

        await using var tx = await transaction.BeginAsync(cancellationToken);
        var exists = await db.Challenges.AnyAsync(c => c.TenantId == tenantId && c.Id == challengeId, cancellationToken);
        if (!exists)
        {
            throw new InvalidOperationException("Challenge des Jobs im Kontext des Tenants nicht gefunden.");
        }

        var aggregate = await db.Contributions
            .Where(c => c.TenantId == tenantId && c.ChallengeId == challengeId)
            .GroupBy(c => c.ChallengeId)
            .Select(g => new { Total = g.Sum(c => c.Value), Count = g.Count(), Contributors = g.Select(c => c.PersonId).Distinct().Count() })
            .SingleOrDefaultAsync(cancellationToken);

        var state = await db.CollectiveStates.SingleOrDefaultAsync(s => s.TenantId == tenantId && s.ChallengeId == challengeId, cancellationToken);
        if (state is null)
        {
            state = CollectiveState.For(tenantId, challengeId);
            db.CollectiveStates.Add(state);
        }

        state.Set(aggregate?.Total ?? 0m, aggregate?.Count ?? 0, aggregate?.Contributors ?? 0, clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }
}
