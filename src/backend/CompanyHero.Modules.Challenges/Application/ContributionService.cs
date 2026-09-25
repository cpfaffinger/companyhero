using CompanyHero.Modules.Challenges.Domain;
using CompanyHero.Modules.Challenges.Infrastructure;
using CompanyHero.Modules.Progress.Application;
using CompanyHero.Modules.Progress.Domain;
using CompanyHero.Platform.Data;
using CompanyHero.Platform.Events;
using CompanyHero.Platform.Jobs;
using CompanyHero.Platform.Metering;
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

/// <summary>Feste Bezeichner der Erfassung: Aktivitätsart für Fortschritt, Metrik und Modul für Metering (Challenges 8).</summary>
public static class ContributionConstants
{
    public const string MeteringModule = "M1";
    public const string ParticipantDayMetric = "challenge.participant_day";
    public const string ActivityKind = "challenge_contribution";
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
}

internal sealed class ContributionService(
    ChallengesDbContext db,
    IContextTransaction transaction,
    ITenantContextAccessor context,
    TimeProvider clock,
    IActivityRecorder activities,
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

        var rejection = ContributionRules.Validate(challenge, challenge.Metric, submission.Value, submission.RecordedAt, receivedAt);
        if (rejection != ContributionRejection.None)
        {
            await tx.RollbackAsync(cancellationToken);
            return new ContributionOutcome(ContributionResult.Rejected, null, rejection);
        }

        var contribution = Contribution.Record(tenantId, challenge.Id, personId, submission.Value, submission.RecordedAt, receivedAt, submission.Channel);
        db.Contributions.Add(contribution);
        proof.Commit(contribution.Id, contentHash, receivedAt);

        var domainEvent = ChallengeEvent.ContributionRecorded(tenantId, contribution, receivedAt);
        db.Events.Add(domainEvent);

        // Genau ein pauschales Aktivitätsereignis je Beitrag (Challenges 8, Fortschritt 2.1); Messwert bleibt hier.
        await activities.RecordAsync(personId, ContributionConstants.ActivityKind, ActivitySource.Self, contribution.RecordedAt, cancellationToken);

        // Teilnehmertag: einmal je Person und Kalendertag der Challenge (Challenges 8 ↔ Metering); anonymer Bezug ist die Challenge.
        if (await IsFirstContributionOfDayAsync(tenantId, challenge.Id, personId, contribution.RecordedAt, cancellationToken))
        {
            await metering.EmitAsync(
                new MeteringEmission(ContributionConstants.MeteringModule, ContributionConstants.ParticipantDayMetric, challenge.Id.ToString("D"), 1m, MeteringSource.Self, contribution.RecordedAt, $"{domainEvent.Id:D}:{ContributionConstants.ParticipantDayMetric}"),
                cancellationToken);
        }

        // Folgejob und Zustellung an Abonnenten in derselben Transaktion (A-006).
        await jobs.EnqueueAsync(new JobRequest(CollectiveRecalculateHandler.JobType, challenge.Id.ToString("D"), $"contribution:{contribution.Id:D}"), cancellationToken);
        await events.DispatchAsync(ChallengeEventTypes.ContributionRecorded, domainEvent.Id, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return new ContributionOutcome(ContributionResult.Recorded, contribution.Id);
    }

    private async Task<bool> IsFirstContributionOfDayAsync(TenantId tenantId, Guid challengeId, PersonId personId, DateTimeOffset recordedAt, CancellationToken cancellationToken)
    {
        var day = TenantTimeZone.DayOf(recordedAt);
        var from = TenantTimeZone.StartOfDay(day);
        var to = TenantTimeZone.StartOfDay(day.AddDays(1));
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
