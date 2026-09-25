using CompanyHero.Platform.Data;
using CompanyHero.Platform.Tenancy;
using Npgsql;

namespace CompanyHero.Platform.Jobs;

/// <summary>Ein beanspruchter Job.</summary>
internal sealed record ClaimedJob(JobId Id, TenantId? TenantId, string JobType, string Reference, int Attempt, int MaxAttempts);

internal sealed record QueueSample(long Queued, double OldestPendingAgeSeconds, long DeadLetters);

/// <summary>
/// SQL der logischen Queue auf <c>platform.job</c> (Backend 6.2, 6.3). Beanspruchung mit <c>FOR UPDATE SKIP LOCKED</c>
/// und Lease; abgelaufene Leases werden erneut beansprucht. Die Reihenfolge bedient Tenants abwechselnd: zuerst je
/// Tenant der älteste wartende Job, unter diesen der am längsten nicht bediente Tenant; die gleichzeitig laufenden Jobs
/// je Tenant sind begrenzt. Ein großer Tenant belegt nicht alle Worker (A-006 Fairness).
/// </summary>
internal sealed class JobStore(NpgsqlDataSource dataSource, TimeProvider clock)
{
    private const string ClaimSql =
        """
        with active as (
            select tenant_id, count(*) as n
            from platform.job
            where status = 2 and lease_until > $5
            group by tenant_id
        ),
        served as (
            select tenant_id, max(completed_at) as last_served_at
            from platform.job
            where status = 3
            group by tenant_id
        ),
        candidates as (
            select j.id, j.tenant_id,
                   row_number() over (partition by j.tenant_id order by j.run_at, j.id) as rank_in_tenant,
                   coalesce(a.n, 0) as active_in_tenant,
                   s.last_served_at
            from platform.job j
            left join active a on a.tenant_id is not distinct from j.tenant_id
            left join served s on s.tenant_id is not distinct from j.tenant_id
            where j.run_at <= $5 and (j.status = 1 or (j.status = 2 and j.lease_until <= $5))
        ),
        picked as (
            select id from candidates
            where rank_in_tenant + active_in_tenant <= $4
            order by rank_in_tenant, last_served_at nulls first, tenant_id nulls last, id
            limit $1
        ),
        locked as (
            select j.id from platform.job j
            where j.id in (select id from picked)
              and j.run_at <= $5 and (j.status = 1 or (j.status = 2 and j.lease_until <= $5))
            for update skip locked
        )
        update platform.job j
        set status = 2, lease_until = $2, attempts = j.attempts + 1, worker_id = $3
        from locked
        where j.id = locked.id
        returning j.id, j.tenant_id, j.job_type, j.reference, j.attempts, j.max_attempts
        """;

    public async Task<IReadOnlyList<ClaimedJob>> ClaimAsync(int batchSize, int maxConcurrentPerTenant, TimeSpan lease, string workerId, CancellationToken cancellationToken)
    {
        if (batchSize <= 0)
        {
            return [];
        }

        var now = clock.GetUtcNow();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(ClaimSql, connection);
        command.Parameters.Add(new NpgsqlParameter { Value = batchSize });
        command.Parameters.Add(new NpgsqlParameter { Value = now.Add(lease) });
        command.Parameters.Add(new NpgsqlParameter { Value = workerId });
        command.Parameters.Add(new NpgsqlParameter { Value = maxConcurrentPerTenant });
        command.Parameters.Add(new NpgsqlParameter { Value = now });

        var jobs = new List<ClaimedJob>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            jobs.Add(new ClaimedJob(
                new JobId(reader.GetGuid(0)),
                reader.IsDBNull(1) ? null : new TenantId(reader.GetGuid(1)),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt32(4),
                reader.GetInt32(5)));
        }

        return jobs;
    }

    /// <summary>
    /// Bestätigt den Job innerhalb der Kontexttransaktion des Handlers: Wirkung und Bestätigung werden gemeinsam
    /// dauerhaft (Backend 6.3 „Bestätigung erst nach dauerhafter Verarbeitung“). Falsch, wenn der Lease inzwischen
    /// einem anderen Worker gehört; dann wird die Transaktion verworfen und die Wirkung entsteht nicht doppelt.
    /// </summary>
    public async Task<bool> ConfirmWithinAsync(ContextTransaction transaction, ClaimedJob job, string workerId, CancellationToken cancellationToken)
    {
        var current = transaction.Current;
        await using var command = current.Connection!.CreateCommand();
        command.Transaction = current;
        // Lease-Token ist (Worker, Versuch): ein verspäteter Versuch desselben Workers bestätigt nicht den Versuch eines Nachfolgers.
        command.CommandText = "update platform.job set status = 3, completed_at = $3, lease_until = null where id = $1 and worker_id = $2 and attempts = $4 and status = 2";
        command.Parameters.Add(new NpgsqlParameter { Value = job.Id.Value });
        command.Parameters.Add(new NpgsqlParameter { Value = workerId });
        command.Parameters.Add(new NpgsqlParameter { Value = clock.GetUtcNow() });
        command.Parameters.Add(new NpgsqlParameter { Value = job.Attempt });
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    /// <summary>Fehlgeschlagener Versuch: erneut warten mit wachsender Wartezeit oder Dead-Letter nach dem letzten Versuch.</summary>
    public async Task<JobStatus> FailAsync(ClaimedJob job, string workerId, string error, TimeSpan backoff, CancellationToken cancellationToken)
    {
        var dead = job.Attempt >= job.MaxAttempts;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            dead
                ? "update platform.job set status = 4, lease_until = null, last_error = $3, completed_at = $4 where id = $1 and worker_id = $2 and attempts = $5 and status = 2"
                : "update platform.job set status = 1, lease_until = null, last_error = $3, run_at = $4 where id = $1 and worker_id = $2 and attempts = $5 and status = 2",
            connection);
        command.Parameters.Add(new NpgsqlParameter { Value = job.Id.Value });
        command.Parameters.Add(new NpgsqlParameter { Value = workerId });
        command.Parameters.Add(new NpgsqlParameter { Value = error });
        command.Parameters.Add(new NpgsqlParameter { Value = dead ? clock.GetUtcNow() : clock.GetUtcNow().Add(backoff) });
        command.Parameters.Add(new NpgsqlParameter { Value = job.Attempt });
        await command.ExecuteNonQueryAsync(cancellationToken);
        return dead ? JobStatus.Dead : JobStatus.Queued;
    }

    /// <summary>Geordnetes Beenden: der Versuch zählt nicht, der Job wartet sofort wieder.</summary>
    public async Task ReleaseAsync(ClaimedJob job, string workerId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "update platform.job set status = 1, lease_until = null, attempts = greatest(attempts - 1, 0), worker_id = null where id = $1 and worker_id = $2 and attempts = $3 and status = 2",
            connection);
        command.Parameters.Add(new NpgsqlParameter { Value = job.Id.Value });
        command.Parameters.Add(new NpgsqlParameter { Value = workerId });
        command.Parameters.Add(new NpgsqlParameter { Value = job.Attempt });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<QueueSample> SampleAsync(CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            select count(*) filter (where status = 1 or (status = 2 and lease_until <= $1)),
                   coalesce(extract(epoch from ($1 - min(run_at) filter (where (status = 1 or (status = 2 and lease_until <= $1)) and run_at <= $1))), 0),
                   count(*) filter (where status = 4)
            from platform.job
            """,
            connection);
        command.Parameters.Add(new NpgsqlParameter { Value = clock.GetUtcNow() });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new QueueSample(reader.GetInt64(0), Convert.ToDouble(reader.GetValue(1), System.Globalization.CultureInfo.InvariantCulture), reader.GetInt64(2));
    }
}
