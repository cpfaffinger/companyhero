using System.Text.Json.Nodes;
using CompanyHero.Modules.Challenges.Domain;
using CompanyHero.Modules.Challenges.Infrastructure;
using CompanyHero.Modules.Organisation.Application;
using CompanyHero.Modules.Progress.Application;
using CompanyHero.Modules.Progress.Domain;
using CompanyHero.Platform.Data;
using CompanyHero.Platform.Events;
using CompanyHero.Platform.Jobs;
using CompanyHero.Platform.Metering;
using CompanyHero.Platform.Privacy;
using CompanyHero.Platform.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace CompanyHero.Modules.Challenges.Application;

/// <summary>Ein Beitrag, wie ihn Handy oder Kiosk übertragen; Idempotenz nach Kanal (A-009): Kiosk mit Vorgangskennung, Handy mit Client-Idempotenzschlüssel.</summary>
public sealed record ContributionSubmission(Guid ChallengeId, decimal Value, DateTimeOffset RecordedAt, ContributionChannel Channel, string Key);

public enum ContributionResult
{
    /// <summary>Neuer Beitrag gespeichert.</summary>
    Recorded = 1,

    /// <summary>Dieselbe Kennung mit demselben Inhalt: der bestehende Beitrag, keine zweite Wirkung.</summary>
    AlreadyRecorded = 2,

    /// <summary>Dieselbe Kennung mit abweichendem Inhalt (A-009).</summary>
    ConflictingContent = 3,

    /// <summary>Vorgangskennung unbekannt oder nicht an diese Person gebunden (A-005).</summary>
    OperationUnknown = 4,

    /// <summary>Vorgang terminal abgebrochen; nimmt keinen verspäteten Beitrag mehr an (A-005).</summary>
    OperationAborted = 5,

    ChallengeNotFound = 6,

    /// <summary>Fachregel verletzt (Challenges 3); Grund in <see cref="ContributionOutcome.Rejection"/>.</summary>
    Rejected = 7,
}

public sealed record ContributionOutcome(ContributionResult Result, Guid? ContributionId, ContributionRejection Rejection = ContributionRejection.None);

public enum ReversalResult
{
    Reversed = 1,
    NotFound = 2,
    AlreadyReversed = 3,
    GracePeriodOver = 4,
}

/// <summary>Eigener Beitrag aus Sicht der Person (Challenges 3): Wert, Zeitpunkt, Kanal, Korrekturzustand.</summary>
public sealed record OwnContributionRecord(Guid Id, decimal Value, DateTimeOffset RecordedAt, ContributionChannel Channel, bool Reversed, bool IsReversal);

/// <summary>Feste Bezeichner der Erfassung: Aktivitätsart für Fortschritt, Metrik und Modul für Metering (Challenges 8).</summary>
public static class ContributionConstants
{
    public const string MeteringModule = "M1";
    public const string ParticipantDayMetric = "challenge.participant_day";
    public const string ActivityKind = ActivityKinds.ChallengeContribution;
}

/// <summary>Öffentliche Anwendungsfunktionen der Erfassung (Domänenkarte 2, 3 „Idempotenz von Beiträgen“).</summary>
public interface IContributionService
{
    /// <summary>Kiosk (A-005): Backend reserviert vor dem Absenden eine Vorgangskennung, gebunden an Tenant und Person.</summary>
    Task<string> ReserveOperationAsync(CancellationToken cancellationToken);

    /// <summary>Offene Vorgänge der Person nach erneuter Anmeldung (A-005 Wiederaufnahme).</summary>
    Task<IReadOnlyList<string>> ListOpenOperationsAsync(CancellationToken cancellationToken);

    /// <summary>Terminal abbrechen: ein verspäteter Beitrag wird nicht mehr angenommen.</summary>
    Task<bool> AbortOperationAsync(string operationId, CancellationToken cancellationToken);

    /// <summary>
    /// Speichert einen Beitrag mit Idempotenznachweis atomar. In einer Transaktion: Beitrag, Fachereignis „Beitrag erfasst“,
    /// Aktivitätsereignis für Fortschritt, Metering-Ereignis beim ersten Beitrag der Person am Tag, Folgejob Kollektivstand,
    /// Zustellung an Abonnenten (Challenges 3, A-006).
    /// </summary>
    Task<ContributionOutcome> SubmitAsync(ContributionSubmission submission, CancellationToken cancellationToken);

    /// <summary>Korrektur eines eigenen Beitrags als Gegenbuchung bis zum Ende der Nachfrist (Challenges 3): Kollektivstand neu, Abzeichen bleiben.</summary>
    Task<ReversalResult> ReverseAsync(Guid contributionId, CancellationToken cancellationToken);

    /// <summary>Eigene Beiträge einer Challenge, nie die anderer Personen.</summary>
    Task<IReadOnlyList<OwnContributionRecord>> ListMineAsync(Guid challengeId, CancellationToken cancellationToken);
}

internal sealed class ContributionService(
    ChallengesDbContext db,
    IContextTransaction transaction,
    ITenantContextAccessor context,
    ITenantTimeZone timeZone,
    TimeProvider clock,
    IActivityRecorder activities,
    IOrganisationDirectory organisations,
    IMeteringEmitter metering,
    IJobQueue jobs,
    IDomainEventDispatcher events) : IContributionService
{
    public async Task<string> ReserveOperationAsync(CancellationToken cancellationToken)
    {
        var (tenantId, personId) = RequirePerson();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var key = ContributionKey.ReserveOperation(tenantId, personId, clock.GetUtcNow());
        db.ContributionKeys.Add(key);
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return key.Key;
    }

    public async Task<IReadOnlyList<string>> ListOpenOperationsAsync(CancellationToken cancellationToken)
    {
        var (tenantId, personId) = RequirePerson();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var open = await db.ContributionKeys
            .Where(k => k.TenantId == tenantId && k.PersonId == personId && k.Regime == KeyRegime.Operation && k.State == KeyState.Reserved)
            .OrderBy(k => k.ReservedAt)
            .Select(k => k.Key)
            .ToListAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return open;
    }

    public async Task<bool> AbortOperationAsync(string operationId, CancellationToken cancellationToken)
    {
        var (tenantId, personId) = RequirePerson();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var key = await db.ContributionKeys.SingleOrDefaultAsync(k => k.TenantId == tenantId && k.PersonId == personId && k.Key == ContributionKey.Normalize(operationId), cancellationToken);
        if (key is null || key.Regime != KeyRegime.Operation || key.State != KeyState.Reserved)
        {
            await tx.CommitAsync(cancellationToken);
            return false;
        }

        key.Abort();
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<ContributionOutcome> SubmitAsync(ContributionSubmission submission, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(submission);
        var (tenantId, personId) = RequirePerson();
        var regime = submission.Channel == ContributionChannel.Kiosk ? KeyRegime.Operation : KeyRegime.Client;
        if (regime == KeyRegime.Client && !ContributionKey.IsValidClientKey(submission.Key))
        {
            throw new ArgumentException("Der Idempotenzschlüssel ist keine zeitlich sortierbare Kennung (UUIDv7).", nameof(submission));
        }

        var key = ContributionKey.Normalize(submission.Key);
        var receivedAt = clock.GetUtcNow();
        var contentHash = ContributionKey.HashContent(submission.ChallengeId, submission.Value, submission.RecordedAt, submission.Channel);
        var zone = await timeZone.GetAsync(cancellationToken);

        await using var tx = await transaction.BeginAsync(cancellationToken);

        if (regime == KeyRegime.Client)
        {
            // Client-Schlüssel: der Nachweis wird zuerst reserviert; parallele Wiederholungen warten auf den ersten Commit.
            await db.Database.ExecuteSqlAsync(
                $"insert into challenges.contribution_key (tenant_id, person_id, key, regime, state, reserved_at) values ({tenantId.Value}, {personId.Value}, {key}, {(short)KeyRegime.Client}, {(short)KeyState.Reserved}, {receivedAt}) on conflict (tenant_id, person_id, key) do nothing",
                cancellationToken);
        }

        // Zeilensperre: Wiederholungen derselben Kennung laufen nacheinander und sehen das Ergebnis der ersten.
        var proof = await db.ContributionKeys
            .FromSql($"select * from challenges.contribution_key where tenant_id = {tenantId.Value} and person_id = {personId.Value} and key = {key} for update")
            .SingleOrDefaultAsync(cancellationToken);

        if (proof is null)
        {
            await tx.CommitAsync(cancellationToken);
            return new ContributionOutcome(ContributionResult.OperationUnknown, null);
        }

        switch (proof.State)
        {
            case KeyState.Committed:
                await tx.CommitAsync(cancellationToken);
                return proof.MatchesContent(contentHash)
                    ? new ContributionOutcome(ContributionResult.AlreadyRecorded, proof.ContributionId)
                    : new ContributionOutcome(ContributionResult.ConflictingContent, null);
            case KeyState.Aborted:
                await tx.CommitAsync(cancellationToken);
                return new ContributionOutcome(ContributionResult.OperationAborted, null);
            case KeyState.Reserved when proof.Regime != regime:
                await tx.CommitAsync(cancellationToken);
                return new ContributionOutcome(ContributionResult.ConflictingContent, null);
            default:
                break;
        }

        var challenge = await db.Challenges.SingleOrDefaultAsync(c => c.TenantId == tenantId && c.Id == submission.ChallengeId, cancellationToken);
        if (challenge is null)
        {
            // Der Nachweis bleibt reserviert (Client-Schlüssel) beziehungsweise offen (Vorgang); keine Wirkung.
            await tx.RollbackAsync(cancellationToken);
            return new ContributionOutcome(ContributionResult.ChallengeNotFound, null);
        }

        var rejection = ContributionRules.Validate(challenge, challenge.Metric, submission.Value, submission.RecordedAt, receivedAt, zone);
        if (rejection != ContributionRejection.None)
        {
            await tx.RollbackAsync(cancellationToken);
            return new ContributionOutcome(ContributionResult.Rejected, null, rejection);
        }

        // Genau ein pauschales Aktivitätsereignis je Beitrag (Challenges 8, Fortschritt 2.1); Messwert bleibt hier.
        var activityId = await activities.RecordAsync(personId, ContributionConstants.ActivityKind, ActivitySource.Self, submission.RecordedAt, cancellationToken);

        // Gruppen zum Beitragszeitpunkt (Organisation 3.3): ein späterer Wechsel verändert vergangene Kollektivstände nicht.
        var groups = (await organisations.GetGroupsOfAsync(personId, cancellationToken)).Where(g => g.GroupId is not null).Select(g => g.GroupId!.Value).ToList();

        var contribution = Contribution.Record(tenantId, challenge.Id, personId, submission.Value, submission.RecordedAt, receivedAt, submission.Channel, groups, activityId);
        db.Contributions.Add(contribution);
        proof.Commit(contribution.Id, contentHash, receivedAt);

        var domainEvent = ChallengeEvent.ContributionRecorded(tenantId, contribution, receivedAt);
        db.Events.Add(domainEvent);

        // Teilnehmertag: einmal je Person und Kalendertag der Challenge (Challenges 8 ↔ Metering); anonymer Bezug ist die Challenge,
        // Idempotenzschlüssel aus Fachereignis (Beitrag) und Metrik (Metering 2.1).
        if (await IsFirstContributionOfDayAsync(tenantId, challenge.Id, personId, contribution.RecordedAt, zone, cancellationToken))
        {
            await metering.EmitAsync(
                new MeteringEmission(ContributionConstants.MeteringModule, ContributionConstants.ParticipantDayMetric, challenge.Id.ToString("D"), 1m, MeteringSource.Self, contribution.RecordedAt, ParticipantDayKey(contribution.Id)),
                cancellationToken);
        }

        // Folgejob und Zustellung an Abonnenten in derselben Transaktion (A-006).
        await jobs.EnqueueAsync(new JobRequest(CollectiveRecalculateHandler.JobType, challenge.Id.ToString("D"), $"contribution:{contribution.Id:D}"), cancellationToken);
        await events.DispatchAsync(ChallengeEventTypes.ContributionRecorded, domainEvent.Id, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return new ContributionOutcome(ContributionResult.Recorded, contribution.Id);
    }

    public async Task<ReversalResult> ReverseAsync(Guid contributionId, CancellationToken cancellationToken)
    {
        var (tenantId, personId) = RequirePerson();
        if (context.Require().IsKiosk)
        {
            throw new UnauthorizedAccessException("Am Kiosk keine Korrektur vergangener Tage (Challenges 3).");
        }

        var now = clock.GetUtcNow();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var contribution = await db.Contributions.SingleOrDefaultAsync(c => c.TenantId == tenantId && c.Id == contributionId && c.PersonId == personId, cancellationToken);
        if (contribution is null || contribution.IsReversal)
        {
            await tx.CommitAsync(cancellationToken);
            return ReversalResult.NotFound;
        }

        if (contribution.Reversed)
        {
            await tx.CommitAsync(cancellationToken);
            return ReversalResult.AlreadyReversed;
        }

        var challenge = await db.Challenges.AsNoTracking().SingleAsync(c => c.TenantId == tenantId && c.Id == contribution.ChallengeId, cancellationToken);
        if (!ContributionRules.MayReverse(challenge, now))
        {
            await tx.CommitAsync(cancellationToken);
            return ReversalResult.GracePeriodOver;
        }

        var reversal = contribution.Reverse(now);
        db.Contributions.Add(reversal);
        var domainEvent = ChallengeEvent.Create(tenantId, ChallengeEventTypes.ContributionReversed, new ContributionReversedPayload(contribution.Id, reversal.Id, challenge.Id), now, personId);
        db.Events.Add(domainEvent);
        await db.SaveChangesAsync(cancellationToken);

        // Gegenereignis für Fortschritt (Fortschritt 2.1); verliehene Abzeichen bleiben (Fortschritt 4.2).
        if (contribution.ActivityEventId is { } activityId)
        {
            await activities.ReverseAsync(activityId, cancellationToken);
        }

        // Gegenbuchung im Ledger (Metering 3.3): der Teilnehmertag entfällt, wenn an diesem Tag kein gültiger Beitrag der Person bleibt.
        var zone = await timeZone.GetAsync(cancellationToken);
        var day = TenantTimeZone.DayOf(contribution.RecordedAt, zone);
        var from = TenantTimeZone.StartOfDay(day, zone);
        var to = TenantTimeZone.StartOfDay(day.AddDays(1), zone);
        var remaining = await db.Contributions
            .Where(c => c.TenantId == tenantId && c.ChallengeId == challenge.Id && c.PersonId == personId && c.RecordedAt >= from && c.RecordedAt < to && c.ReversalOf == null && !c.Reversed)
            .AnyAsync(cancellationToken);
        if (!remaining)
        {
            var first = await db.Contributions
                .Where(c => c.TenantId == tenantId && c.ChallengeId == challenge.Id && c.PersonId == personId && c.RecordedAt >= from && c.RecordedAt < to && c.ReversalOf == null)
                .OrderBy(c => c.RecordedAt).ThenBy(c => c.Id)
                .Select(c => c.Id)
                .FirstAsync(cancellationToken);
            await metering.EmitAsync(
                new MeteringEmission(ContributionConstants.MeteringModule, ContributionConstants.ParticipantDayMetric, challenge.Id.ToString("D"), -1m, MeteringSource.Self, contribution.RecordedAt, ParticipantDayKey(reversal.Id), ParticipantDayKey(first)),
                cancellationToken);
        }

        await jobs.EnqueueAsync(new JobRequest(CollectiveRecalculateHandler.JobType, challenge.Id.ToString("D"), $"reversal:{reversal.Id:D}"), cancellationToken);
        await events.DispatchAsync(ChallengeEventTypes.ContributionReversed, domainEvent.Id, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return ReversalResult.Reversed;
    }

    public async Task<IReadOnlyList<OwnContributionRecord>> ListMineAsync(Guid challengeId, CancellationToken cancellationToken)
    {
        var (tenantId, personId) = RequirePerson();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var rows = await db.Contributions.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.ChallengeId == challengeId && c.PersonId == personId)
            .OrderBy(c => c.RecordedAt).ThenBy(c => c.Id)
            .Select(c => new OwnContributionRecord(c.Id, c.Value, c.RecordedAt, c.Channel, c.Reversed, c.ReversalOf != null))
            .ToListAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return rows;
    }

    private static string ParticipantDayKey(Guid contributionId) => $"{contributionId:D}:{ContributionConstants.ParticipantDayMetric}";

    private async Task<bool> IsFirstContributionOfDayAsync(TenantId tenantId, Guid challengeId, PersonId personId, DateTimeOffset recordedAt, TimeZoneInfo zone, CancellationToken cancellationToken)
    {
        var day = TenantTimeZone.DayOf(recordedAt, zone);
        var from = TenantTimeZone.StartOfDay(day, zone);
        var to = TenantTimeZone.StartOfDay(day.AddDays(1), zone);
        return !await db.Contributions.AnyAsync(
            c => c.TenantId == tenantId && c.ChallengeId == challengeId && c.PersonId == personId && c.RecordedAt >= from && c.RecordedAt < to,
            cancellationToken);
    }

    private (TenantId TenantId, PersonId PersonId) RequirePerson()
    {
        var current = context.Require();
        return (current.RequireTenant(), current.RequirePerson());
    }
}

/// <summary>Auskunft (Datenschutz 6.4): eigene Beiträge; Löschung (5.2): Beiträge bleiben als Summenanteil ohne Personenbezug, Nachweise entfallen.</summary>
internal sealed class ChallengesPersonalData(ChallengesDbContext db, IContextTransaction transaction, ITenantContextAccessor context, IJobQueue jobs) : IPersonalDataExporter, IPersonalDataEraser
{
    public string Section => "challenges";

    public async Task<JsonNode> ExportAsync(PersonId personId, CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var rows = await db.Contributions.AsNoTracking().Where(c => c.TenantId == tenantId && c.PersonId == personId).OrderBy(c => c.RecordedAt).ToListAsync(cancellationToken);
        var ids = rows.Select(r => r.ChallengeId).Distinct().ToList();
        var titles = await db.Challenges.AsNoTracking().Where(c => c.TenantId == tenantId && ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Title, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return new JsonObject
        {
            ["beitraege"] = new JsonArray(rows.Select(r => (JsonNode)new JsonObject
            {
                ["challenge"] = titles.GetValueOrDefault(r.ChallengeId, r.ChallengeId.ToString("D")),
                ["wert"] = r.Value.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture),
                ["erfasst"] = r.RecordedAt,
                ["kanal"] = r.Channel.ToString(),
                ["gegenbuchung"] = r.IsReversal,
            }).ToArray()),
        };
    }

    public async Task EraseAsync(PersonId personId, CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        var anonymous = Contribution.AnonymousPerson;
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var challenges = await db.Contributions.Where(c => c.TenantId == tenantId && c.PersonId == personId).Select(c => c.ChallengeId).Distinct().ToListAsync(cancellationToken);
        await db.ContributionKeys.Where(k => k.TenantId == tenantId && k.PersonId == personId).ExecuteDeleteAsync(cancellationToken);
        await db.Contributions.Where(c => c.TenantId == tenantId && c.PersonId == personId).ExecuteUpdateAsync(u => u.SetProperty(c => c.PersonId, anonymous).SetProperty(c => c.GroupIds, new List<Guid>()), cancellationToken);
        // Der Kollektivstand zählt anonyme Beiträge in Summen, nicht mehr als Beitragende: Neuberechnung als Job in derselben Transaktion (A-006).
        foreach (var challengeId in challenges)
        {
            await jobs.EnqueueAsync(new JobRequest(CollectiveRecalculateHandler.JobType, challengeId.ToString("D"), $"erase:{personId.Value:D}:{challengeId:D}"), cancellationToken);
        }

        await db.Events.Where(e => e.TenantId == tenantId && e.CausedBy == personId).ExecuteUpdateAsync(u => u.SetProperty(e => e.CausedBy, (PersonId?)null), cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }
}
