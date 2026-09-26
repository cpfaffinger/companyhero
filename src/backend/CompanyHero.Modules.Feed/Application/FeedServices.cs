using System.Globalization;
using System.Text.Json.Nodes;
using CompanyHero.Modules.Challenges.Application;
using CompanyHero.Modules.Feed.Domain;
using CompanyHero.Modules.Feed.Infrastructure;
using CompanyHero.Modules.Identity.Application;
using CompanyHero.Modules.Organisation.Application;
using CompanyHero.Modules.Privacy.Application;
using CompanyHero.Modules.Privacy.Domain;
using CompanyHero.Modules.Progress.Application;
using CompanyHero.Modules.Progress.Domain;
using CompanyHero.Platform.Data;
using CompanyHero.Platform.Events;
using CompanyHero.Platform.Jobs;
using CompanyHero.Platform.Privacy;
using CompanyHero.Platform.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyHero.Modules.Feed.Application;

/// <summary>Kopfkarte (Feed 2.3): laufende Challenge des Tenants oder das persönliche Tagesziel.</summary>
public sealed record HeadCardRecord(ChallengeCardRecord? Challenge, int DailyGoal, int TodayActions);

public sealed record FeedPageRecord(HeadCardRecord Head, bool CheckInDue, IReadOnlyList<FeedCard> Cards, IReadOnlyDictionary<PersonId, string> DisplayNames);

public enum PostOutcome
{
    Created = 1,
    VisibilityOnlyMe = 2,
    DailyLimitReached = 3,
}

public sealed record PostResult(PostOutcome Outcome, Guid? EntryId);

/// <summary>Lesen und Schreiben des Feeds im Kontext der angemeldeten Person (Feed 2, 3.2).</summary>
public interface IFeedService
{
    Task<FeedPageRecord> ReadAsync(CancellationToken cancellationToken);

    /// <summary>Mitglieder-Beitrag (Feed 3.2): mit „Nur für mich“ kein Beitrag; Deckel fünf je Tag; zählt als Handlung für Fortschritt.</summary>
    Task<PostResult> PostAsync(string body, CancellationToken cancellationToken);

    /// <summary>Löschen des eigenen Beitrags (Feed 3.2). Falsch, wenn er nicht existiert oder nicht der eigene ist.</summary>
    Task<bool> DeletePostAsync(Guid entryId, CancellationToken cancellationToken);
}

internal sealed class FeedService(FeedDbContext db, IContextTransaction transaction, ITenantContextAccessor context, ITenantTimeZone timeZone, IOrganisationDirectory organisations, IVisibilityRule visibility, IVisibilityChoice choices, IPersonDirectory persons, IChallengeCatalog challenges, IProgressQuery progress, IActivityRecorder activities, TimeProvider clock) : IFeedService
{
    public async Task<FeedPageRecord> ReadAsync(CancellationToken cancellationToken)
    {
        var current = context.Require();
        var tenantId = current.RequireTenant();
        var reader = current.RequirePerson();
        await using var tx = await transaction.BeginAsync(cancellationToken);

        var running = await challenges.ListRunningAsync(cancellationToken);
        var head = running.FirstOrDefault(c => c.Challenge.State is Challenges.Domain.ChallengeState.Running or Challenges.Domain.ChallengeState.Grace)
            ?? (running.Count > 0 ? running[0] : null);
        var mine = await progress.GetMineAsync(cancellationToken);
        var myGroups = (await organisations.GetGroupsOfAsync(reader, cancellationToken)).Where(g => g.GroupId is not null).Select(g => g.GroupId!.Value).ToList();

        var since = clock.GetUtcNow() - FeedRules.Retention;
        var entries = await db.Entries.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.OccurredAt >= since)
            .OrderByDescending(e => e.OccurredAt)
            .Take(FeedRules.PageSize * 4)
            .ToListAsync(cancellationToken);
        entries = entries.Where(e => e.Scope == FeedScope.Tenant || e.GroupIds.Any(myGroups.Contains)).ToList();

        var subjects = entries.Where(e => e.SubjectPersonId is not null).Select(e => e.SubjectPersonId!.Value).Distinct().ToList();
        var visible = await visibility.FilterVisibleAsync(subjects, VisibilityPurpose.Presence, cancellationToken);
        var cards = FeedAssembly.Assemble(entries, reader, visible, AggregateRule.MinimumPersons, FeedRules.MaxSystemCardsPerDay).Take(FeedRules.PageSize).ToList();
        var shown = cards.Where(c => c.Subject is not null).Select(c => c.Subject!.Value).Distinct().ToList();
        var names = shown.Count == 0 ? new Dictionary<PersonId, string>() : await persons.GetDisplayNamesAsync(shown, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return new FeedPageRecord(new HeadCardRecord(head, mine.DailyGoal, mine.TodayActions), !mine.CheckedInToday, cards, names);
    }

    public async Task<PostResult> PostAsync(string body, CancellationToken cancellationToken)
    {
        var current = context.Require();
        var tenantId = current.RequireTenant();
        var author = current.RequirePerson();
        var text = FeedRules.ValidatePost(body);
        var zone = await timeZone.GetAsync(cancellationToken);
        var now = clock.GetUtcNow();
        var day = TenantTimeZone.DayOf(now, zone);
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var level = await choices.GetAsync(author, cancellationToken);
        if (level is null or VisibilityLevel.OnlyMe)
        {
            return new PostResult(PostOutcome.VisibilityOnlyMe, null);
        }

        var today = await db.Entries.CountAsync(e => e.TenantId == tenantId && e.Kind == FeedEntryKind.MemberPost && e.SubjectPersonId == author && e.Day == day, cancellationToken);
        if (today >= FeedRules.MaxPostsPerPersonAndDay)
        {
            return new PostResult(PostOutcome.DailyLimitReached, null);
        }

        var entry = FeedEntry.MemberPost(tenantId, author, text, now, day);
        db.Entries.Add(entry);
        await db.SaveChangesAsync(cancellationToken);
        await activities.RecordAsync(author, ActivityKinds.FeedPost, ActivitySource.Self, now, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return new PostResult(PostOutcome.Created, entry.Id);
    }

    public async Task<bool> DeletePostAsync(Guid entryId, CancellationToken cancellationToken)
    {
        var current = context.Require();
        var tenantId = current.RequireTenant();
        var author = current.RequirePerson();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var deleted = await db.Entries.Where(e => e.TenantId == tenantId && e.Id == entryId && e.Kind == FeedEntryKind.MemberPost && e.SubjectPersonId == author).ExecuteDeleteAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return deleted > 0;
    }
}

/// <summary>
/// Projektion der Fachereignisse in Feed-Einträge (Feed 2.1): liest das Ereignis über die Ereignisquelle des Veröffentlichers, nie
/// über dessen Tabellen; die Ereigniskennung ist der Eindeutigkeitsschlüssel, mehrfache Zustellung erzeugt keinen zweiten Eintrag.
/// </summary>
internal sealed class FeedProjector(FeedDbContext db, IContextTransaction transaction, ITenantContextAccessor context, ITenantTimeZone timeZone)
{
    public async Task ProjectAsync(DomainEventRecord record, CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        var zone = await timeZone.GetAsync(cancellationToken);
        var payload = JsonNode.Parse(record.PayloadJson)?.AsObject() ?? throw new InvalidOperationException("Leere Nutzdaten.");
        var eventKey = $"{record.Type}:{record.Id:D}";
        var day = TenantTimeZone.DayOf(record.OccurredAt, zone);
        var entry = record.Type switch
        {
            "challenges.started" => FeedEntry.SystemEvent(tenantId, eventKey, FeedTextKeys.ChallengeStarted, Params(("titel", Text(payload, "title"))), record.OccurredAt, day, referenceKind: "challenge", referenceId: Id(payload, "challengeId")),
            "challenges.milestone.reached" => FeedEntry.SystemEvent(tenantId, eventKey, FeedTextKeys.Milestone, Params(("titel", Text(payload, "title")), ("prozent", Text(payload, "percent"))), record.OccurredAt, day, referenceKind: "challenge", referenceId: Id(payload, "challengeId")),
            "challenges.ended" => FeedEntry.SystemEvent(tenantId, eventKey, FeedTextKeys.ChallengeEnded, Params(("titel", Text(payload, "title")), ("prozent", Text(payload, "percent"))), record.OccurredAt, day, referenceKind: "challenge", referenceId: Id(payload, "challengeId")),
            "progress.badge.awarded" => FeedEntry.SystemEvent(tenantId, eventKey, FeedTextKeys.BadgeAwarded, Params(("abzeichen", Text(payload, "textKey"))), record.OccurredAt, day, subject: Person(payload), referenceKind: "badge"),
            "progress.level.reached" => FeedEntry.SystemEvent(tenantId, eventKey, FeedTextKeys.LevelReached, Params(("stufe", Text(payload, "level"))), record.OccurredAt, day, subject: Person(payload), referenceKind: "level"),
            "progress.checkin.recorded" => FeedEntry.SystemEvent(tenantId, eventKey, FeedTextKeys.CheckIn, Params(), record.OccurredAt, day, subject: Person(payload), referenceKind: "checkin"),
            _ => null,
        };
        if (entry is null)
        {
            return;
        }

        await using var tx = await transaction.BeginAsync(cancellationToken);
        if (!await db.Entries.AnyAsync(e => e.TenantId == tenantId && e.EventKey == eventKey, cancellationToken))
        {
            db.Entries.Add(entry);
            await db.SaveChangesAsync(cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);
    }

    private static Dictionary<string, string> Params(params (string Key, string Value)[] values)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in values)
        {
            result[key] = value;
        }

        return result;
    }

    private static string Text(JsonObject payload, string name) => payload[name] switch
    {
        null => string.Empty,
        JsonValue v when v.TryGetValue<string>(out var s) => s,
        JsonValue v when v.TryGetValue<int>(out var i) => i.ToString(CultureInfo.InvariantCulture),
        var other => other.ToJsonString(),
    };

    private static Guid? Id(JsonObject payload, string name) => Guid.TryParse(Text(payload, name), out var id) ? id : null;

    private static PersonId? Person(JsonObject payload) => Id(payload, "personId") is { } id ? new PersonId(id) : null;
}

/// <summary>Abonnent aller Ereignisse der Challenges (Start, Meilenstein, Ende); der Typ steht im gelesenen Ereignis.</summary>
internal sealed class ChallengeFeedHandler(IDomainEventReader reader, FeedProjector projector) : IJobHandler
{
    public static string JobType => "feed.project.challenges";

    public async Task HandleAsync(JobExecution job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        var record = await reader.ReadAsync("challenges.", Guid.ParseExact(job.Reference, "D"), cancellationToken)
            ?? throw new InvalidOperationException("Ereignis im Kontext des Tenants nicht lesbar.");
        await projector.ProjectAsync(record, cancellationToken);
    }
}

/// <summary>Abonnent der Ereignisse des Fortschritts (Abzeichen, Stufe, Check-in).</summary>
internal sealed class ProgressFeedHandler(IDomainEventReader reader, FeedProjector projector) : IJobHandler
{
    public static string JobType => "feed.project.progress";

    public async Task HandleAsync(JobExecution job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        var record = await reader.ReadAsync("progress.", Guid.ParseExact(job.Reference, "D"), cancellationToken)
            ?? throw new InvalidOperationException("Ereignis im Kontext des Tenants nicht lesbar.");
        await projector.ProjectAsync(record, cancellationToken);
    }
}

/// <summary>Feed-Einträge bleiben zwölf Monate (Feed 2.6); der Lauf entfernt ältere je Tenant.</summary>
internal sealed class FeedRetentionTask(ITenantScopeFactory scopes, IOrganisationDirectory organisations, TimeProvider clock) : IScheduledTask
{
    public static string Name => "feed.retention";

    public static TimeSpan Interval => TimeSpan.FromHours(24);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var cutoff = clock.GetUtcNow() - FeedRules.Retention;
        foreach (var tenant in await organisations.ListActiveTenantsAsync(cancellationToken))
        {
            await scopes.RunAsync(TenantContext.ForTenant(tenant), async (sp, ct) =>
            {
                var db = sp.GetRequiredService<FeedDbContext>();
                var transaction = sp.GetRequiredService<IContextTransaction>();
                await using var tx = await transaction.BeginAsync(ct);
                await db.Entries.Where(e => e.TenantId == tenant && e.OccurredAt < cutoff).ExecuteDeleteAsync(ct);
                await tx.CommitAsync(ct);
            }, cancellationToken);
        }
    }
}

/// <summary>Auskunft (Datenschutz 6.4): eigene Beiträge und eigene Ereigniskarten; Löschung (5.2): alles mit Personenbezug entfernt.</summary>
internal sealed class FeedPersonalData(FeedDbContext db, IContextTransaction transaction, ITenantContextAccessor context) : IPersonalDataExporter, IPersonalDataEraser
{
    public string Section => "feed";

    public async Task<JsonNode> ExportAsync(PersonId personId, CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var entries = await db.Entries.AsNoTracking().Where(e => e.TenantId == tenantId && e.SubjectPersonId == personId).OrderBy(e => e.OccurredAt).ToListAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return new JsonObject
        {
            ["beitraege"] = new JsonArray(entries.Where(e => e.Kind == FeedEntryKind.MemberPost).Select(e => (JsonNode)new JsonObject { ["zeitpunkt"] = e.OccurredAt, ["text"] = e.Body }).ToArray()),
            ["ereignisse"] = new JsonArray(entries.Where(e => e.Kind == FeedEntryKind.System).Select(e => (JsonNode)new JsonObject { ["zeitpunkt"] = e.OccurredAt, ["art"] = e.TextKey }).ToArray()),
        };
    }

    public async Task EraseAsync(PersonId personId, CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        await db.Entries.Where(e => e.TenantId == tenantId && e.SubjectPersonId == personId).ExecuteDeleteAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }
}
