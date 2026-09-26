using CompanyHero.Modules.Challenges.Domain;
using CompanyHero.Modules.Challenges.Infrastructure;
using CompanyHero.Modules.Organisation.Application;
using CompanyHero.Modules.Organisation.Domain;
using CompanyHero.Platform.Audit;
using CompanyHero.Platform.Data;
using CompanyHero.Platform.Events;
using CompanyHero.Platform.Jobs;
using CompanyHero.Platform.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CompanyHero.Modules.Challenges.Application;

public sealed record ChallengeRecord(Guid Id, string Title, string? Description, ChallengeMetric Metric, decimal Target, ChallengeState State, DateTimeOffset StartsAt, DateTimeOffset EndsAt, ChallengeVisibility Visibility, string? TemplateKey, bool Previewed);

/// <summary>Kollektivstand ohne Personenbezug (Challenges 2.3); <c>null</c>, solange kein Job ihn berechnet hat.</summary>
public sealed record CollectiveRecord(decimal Total, int ContributionCount, int ContributorCount, DateTimeOffset UpdatedAt);

/// <summary>Daten der Challenge-Karte einer Person (Marke 2.5 Fortschritt): Challenge, Kollektivstand mit Prozent und Altersangabe, eigener Beitrag heute.</summary>
public sealed record ChallengeCardRecord(ChallengeRecord Challenge, CollectiveRecord? Collective, int Percent, bool ContributedToday);

/// <summary>Öffentliche Anwendungsfunktionen der Challenges (Domänenkarte 2); Tenant-Kontext.</summary>
public interface IChallengeCatalog
{
    /// <summary>Programm-Manager oder Tenant-Admin (Challenges 4.2): laufende Challenge mit Sammelziel für Bestände und Tests.</summary>
    Task<Guid> StartRunningAsync(string title, ChallengeMetric metric, decimal target, DateTimeOffset startsAt, DateTimeOffset endsAt, CancellationToken cancellationToken);

    /// <summary>Wizard (Challenges 4.1, 4.2): Entwurf aus den Achsen; nur Programm-Manager oder Tenant-Admin.</summary>
    Task<Guid> CreateDraftAsync(ChallengeDraft draft, CancellationToken cancellationToken);

    /// <summary>Kickoff-Challenge (Challenges 4.3): vorbelegter Entwurf beim Einrichten des Tenants.</summary>
    Task<Guid> CreateKickoffDraftAsync(CancellationToken cancellationToken);

    /// <summary>Vorschau des Entwurfs als Karte; die Vorschau ist Pflicht vor „Planen“ (Challenges 4.1).</summary>
    Task<ChallengeCardRecord?> PreviewAsync(Guid challengeId, CancellationToken cancellationToken);

    Task<bool> PlanAsync(Guid challengeId, CancellationToken cancellationToken);

    /// <summary>Vorzeitiges Ende durch den Programm-Manager mit Begründung im Protokoll (Challenges 4.1, 4.2).</summary>
    Task<bool> EndEarlyAsync(Guid challengeId, string reason, CancellationToken cancellationToken);

    Task<bool> UpdateTextsAsync(Guid challengeId, string title, string? description, CancellationToken cancellationToken);

    Task<ChallengeRecord?> GetAsync(Guid challengeId, CancellationToken cancellationToken);

    Task<CollectiveRecord?> GetCollectiveAsync(Guid challengeId, CancellationToken cancellationToken);

    /// <summary>Sichtbare Challenges des Tenants (geplant, laufend, Nachfrist) mit Kartendaten für die angemeldete Person; keine Werte anderer Personen. Ohne Person (Kiosk-Gerätesitzung) nur der Kollektivstand.</summary>
    Task<IReadOnlyList<ChallengeCardRecord>> ListRunningAsync(CancellationToken cancellationToken);

    /// <summary>Verwaltung (Programm-Manager, Tenant-Admin, Einsichtsrolle): alle Challenges einschließlich Entwürfen und Beendeten.</summary>
    Task<IReadOnlyList<ChallengeCardRecord>> ListAllAsync(CancellationToken cancellationToken);
}

/// <summary>Fachereignisse des Moduls für Abonnenten (Backend 6.4): Lesen über die Schnittstelle, nie über die Tabelle.</summary>
public interface IChallengeEvents
{
    Task<ContributionRecordedPayload?> GetContributionRecordedAsync(Guid eventId, CancellationToken cancellationToken);
}

internal sealed class ChallengeCatalog(ChallengesDbContext db, IContextTransaction transaction, ITenantContextAccessor context, ITenantTimeZone timeZone, IOrganisationDirectory organisations, IAuditLog audit, TimeProvider clock) : IChallengeCatalog
{
    public async Task<Guid> StartRunningAsync(string title, ChallengeMetric metric, decimal target, DateTimeOffset startsAt, DateTimeOffset endsAt, CancellationToken cancellationToken)
    {
        var tenantId = RequireCreator();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var challenge = Challenge.StartRunning(tenantId, title, metric, target, startsAt, endsAt, clock.GetUtcNow());
        db.Challenges.Add(challenge);
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return challenge.Id;
    }

    public async Task<Guid> CreateDraftAsync(ChallengeDraft draft, CancellationToken cancellationToken)
    {
        var tenantId = RequireCreator();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var challenge = Challenge.CreateDraft(tenantId, draft, context.Require().RequirePerson(), clock.GetUtcNow());
        db.Challenges.Add(challenge);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditEntry("challenges.draft.created", challenge.Id.ToString("D"), challenge.TemplateKey), cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return challenge.Id;
    }

    public async Task<Guid> CreateKickoffDraftAsync(CancellationToken cancellationToken)
    {
        RequireCreator();
        var zone = await timeZone.GetAsync(cancellationToken);
        var now = clock.GetUtcNow();
        var headcount = await organisations.GetHeadcountAtAsync(null, TenantTimeZone.DayOf(now, zone), cancellationToken) ?? 0;
        return await CreateDraftAsync(Challenge.KickoffDraft(now, zone, headcount), cancellationToken);
    }

    public async Task<ChallengeCardRecord?> PreviewAsync(Guid challengeId, CancellationToken cancellationToken)
    {
        var tenantId = RequireCreator();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var challenge = await db.Challenges.SingleOrDefaultAsync(c => c.TenantId == tenantId && c.Id == challengeId, cancellationToken);
        if (challenge is null)
        {
            await tx.CommitAsync(cancellationToken);
            return null;
        }

        if (challenge.State == ChallengeState.Draft)
        {
            challenge.MarkPreviewed(clock.GetUtcNow());
            await db.SaveChangesAsync(cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);
        return new ChallengeCardRecord(ToRecord(challenge), null, 0, false);
    }

    public async Task<bool> PlanAsync(Guid challengeId, CancellationToken cancellationToken)
    {
        var tenantId = RequireCreator();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var challenge = await db.Challenges.SingleOrDefaultAsync(c => c.TenantId == tenantId && c.Id == challengeId, cancellationToken);
        if (challenge is null)
        {
            await tx.CommitAsync(cancellationToken);
            return false;
        }

        challenge.Plan(clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditEntry("challenges.planned", challenge.Id.ToString("D"), null), cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<bool> EndEarlyAsync(Guid challengeId, string reason, CancellationToken cancellationToken)
    {
        var current = context.Require();
        if (!current.HasRole(Role.ProgrammeManager))
        {
            throw new UnauthorizedAccessException("Laufende Challenges beendet der Programm-Manager (Challenges 4.2).");
        }

        var tenantId = current.RequireTenant();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var challenge = await db.Challenges.SingleOrDefaultAsync(c => c.TenantId == tenantId && c.Id == challengeId, cancellationToken);
        if (challenge is null)
        {
            await tx.CommitAsync(cancellationToken);
            return false;
        }

        challenge.EndEarly(reason, clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditEntry("challenges.ended_early", challenge.Id.ToString("D"), challenge.EndedEarlyReason), cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<bool> UpdateTextsAsync(Guid challengeId, string title, string? description, CancellationToken cancellationToken)
    {
        var tenantId = RequireCreator();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var challenge = await db.Challenges.SingleOrDefaultAsync(c => c.TenantId == tenantId && c.Id == challengeId, cancellationToken);
        if (challenge is null)
        {
            await tx.CommitAsync(cancellationToken);
            return false;
        }

        challenge.UpdateTexts(title, description);
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<ChallengeRecord?> GetAsync(Guid challengeId, CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var challenge = await db.Challenges.AsNoTracking().SingleOrDefaultAsync(c => c.TenantId == tenantId && c.Id == challengeId, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return challenge is null || !IsVisibleTo(challenge, context.Require()) ? null : ToRecord(challenge);
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

    public Task<IReadOnlyList<ChallengeCardRecord>> ListRunningAsync(CancellationToken cancellationToken) =>
        ListAsync([ChallengeState.Planned, ChallengeState.Running, ChallengeState.Grace], cancellationToken);

    public Task<IReadOnlyList<ChallengeCardRecord>> ListAllAsync(CancellationToken cancellationToken)
    {
        var current = context.Require();
        if (!current.HasRole(Role.ProgrammeManager) && !current.HasRole(Role.TenantAdmin) && !current.HasRole(Role.Insight))
        {
            throw new UnauthorizedAccessException("Alle Challenges sehen Programm-Manager, Tenant-Admin und Einsichtsrolle (Organisation 4.2).");
        }

        return ListAsync(Enum.GetValues<ChallengeState>(), cancellationToken);
    }

    private async Task<IReadOnlyList<ChallengeCardRecord>> ListAsync(IReadOnlyList<ChallengeState> states, CancellationToken cancellationToken)
    {
        var current = context.Require();
        var tenantId = current.RequireTenant();
        var personId = current.PersonId;
        var zone = await timeZone.GetAsync(cancellationToken);
        var today = TenantTimeZone.DayOf(clock.GetUtcNow(), zone);
        var from = TenantTimeZone.StartOfDay(today, zone);
        var to = TenantTimeZone.StartOfDay(today.AddDays(1), zone);

        await using var tx = await transaction.BeginAsync(cancellationToken);
        var challenges = await db.Challenges.AsNoTracking()
            .Where(c => c.TenantId == tenantId && states.Contains(c.State))
            .OrderBy(c => c.State).ThenBy(c => c.EndsAt).ThenBy(c => c.Id)
            .ToListAsync(cancellationToken);
        var ids = challenges.Select(c => c.Id).ToList();
        var collectives = await db.CollectiveStates.AsNoTracking()
            .Where(s => s.TenantId == tenantId && ids.Contains(s.ChallengeId))
            .ToDictionaryAsync(s => s.ChallengeId, cancellationToken);
        var contributedToday = personId is null
            ? []
            : await db.Contributions.AsNoTracking()
                .Where(c => c.TenantId == tenantId && c.PersonId == personId && ids.Contains(c.ChallengeId) && c.RecordedAt >= from && c.RecordedAt < to && !c.Reversed && c.ReversalOf == null)
                .Select(c => c.ChallengeId)
                .Distinct()
                .ToListAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        return challenges.Where(c => IsVisibleTo(c, current)).Select(c =>
        {
            var collective = collectives.TryGetValue(c.Id, out var s) ? new CollectiveRecord(s.Total, s.ContributionCount, s.ContributorCount, s.UpdatedAt) : null;
            return new ChallengeCardRecord(ToRecord(c), collective, c.PercentOf(collective?.Total ?? 0m), contributedToday.Contains(c.Id));
        }).ToList();
    }

    /// <summary>Sichtbarkeit der Challenge (Achse 7): „Nur ich“ sieht nur der Ersteller; Entwürfe nur Programm-Manager und Tenant-Admin.</summary>
    private static bool IsVisibleTo(Challenge challenge, TenantContext current)
    {
        if (challenge.State == ChallengeState.Draft && !current.HasRole(Role.ProgrammeManager) && !current.HasRole(Role.TenantAdmin) && !current.HasRole(Role.Insight))
        {
            return false;
        }

        return challenge.Visibility != ChallengeVisibility.OnlyMe || challenge.CreatedBy == current.PersonId;
    }

    private static ChallengeRecord ToRecord(Challenge c) =>
        new(c.Id, c.Title, c.Description, c.Metric, c.Target, c.State, c.StartsAt, c.EndsAt, c.Visibility, c.TemplateKey, c.PreviewedAt is not null);

    private TenantId RequireCreator()
    {
        var current = context.Require();
        if (!current.HasRole(Role.ProgrammeManager) && !current.HasRole(Role.TenantAdmin))
        {
            throw new UnauthorizedAccessException("Challenges legen Programm-Manager oder Tenant-Admin an (Challenges 4.2, Stufe 6).");
        }

        return current.RequireTenant();
    }
}

internal sealed class ChallengeEvents(ChallengesDbContext db, IContextTransaction transaction, ITenantContextAccessor context) : IChallengeEvents, IDomainEventSource
{
    public string TypePrefix => ChallengeEventTypes.Prefix;

    public async Task<ContributionRecordedPayload?> GetContributionRecordedAsync(Guid eventId, CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var domainEvent = await db.Events.SingleOrDefaultAsync(e => e.TenantId == tenantId && e.Id == eventId && e.Type == ChallengeEventTypes.ContributionRecorded, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return domainEvent?.ReadContributionRecorded();
    }

    public async Task<DomainEventRecord?> ReadAsync(Guid eventId, CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var e = await db.Events.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == eventId, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return e is null ? null : new DomainEventRecord(e.Id, e.Type, e.Version, e.OccurredAt, e.Payload, e.CausedBy);
    }
}

/// <summary>
/// Folgejob eines Beitrags (Challenges 3): berechnet den Kollektivstand aus den Beiträgen neu (Gegenbuchungen eingeschlossen)
/// und meldet neu erreichte Meilensteine genau einmal (Challenges 6.2). Idempotent, weil er nichts addiert, sondern den
/// Stand aus der Quelle ableitet; mehrfache Zustellung ergibt denselben Stand (Backend 6.3).
/// </summary>
internal sealed class CollectiveRecalculateHandler(ChallengesDbContext db, IContextTransaction transaction, ITenantContextAccessor context, IDomainEventDispatcher events, TimeProvider clock) : IJobHandler
{
    public static string JobType => "challenges.collective.recalculate";

    public async Task HandleAsync(JobExecution job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        var tenantId = context.Require().RequireTenant();
        var challengeId = Guid.ParseExact(job.Reference, "D");

        await using var tx = await transaction.BeginAsync(cancellationToken);
        var challenge = await db.Challenges.AsNoTracking().SingleOrDefaultAsync(c => c.TenantId == tenantId && c.Id == challengeId, cancellationToken)
            ?? throw new InvalidOperationException("Challenge des Jobs im Kontext des Tenants nicht gefunden.");

        var anonymous = Contribution.AnonymousPerson;
        var rows = await db.Contributions.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.ChallengeId == challengeId)
            .Select(c => new { c.PersonId, c.Value, c.Reversed, c.ReversalOf })
            .ToListAsync(cancellationToken);
        var total = rows.Sum(c => c.Value);
        var effective = rows.Where(c => !c.Reversed && c.ReversalOf == null).ToList();
        var contributors = effective.Select(c => c.PersonId).Where(p => p != anonymous).Distinct().Count();

        var state = await db.CollectiveStates.SingleOrDefaultAsync(s => s.TenantId == tenantId && s.ChallengeId == challengeId, cancellationToken);
        if (state is null)
        {
            state = CollectiveState.For(tenantId, challengeId);
            db.CollectiveStates.Add(state);
        }

        var now = clock.GetUtcNow();
        state.Set(total, effective.Count, contributors, now);
        foreach (var milestone in state.AdvanceMilestones(challenge.PercentOf(total)))
        {
            var milestoneEvent = ChallengeEvent.Create(tenantId, ChallengeEventTypes.MilestoneReached, new MilestonePayload(challengeId, challenge.Title, milestone), now, null);
            db.Events.Add(milestoneEvent);
            await db.SaveChangesAsync(cancellationToken);
            await events.DispatchAsync(ChallengeEventTypes.MilestoneReached, milestoneEvent.Id, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }
}

/// <summary>
/// Zeitgesteuerter Lebenszyklus (Challenges 4.1, A-040): Geplant → Laufend zum Start, Laufend → Nachfrist zum Ende,
/// Nachfrist → Beendet nach 48 Stunden, Beendet → Archiviert nach 12 Monaten; Start und Ende als Fachereignisse für Feed
/// und Benachrichtigungen. Plattformkontext für die Tenantliste, Übergänge je Tenant in dessen Kontext.
/// </summary>
internal sealed class ChallengeLifecycleTask(ITenantScopeFactory scopes, IOrganisationDirectory organisations, TimeProvider clock, ILogger<ChallengeLifecycleTask> logger) : IScheduledTask
{
    public static string Name => "challenges.lifecycle";

    public static TimeSpan Interval => TimeSpan.FromMinutes(1);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        foreach (var tenant in await organisations.ListActiveTenantsAsync(cancellationToken))
        {
            var transitions = await scopes.RunAsync(TenantContext.ForTenant(tenant), (sp, ct) => AdvanceTenantAsync(sp, tenant, now, ct), cancellationToken);
            if (transitions > 0)
            {
                logger.LogInformation("Challenge-Lebenszyklus: {Count} Übergänge", transitions);
            }
        }
    }

    internal static async Task<int> AdvanceTenantAsync(IServiceProvider sp, TenantId tenant, DateTimeOffset now, CancellationToken ct)
    {
        var db = sp.GetRequiredService<ChallengesDbContext>();
        var events = sp.GetRequiredService<IDomainEventDispatcher>();
        var tx = await sp.GetRequiredService<IContextTransaction>().BeginAsync(ct);
        await using (tx)
        {
            var transitions = 0;
            var open = await db.Challenges.Where(c => c.TenantId == tenant && c.State != ChallengeState.Draft && c.State != ChallengeState.Archived).ToListAsync(ct);
            var ids = open.Select(c => c.Id).ToList();
            var totals = await db.CollectiveStates.AsNoTracking().Where(s => s.TenantId == tenant && ids.Contains(s.ChallengeId)).ToDictionaryAsync(s => s.ChallengeId, s => s.Total, ct);
            foreach (var challenge in open)
            {
                var percent = challenge.PercentOf(totals.GetValueOrDefault(challenge.Id));
                if (challenge.StartIfDue(now))
                {
                    transitions++;
                    await PublishAsync(db, events, tenant, ChallengeEventTypes.Started, new ChallengeStatePayload(challenge.Id, challenge.Title, percent), now, ct);
                }

                if (challenge.EnterGraceIfDue(now))
                {
                    transitions++;
                }

                if (challenge.EndIfDue(now))
                {
                    transitions++;
                    await PublishAsync(db, events, tenant, ChallengeEventTypes.Ended, new ChallengeStatePayload(challenge.Id, challenge.Title, percent), now, ct);
                }

                if (challenge.ArchiveIfDue(now))
                {
                    transitions++;
                }
            }

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return transitions;
        }
    }

    private static async Task PublishAsync<TPayload>(ChallengesDbContext db, IDomainEventDispatcher events, TenantId tenant, string type, TPayload payload, DateTimeOffset now, CancellationToken ct)
    {
        var domainEvent = ChallengeEvent.Create(tenant, type, payload, now, null);
        db.Events.Add(domainEvent);
        await db.SaveChangesAsync(ct);
        await events.DispatchAsync(type, domainEvent.Id, ct);
    }
}
