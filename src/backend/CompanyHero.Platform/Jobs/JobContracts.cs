using CompanyHero.Platform.Tenancy;

namespace CompanyHero.Platform.Jobs;

/// <summary>Kennung eines Jobs; zeitlich sortierbar (Backend 4).</summary>
public readonly record struct JobId(Guid Value)
{
    public static JobId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString("D");
}

/// <summary>Zustand eines Jobs in der logischen Queue (Backend 6.2, 6.3).</summary>
public enum JobStatus
{
    /// <summary>Wartet auf Beanspruchung ab <c>run_at</c>.</summary>
    Queued = 1,

    /// <summary>Von einem Worker beansprucht; der Lease läuft ab, wenn der Worker nicht bestätigt.</summary>
    Leased = 2,

    /// <summary>Dauerhaft verarbeitet und bestätigt.</summary>
    Succeeded = 3,

    /// <summary>Alle Versuche verbraucht; Replay ist eine bewusste Operator-Aktion.</summary>
    Dead = 4,
}

/// <summary>
/// Einreihungsauftrag. Enthält nur Routinginformationen (A-006): Jobtyp, fachliche Referenz und Idempotenzschlüssel.
/// Fachliche Nutzdaten bleiben in den tenantbezogenen Tabellen; der Handler lädt sie erst im Tenant-Kontext.
/// </summary>
/// <param name="JobType">Registrierter Jobtyp, etwa <c>challenges.collective.recalculate</c>.</param>
/// <param name="Reference">Fachliche Referenz (Kennung des Fachobjekts), keine Nutzdaten und kein Personenbezug.</param>
/// <param name="IdempotencyKey">Eindeutig je Tenant und Jobtyp; eine zweite Einreihung mit demselben Schlüssel erzeugt keinen zweiten Job.</param>
/// <param name="RunAt">Frühester Ausführungszeitpunkt; <c>null</c> bedeutet sofort.</param>
/// <param name="MaxAttempts">Versuche bis zum Dead-Letter-Status; <c>null</c> nimmt die Voreinstellung des Workers.</param>
public sealed record JobRequest(string JobType, string Reference, string IdempotencyKey, DateTimeOffset? RunAt = null, int? MaxAttempts = null);

public enum EnqueueOutcome
{
    Enqueued = 1,

    /// <summary>Ein Job mit demselben Idempotenzschlüssel existiert bereits (A-006: mehrfache Einreihung ist wirkungslos).</summary>
    AlreadyQueued = 2,
}

public sealed record EnqueueResult(EnqueueOutcome Outcome, JobId? JobId);

/// <summary>
/// Einreihung in die logische Queue. Läuft ausschließlich innerhalb der laufenden Kontexttransaktion des Scopes:
/// Der Job wird in derselben Transaktion geschrieben wie die fachliche Änderung, die ihn auslöst (Backend 6.1, A-006).
/// Erst nach deren Commit ist der Job sichtbar; ohne Commit gibt es ihn nicht.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Die Queue-Schnittstelle nach A-006 heißt so; der Transport dahinter darf wechseln.")]
public interface IJobQueue
{
    /// <summary>Reiht einen Job für den Tenant des aktiven Kontexts ein (Plattformkontext: Job ohne Tenant).</summary>
    Task<EnqueueResult> EnqueueAsync(JobRequest request, CancellationToken cancellationToken);
}

/// <summary>Der laufende Job aus Sicht seines Handlers.</summary>
public sealed record JobExecution(JobId Id, TenantId? TenantId, string JobType, string Reference, int Attempt, int MaxAttempts);

/// <summary>
/// Handler eines Jobtyps. Läuft im Kontext des Jobs (Tenant oder Plattform) innerhalb der Kontexttransaktion des Workers;
/// der Jobabschluss wird in derselben Transaktion bestätigt. Mehrfache Zustellung wird erwartet (Backend 6.3):
/// Handler sind idempotent und deduplizieren über fachliche Eindeutigkeitsschlüssel.
/// </summary>
public interface IJobHandler
{
    static abstract string JobType { get; }

    Task HandleAsync(JobExecution job, CancellationToken cancellationToken);
}

/// <summary>Ein Dead-Letter-Job aus Sicht des Operators; ohne Nutzdaten und ohne Personenbezug.</summary>
public sealed record DeadLetterJob(JobId Id, TenantId? TenantId, string JobType, string Reference, int Attempts, string? LastError, DateTimeOffset CreatedAt);

/// <summary>Operator-Anwendungsfälle der Queue (Backend 5.3, 6.3): Dead-Letter einsehen und bewusst erneut einreihen.</summary>
public interface IJobAdministration
{
    Task<IReadOnlyList<DeadLetterJob>> ListDeadLettersAsync(CancellationToken cancellationToken);

    /// <summary>Replay eines Dead-Letter-Jobs mit Begründung; wird protokolliert. Wahr, wenn der Job erneut wartet.</summary>
    Task<bool> ReplayAsync(JobId jobId, string reason, CancellationToken cancellationToken);
}

/// <summary>Der Lease dieses Workers ist verloren: ein anderer Worker hat den Job nach Ablauf des Lease übernommen.</summary>
public sealed class JobLeaseLostException : InvalidOperationException
{
    public JobLeaseLostException()
        : base("Der Lease des Jobs ist abgelaufen und wurde von einem anderen Worker übernommen; die Wirkung dieses Versuchs wird verworfen.")
    {
    }

    public JobLeaseLostException(string message)
        : base(message)
    {
    }

    public JobLeaseLostException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
