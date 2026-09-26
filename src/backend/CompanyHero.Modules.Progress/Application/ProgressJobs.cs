using System.Text.Json;
using System.Text.Json.Nodes;
using CompanyHero.Modules.Organisation.Application;
using CompanyHero.Modules.Progress.Domain;
using CompanyHero.Modules.Progress.Infrastructure;
using CompanyHero.Platform.Data;
using CompanyHero.Platform.Events;
using CompanyHero.Platform.Jobs;
using CompanyHero.Platform.Privacy;
using CompanyHero.Platform.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace CompanyHero.Modules.Progress.Application;

/// <summary>Ereignisquelle der Domäne Fortschritt für Abonnenten (Backend 6.4).</summary>
internal sealed class ProgressEventSource(ProgressDbContext db, IContextTransaction transaction, ITenantContextAccessor context) : IDomainEventSource
{
    public string TypePrefix => ProgressEventTypes.Prefix;

    public async Task<DomainEventRecord?> ReadAsync(Guid eventId, CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var e = await db.Events.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == eventId, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return e is null ? null : new DomainEventRecord(e.Id, e.Type, e.Version, e.OccurredAt, e.Payload, e.CausedBy);
    }
}

/// <summary>Verleiht Abzeichen nach dem Katalog; idempotent, weil verdiente Abzeichen nur einmal entstehen (Fortschritt 4.2).</summary>
internal sealed class BadgeEvaluator(ProgressDbContext db, IContextTransaction transaction, ITenantContextAccessor context, IOrganisationDirectory organisations, IDomainEventDispatcher events, TimeProvider clock)
{
    public async Task<IReadOnlyList<string>> EvaluateAsync(PersonId personId, bool collectiveGoalReached, CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        var groups = await organisations.GetGroupsOfAsync(personId, cancellationToken);
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var progress = await db.PersonProgress.AsNoTracking().SingleOrDefaultAsync(p => p.TenantId == tenantId && p.PersonId == personId, cancellationToken);
        var counted = await db.ActivityEvents.Where(e => e.TenantId == tenantId && e.PersonId == personId && e.Points > 0 && !e.Reversed).ToListAsync(cancellationToken);
        var earned = await db.Badges.Where(b => b.TenantId == tenantId && b.PersonId == personId).Select(b => b.BadgeKey).ToListAsync(cancellationToken);
        var earnedSet = earned.ToHashSet(StringComparer.Ordinal);
        var hadCollectiveGoal = earnedSet.Contains(BadgeCatalog.Firmenziel) || collectiveGoalReached;

        var snapshot = new BadgeProgressSnapshot(
            counted.Count,
            progress?.CurrentStreak ?? 0,
            counted.Select(e => e.Kind).Distinct(StringComparer.Ordinal).Count(),
            groups.Any(g => g.GroupId is not null),
            hadCollectiveGoal,
            counted.Count(e => e.Kind == ActivityKinds.RecognitionGiven));

        var now = clock.GetUtcNow();
        var awarded = new List<string>();
        foreach (var key in BadgeCatalog.Earned(snapshot).Where(k => !earnedSet.Contains(k)))
        {
            var award = BadgeAward.Grant(tenantId, personId, key, now);
            db.Badges.Add(award);
            var definition = BadgeCatalog.Get(key);
            var badgeEvent = ProgressEvent.Create(tenantId, ProgressEventTypes.BadgeAwarded, new BadgeAwardedPayload(personId.Value, key, definition.TextKey, definition.Category.ToString()), now, personId);
            db.Events.Add(badgeEvent);
            await db.SaveChangesAsync(cancellationToken);
            await events.DispatchAsync(ProgressEventTypes.BadgeAwarded, badgeEvent.Id, cancellationToken);
            awarded.Add(key);
        }

        await tx.CommitAsync(cancellationToken);
        return awarded;
    }
}

/// <summary>Job nach jeder Handlung (Fortschritt 4.2): Abzeichen prüfen; Referenz ist die Person, Schlüssel die Handlung.</summary>
internal sealed class BadgeEvaluationHandler(BadgeEvaluator evaluator) : IJobHandler
{
    public static string JobType => ProgressConstants.BadgeJobType;

    public Task HandleAsync(JobExecution job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        return evaluator.EvaluateAsync(new PersonId(Guid.ParseExact(job.Reference, "D")), false, cancellationToken);
    }
}

/// <summary>
/// Abonnent des Meilenstein-Ereignisses der Challenges (Fortschritt 7: Kollektivereignisse lösen Abzeichen aus): bei 100 Prozent
/// erhält jedes aktive Mitglied „Firmenziel erreicht“ (Fortschritt 4.1 „Gemeinsam“). Gelesen über die Ereignisquelle, ohne Tabellen
/// des anderen Moduls; der Ereignistyp ist der veröffentlichte Vertrag <c>challenges.milestone.reached</c> (Version 1: challengeId, percent).
/// </summary>
internal sealed class CollectiveGoalHandler(IDomainEventReader reader, IOrganisationDirectory organisations, BadgeEvaluator evaluator) : IJobHandler
{
    public const string SubscribedEventType = "challenges.milestone.reached";

    public static string JobType => "progress.collective_goal";

    public async Task HandleAsync(JobExecution job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        var record = await reader.ReadAsync(SubscribedEventType, Guid.ParseExact(job.Reference, "D"), cancellationToken)
            ?? throw new InvalidOperationException("Ereignis im Kontext des Tenants nicht lesbar.");
        var payload = JsonNode.Parse(record.PayloadJson)!;
        if ((payload["percent"]?.GetValue<int>() ?? 0) < 100)
        {
            return;
        }

        foreach (var person in await organisations.ListActiveMemberIdsAsync(cancellationToken))
        {
            await evaluator.EvaluateAsync(person, collectiveGoalReached: true, cancellationToken);
        }
    }
}

/// <summary>Auskunft (Datenschutz 6.4): Ereignisse, Stand, Abzeichen, Check-ins; Löschung (5.2): alles entfernt, Abzeichen eingeschlossen.</summary>
internal sealed class ProgressPersonalData(ProgressDbContext db, IContextTransaction transaction, ITenantContextAccessor context) : IPersonalDataExporter, IPersonalDataEraser
{
    public string Section => "fortschritt";

    public async Task<JsonNode> ExportAsync(PersonId personId, CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var progress = await db.PersonProgress.AsNoTracking().SingleOrDefaultAsync(p => p.TenantId == tenantId && p.PersonId == personId, cancellationToken);
        var activities = await db.ActivityEvents.AsNoTracking().Where(e => e.TenantId == tenantId && e.PersonId == personId).OrderBy(e => e.OccurredAt).ToListAsync(cancellationToken);
        var badges = await db.Badges.AsNoTracking().Where(b => b.TenantId == tenantId && b.PersonId == personId).OrderBy(b => b.AwardedAt).ToListAsync(cancellationToken);
        var checkIns = await db.CheckIns.AsNoTracking().Where(c => c.TenantId == tenantId && c.PersonId == personId).OrderBy(c => c.Day).ToListAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return new JsonObject
        {
            ["stand"] = progress is null ? null : new JsonObject { ["punkte"] = progress.PointsBalance, ["stufe"] = progress.Level, ["serie"] = progress.CurrentStreak, ["laengsteSerie"] = progress.LongestStreak, ["tagesziel"] = progress.DailyGoal },
            ["handlungen"] = new JsonArray(activities.Select(a => (JsonNode)new JsonObject { ["art"] = a.Kind, ["quelle"] = a.Source.ToString(), ["zeitpunkt"] = a.OccurredAt, ["punkte"] = a.Points, ["gegenereignis"] = a.IsReversal }).ToArray()),
            ["abzeichen"] = new JsonArray(badges.Select(b => (JsonNode)new JsonObject { ["abzeichen"] = b.BadgeKey, ["verliehen"] = b.AwardedAt }).ToArray()),
            ["checkIns"] = new JsonArray(checkIns.Select(c => (JsonNode)new JsonObject { ["tag"] = c.Day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), ["kacheln"] = c.Tiles.ToString() }).ToArray()),
        };
    }

    public async Task EraseAsync(PersonId personId, CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        await db.CheckIns.Where(c => c.TenantId == tenantId && c.PersonId == personId).ExecuteDeleteAsync(cancellationToken);
        await db.Badges.Where(b => b.TenantId == tenantId && b.PersonId == personId).ExecuteDeleteAsync(cancellationToken);
        await db.ActivityEvents.Where(e => e.TenantId == tenantId && e.PersonId == personId).ExecuteDeleteAsync(cancellationToken);
        await db.PersonProgress.Where(p => p.TenantId == tenantId && p.PersonId == personId).ExecuteDeleteAsync(cancellationToken);
        await db.Events.Where(e => e.TenantId == tenantId && e.CausedBy == personId).ExecuteDeleteAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }
}

internal static class ProgressJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web);
}
