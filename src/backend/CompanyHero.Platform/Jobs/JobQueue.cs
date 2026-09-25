using CompanyHero.Platform.Data;
using CompanyHero.Platform.Tenancy;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace CompanyHero.Platform.Jobs;

/// <summary>Einreihung in derselben Transaktion wie die fachliche Änderung (Backend 6.1, A-006 „atomare Kopplung“).</summary>
internal sealed class JobQueue(ContextTransaction transaction, ITenantContextAccessor accessor, IOptions<JobWorkerOptions> options, TimeProvider clock) : IJobQueue
{
    public async Task<EnqueueResult> EnqueueAsync(JobRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.JobType);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Reference);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.IdempotencyKey);

        var context = accessor.Require();
        var current = transaction.Current;
        var now = clock.GetUtcNow();
        var id = JobId.New();

        await using var command = current.Connection!.CreateCommand();
        command.Transaction = current;
        command.CommandText =
            """
            insert into platform.job (id, tenant_id, job_type, reference, idempotency_key, status, run_at, attempts, max_attempts, created_at)
            values ($1, $2, $3, $4, $5, 1, $6, 0, $7, $8)
            on conflict (job_type, idempotency_key, tenant_id) do nothing
            returning id
            """;
        command.Parameters.Add(new NpgsqlParameter { Value = id.Value });
        command.Parameters.Add(new NpgsqlParameter<Guid?> { TypedValue = context.Kind == TenantContextKind.Tenant ? context.RequireTenant().Value : null, NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Uuid });
        command.Parameters.Add(new NpgsqlParameter { Value = request.JobType });
        command.Parameters.Add(new NpgsqlParameter { Value = request.Reference });
        command.Parameters.Add(new NpgsqlParameter { Value = request.IdempotencyKey });
        command.Parameters.Add(new NpgsqlParameter { Value = request.RunAt ?? now });
        command.Parameters.Add(new NpgsqlParameter { Value = request.MaxAttempts ?? options.Value.DefaultMaxAttempts });
        command.Parameters.Add(new NpgsqlParameter { Value = now });

        var inserted = await command.ExecuteScalarAsync(cancellationToken);
        return inserted is Guid
            ? new EnqueueResult(EnqueueOutcome.Enqueued, id)
            : new EnqueueResult(EnqueueOutcome.AlreadyQueued, null);
    }
}

/// <summary>Operator-Anwendungsfälle: nur im Plattformkontext (Backend 5.3); jeder Replay wird protokolliert (Backend 6.3).</summary>
internal sealed class JobAdministration(NpgsqlDataSource dataSource, ITenantContextAccessor accessor, TimeProvider clock, ILogger<JobAdministration> logger) : IJobAdministration
{
    public async Task<IReadOnlyList<DeadLetterJob>> ListDeadLettersAsync(CancellationToken cancellationToken)
    {
        RequirePlatform();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "select id, tenant_id, job_type, reference, attempts, last_error, created_at from platform.job where status = 4 order by created_at", connection);
        var result = new List<DeadLetterJob>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new DeadLetterJob(
                new JobId(reader.GetGuid(0)),
                reader.IsDBNull(1) ? null : new TenantId(reader.GetGuid(1)),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt32(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetFieldValue<DateTimeOffset>(6)));
        }

        return result;
    }

    public async Task<bool> ReplayAsync(JobId jobId, string reason, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        var context = RequirePlatform();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "update platform.job set status = 1, attempts = 0, run_at = $2, replayed_at = $2, lease_until = null, last_error = null, completed_at = null where id = $1 and status = 4",
            connection);
        command.Parameters.Add(new NpgsqlParameter { Value = jobId.Value });
        command.Parameters.Add(new NpgsqlParameter { Value = clock.GetUtcNow() });
        var replayed = await command.ExecuteNonQueryAsync(cancellationToken) == 1;
        if (replayed)
        {
            // Protokoll ohne Personenbezug: Operator-Kennung ist pseudonym, Begründung ist Freitext des Operators.
            logger.LogWarning("Dead-Letter-Replay: Job {JobId} durch Operator {Operator}; Grund: {Reason}", jobId, context.PersonId?.ToString() ?? "unbekannt", reason);
        }

        return replayed;
    }

    private TenantContext RequirePlatform()
    {
        var context = accessor.Require();
        return context.Kind == TenantContextKind.Platform
            ? context
            : throw new TenantContextMissingException("Die Queue-Verwaltung ist ein Operator-Anwendungsfall im Plattformkontext (Backend 5.3).");
    }
}
