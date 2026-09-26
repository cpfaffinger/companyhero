using CompanyHero.Modules.Organisation.Application;
using CompanyHero.Modules.Organisation.Domain;
using CompanyHero.Modules.Privacy.Application;
using CompanyHero.Modules.Privacy.Domain;
using CompanyHero.Modules.Progress.Domain;
using CompanyHero.Modules.Progress.Infrastructure;
using CompanyHero.Platform.Data;
using CompanyHero.Platform.Events;
using CompanyHero.Platform.Jobs;
using CompanyHero.Platform.Metering;
using CompanyHero.Platform.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace CompanyHero.Modules.Progress.Application;

public sealed record ActivityRecord(Guid Id, string Kind, ActivitySource Source, DateTimeOffset OccurredAt, int Points, bool Reversed);

/// <summary>Erfasst genau ein Aktivitätsereignis je Handlung (Fortschritt 2.1) und nimmt es bei Korrektur als Gegenereignis zurück.</summary>
public interface IActivityRecorder
{
    /// <summary>Wertet die Handlung (Punkte nach Deckel, Serie, Stufe), reiht die Abzeichenprüfung ein und meldet den aktiven Tag an Metering; alles in der laufenden Kontexttransaktion.</summary>
    Task<Guid> RecordAsync(PersonId personId, string kind, ActivitySource source, DateTimeOffset occurredAt, CancellationToken cancellationToken);

    /// <summary>Gegenereignis (Fortschritt 2.1): Punkte zurück bis null, Serie neu berechnet; Stufe und Abzeichen bleiben.</summary>
    Task ReverseAsync(Guid activityId, CancellationToken cancellationToken);
}

/// <summary>
/// Persönlicher Fortschritt einer Person (Domänenkarte 2) hinter der zentralen Sichtbarkeitsregel. Ergebnis <c>null</c>
/// bedeutet: für die lesende Person existiert dieser Fortschritt nicht (Datenschutz 7: „nicht gefunden“, nicht „verboten“).
/// </summary>
public interface IPersonalActivityQuery
{
    Task<IReadOnlyList<ActivityRecord>?> GetForPersonAsync(PersonId subject, CancellationToken cancellationToken);
}

public sealed record BadgeRecord(string Key, BadgeCategory Category, string TextKey, DateTimeOffset? AwardedAt);

/// <summary>Persönliche Anzeigen (Fortschritt 6.2): nur für die Person selbst; keine Nullwerte, keine Vergleiche.</summary>
public sealed record PersonalProgress(int PointsBalance, int Level, int CurrentStreak, int LongestStreak, int DailyGoal, int TodayActions, bool CheckedInToday, IReadOnlyList<BadgeRecord> Earned, IReadOnlyList<BadgeRecord> Next);

public interface IProgressQuery
{
    /// <summary>Stand der angemeldeten Person; das erste Abzeichen entsteht spätestens hier, damit niemand mit leerem Profil beginnt (Fortschritt 4.2).</summary>
    Task<PersonalProgress> GetMineAsync(CancellationToken cancellationToken);

    Task SetDailyGoalAsync(int goal, CancellationToken cancellationToken);

    Task<bool> CheckedInTodayAsync(PersonId personId, CancellationToken cancellationToken);
}

public enum CheckInOutcome
{
    Recorded = 1,
    AlreadyToday = 2,
}

/// <summary>Täglicher Check-in (Fortschritt 6.1): einmal je Tag, auch am Kiosk; erzeugt eine Handlung und ein Feed-Ereignis nach Sichtbarkeit.</summary>
public interface ICheckInService
{
    Task<CheckInOutcome> CheckInAsync(CheckInTiles tiles, CancellationToken cancellationToken);
}

/// <summary>Beteiligung (Fortschritt 8, Datenschutz 4.1): nur ab fünf Personen mit Handlung; darunter nichts.</summary>
public sealed record ParticipationAggregate(bool Available, int? ActivePersons, int? Headcount, int? Percent);

public interface IProgressAggregates
{
    /// <summary>Aktive Personen (gewertete Handlung mit Quelle selbst oder Plattform) im Zeitraum, tenantweit oder je Gruppe (Datenschutz 4.2).</summary>
    Task<ParticipationAggregate> ParticipationAsync(Guid? groupId, string period, CancellationToken cancellationToken);
}

internal static class ProgressConstants
{
    public const string MeteringModule = "Kern";
    public const string ActiveDayMetric = "member.active_day";
    public const string ActiveMonthMetric = "member.active_month";
    public const string BadgeJobType = "progress.badges.evaluate";
}

internal sealed class ActivityRecorder(
    ProgressDbContext db,
    IContextTransaction transaction,
    ITenantContextAccessor context,
    ITenantTimeZone timeZone,
    IMeteringEmitter metering,
    IMeteringSlots slots,
    IJobQueue jobs,
    IDomainEventDispatcher events,
    TimeProvider clock) : IActivityRecorder
{
    public async Task<Guid> RecordAsync(PersonId personId, string kind, ActivitySource source, DateTimeOffset occurredAt, CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        var zone = await timeZone.GetAsync(cancellationToken);
        var day = TenantTimeZone.DayOf(occurredAt, zone);
        await using var tx = await transaction.BeginAsync(cancellationToken);

        var countedTotal = await db.ActivityEvents.CountAsync(e => e.TenantId == tenantId && e.PersonId == personId && e.Day == day && e.Points > 0 && !e.Reversed, cancellationToken);
        var countedOfKind = await db.ActivityEvents.CountAsync(e => e.TenantId == tenantId && e.PersonId == personId && e.Day == day && e.Kind == kind && e.Points > 0 && !e.Reversed, cancellationToken);
        var points = PointsRules.PointsFor(kind, countedTotal, countedOfKind);

        var activity = ActivityEvent.Record(tenantId, personId, kind, source, occurredAt, day, points);
        db.ActivityEvents.Add(activity);

        var progress = await db.PersonProgress.SingleOrDefaultAsync(p => p.TenantId == tenantId && p.PersonId == personId, cancellationToken);
        if (progress is null)
        {
            progress = PersonProgress.Start(tenantId, personId);
            db.PersonProgress.Add(progress);
        }

        var reachedLevel = progress.Apply(points, day);
        await db.SaveChangesAsync(cancellationToken);

        if (reachedLevel is { } level)
        {
            var levelEvent = ProgressEvent.Create(tenantId, ProgressEventTypes.LevelReached, new LevelReachedPayload(personId.Value, level), clock.GetUtcNow(), personId);
            db.Events.Add(levelEvent);
            await db.SaveChangesAsync(cancellationToken);
            await events.DispatchAsync(ProgressEventTypes.LevelReached, levelEvent.Id, cancellationToken);
        }

        // „Aktives Mitglied“ (Fortschritt 2.3, A-071): einmal je Person und Tag beziehungsweise Monat bei der ersten gewerteten Handlung
        // mit Quelle selbst oder Plattform; automatische Tageswerte und Kommentare nie. Bezug und Schlüssel sind der periodengesalzene
        // Zähler-Slot von Metering (Metering 2.3): innerhalb der Periode zählbar, über Perioden nicht verkettbar, ohne Salz nicht rückrechenbar.
        // Die Deduplizierung je Tag und Monat leistet der Idempotenzschlüssel des Ledgers, nicht der Zähler der Punkte: ein automatischer
        // Tageswert mit Punkten davor darf die erste gewertete eigene Handlung nicht verdecken.
        if (points > 0 && source != ActivitySource.Automatic)
        {
            var slot = await slots.SlotForAsync(personId, occurredAt, cancellationToken);
            await metering.EmitAsync(new MeteringEmission(ProgressConstants.MeteringModule, ProgressConstants.ActiveDayMetric, slot, 1m, MeteringSource.Self, occurredAt, $"ad:{slot}:{day:yyyy-MM-dd}"), cancellationToken);
            await metering.EmitAsync(new MeteringEmission(ProgressConstants.MeteringModule, ProgressConstants.ActiveMonthMetric, slot, 1m, MeteringSource.Self, occurredAt, $"am:{slot}"), cancellationToken);
        }

        // Abzeichen ereignisgetrieben durch Jobs nach jeder Handlung (Fortschritt 4.2), dedupliziert je Handlung.
        await jobs.EnqueueAsync(new JobRequest(ProgressConstants.BadgeJobType, personId.ToString(), $"activity:{activity.Id:D}"), cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return activity.Id;
    }

    public async Task ReverseAsync(Guid activityId, CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var activity = await db.ActivityEvents.SingleOrDefaultAsync(e => e.TenantId == tenantId && e.Id == activityId, cancellationToken)
            ?? throw new InvalidOperationException("Aktivitätsereignis nicht gefunden.");
        if (activity.Reversed)
        {
            await tx.CommitAsync(cancellationToken);
            return;
        }

        var reversal = activity.Reverse(clock.GetUtcNow());
        db.ActivityEvents.Add(reversal);
        await db.SaveChangesAsync(cancellationToken);

        var progress = await db.PersonProgress.SingleOrDefaultAsync(p => p.TenantId == tenantId && p.PersonId == activity.PersonId, cancellationToken);
        if (progress is not null)
        {
            var activeDays = await db.ActivityEvents
                .Where(e => e.TenantId == tenantId && e.PersonId == activity.PersonId && e.Points > 0 && !e.Reversed)
                .Select(e => e.Day)
                .Distinct()
                .ToListAsync(cancellationToken);
            progress.Reverse(reversal.Points, activeDays);
            await db.SaveChangesAsync(cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);
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
            .Where(e => e.TenantId == tenantId && e.PersonId == subject && e.ReversalOf == null)
            .OrderBy(e => e.OccurredAt)
            .Select(e => new ActivityRecord(e.Id, e.Kind, e.Source, e.OccurredAt, e.Points, e.Reversed))
            .ToListAsync(cancellationToken);
    }
}

internal sealed class ProgressQuery(ProgressDbContext db, IContextTransaction transaction, ITenantContextAccessor context, ITenantTimeZone timeZone, IDomainEventDispatcher events, TimeProvider clock) : IProgressQuery
{
    public async Task<PersonalProgress> GetMineAsync(CancellationToken cancellationToken)
    {
        var current = context.Require();
        var tenantId = current.RequireTenant();
        var personId = current.RequirePerson();
        var zone = await timeZone.GetAsync(cancellationToken);
        var now = clock.GetUtcNow();
        var today = TenantTimeZone.DayOf(now, zone);

        await using var tx = await transaction.BeginAsync(cancellationToken);
        var progress = await db.PersonProgress.AsNoTracking().SingleOrDefaultAsync(p => p.TenantId == tenantId && p.PersonId == personId, cancellationToken);
        var todayActions = await db.ActivityEvents.CountAsync(e => e.TenantId == tenantId && e.PersonId == personId && e.Day == today && e.Points > 0 && !e.Reversed, cancellationToken);
        var checkedIn = await db.CheckIns.AnyAsync(c => c.TenantId == tenantId && c.PersonId == personId && c.Day == today, cancellationToken);
        var earned = await db.Badges.Where(b => b.TenantId == tenantId && b.PersonId == personId).OrderBy(b => b.AwardedAt).ToListAsync(cancellationToken);

        // Nullwerte sind verboten (Fortschritt 1): das erste Abzeichen ist spätestens beim ersten Blick da.
        if (earned.Count == 0)
        {
            var award = BadgeAward.Grant(tenantId, personId, BadgeCatalog.ErsterTag, now);
            db.Badges.Add(award);
            var definition = BadgeCatalog.Get(award.BadgeKey);
            var badgeEvent = ProgressEvent.Create(tenantId, ProgressEventTypes.BadgeAwarded, new BadgeAwardedPayload(personId.Value, award.BadgeKey, definition.TextKey, definition.Category.ToString()), now, personId);
            db.Events.Add(badgeEvent);
            await db.SaveChangesAsync(cancellationToken);
            await events.DispatchAsync(ProgressEventTypes.BadgeAwarded, badgeEvent.Id, cancellationToken);
            earned.Add(award);
        }

        await tx.CommitAsync(cancellationToken);

        var earnedKeys = earned.Select(b => b.BadgeKey).ToHashSet(StringComparer.Ordinal);
        return new PersonalProgress(
            progress?.PointsBalance ?? 0,
            progress?.Level ?? 1,
            progress?.CurrentStreak ?? 0,
            progress?.LongestStreak ?? 0,
            progress?.DailyGoal ?? 1,
            todayActions,
            checkedIn,
            earned.Select(b => ToRecord(BadgeCatalog.Get(b.BadgeKey), b.AwardedAt)).ToList(),
            BadgeCatalog.NextPerCategory(earnedKeys).Select(d => ToRecord(d, null)).ToList());
    }

    public async Task SetDailyGoalAsync(int goal, CancellationToken cancellationToken)
    {
        var current = context.Require();
        var tenantId = current.RequireTenant();
        var personId = current.RequirePerson();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var progress = await db.PersonProgress.SingleOrDefaultAsync(p => p.TenantId == tenantId && p.PersonId == personId, cancellationToken);
        if (progress is null)
        {
            progress = PersonProgress.Start(tenantId, personId);
            db.PersonProgress.Add(progress);
        }

        progress.SetDailyGoal(goal);
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    public async Task<bool> CheckedInTodayAsync(PersonId personId, CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        var today = TenantTimeZone.DayOf(clock.GetUtcNow(), await timeZone.GetAsync(cancellationToken));
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var checkedIn = await db.CheckIns.AnyAsync(c => c.TenantId == tenantId && c.PersonId == personId && c.Day == today, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return checkedIn;
    }

    private static BadgeRecord ToRecord(BadgeDefinition definition, DateTimeOffset? awardedAt) => new(definition.Key, definition.Category, definition.TextKey, awardedAt);
}

internal sealed class CheckInService(ProgressDbContext db, IContextTransaction transaction, ITenantContextAccessor context, ITenantTimeZone timeZone, IActivityRecorder activities, IDomainEventDispatcher events, TimeProvider clock) : ICheckInService
{
    public async Task<CheckInOutcome> CheckInAsync(CheckInTiles tiles, CancellationToken cancellationToken)
    {
        var current = context.Require();
        var tenantId = current.RequireTenant();
        var personId = current.RequirePerson();
        var now = clock.GetUtcNow();
        var today = TenantTimeZone.DayOf(now, await timeZone.GetAsync(cancellationToken));
        await using var tx = await transaction.BeginAsync(cancellationToken);
        if (await db.CheckIns.AnyAsync(c => c.TenantId == tenantId && c.PersonId == personId && c.Day == today, cancellationToken))
        {
            await tx.CommitAsync(cancellationToken);
            return CheckInOutcome.AlreadyToday;
        }

        var activityId = await activities.RecordAsync(personId, ActivityKinds.CheckIn, ActivitySource.Self, now, cancellationToken);
        db.CheckIns.Add(CheckIn.Record(tenantId, personId, today, tiles, now, activityId));
        var checkInEvent = ProgressEvent.Create(tenantId, ProgressEventTypes.CheckInRecorded, new CheckInRecordedPayload(personId.Value, today), now, personId);
        db.Events.Add(checkInEvent);
        await db.SaveChangesAsync(cancellationToken);
        await events.DispatchAsync(ProgressEventTypes.CheckInRecorded, checkInEvent.Id, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return CheckInOutcome.Recorded;
    }
}

internal sealed class ProgressAggregates(ProgressDbContext db, IContextTransaction transaction, ITenantContextAccessor context, ITenantTimeZone timeZone, IOrganisationDirectory organisations, TimeProvider clock) : IProgressAggregates
{
    public async Task<ParticipationAggregate> ParticipationAsync(Guid? groupId, string period, CancellationToken cancellationToken)
    {
        var current = context.Require();
        var tenantId = current.RequireTenant();
        // Rechtematrix (Organisation 4.2): Beteiligungsquoten für Tenant-Admin und Programm-Manager je Gruppe, Einsichtsrolle nur tenantweit.
        var allowed = current.HasRole(Role.TenantAdmin) || current.HasRole(Role.ProgrammeManager) || (current.HasRole(Role.Insight) && groupId is null);
        if (!allowed)
        {
            throw new UnauthorizedAccessException("Beteiligungsquoten nur nach Rechtematrix (Organisation 4.2).");
        }

        var zone = await timeZone.GetAsync(cancellationToken);
        var today = TenantTimeZone.DayOf(clock.GetUtcNow(), zone);
        // Zeiträume nur Kalenderwoche und Kalendermonat (Datenschutz 4.2); keine feineren Schnitte.
        var (from, to) = period == "week"
            ? (today.AddDays(-(((int)today.DayOfWeek + 6) % 7)), today.AddDays(7 - (((int)today.DayOfWeek + 6) % 7)))
            : (new DateOnly(today.Year, today.Month, 1), new DateOnly(today.Year, today.Month, 1).AddMonths(1));

        IReadOnlyList<PersonId> population = groupId is { } g ? await organisations.ListGroupMembersAsync(g, cancellationToken) : await organisations.ListActiveMemberIdsAsync(cancellationToken);
        var populationIds = population.ToList();

        await using var tx = await transaction.BeginAsync(cancellationToken);
        var active = await db.ActivityEvents
            .Where(e => e.TenantId == tenantId && populationIds.Contains(e.PersonId) && e.Day >= from && e.Day < to && e.Points > 0 && !e.Reversed && e.Source != ActivitySource.Automatic)
            .Select(e => e.PersonId)
            .Distinct()
            .CountAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        // Mindestzahl (A-023): unter fünf Personen mit Handlung nichts; darüber Quote auf die Sollstärke als Schätzgröße (Organisation 2.2).
        if (!AggregateRule.MayPublish(active))
        {
            return new ParticipationAggregate(false, null, null, null);
        }

        var headcount = await organisations.GetHeadcountAtAsync(groupId, today, cancellationToken);
        var percent = headcount is > 0 ? (int?)Math.Min(100, (int)Math.Round(active * 100m / headcount.Value, 0, MidpointRounding.AwayFromZero)) : null;
        return new ParticipationAggregate(true, active, headcount, percent);
    }
}
