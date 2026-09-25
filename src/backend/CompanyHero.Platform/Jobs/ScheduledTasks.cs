using CompanyHero.Platform.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace CompanyHero.Platform.Jobs;

/// <summary>
/// Zeitgesteuerte Aufgabe (Backend 6.2 <c>job_schedule</c>, 6.3): läuft im Plattformkontext in eigenem Scope und wird
/// über die Zeile in <c>platform.job_schedule</c> gegen parallele Doppelausführung gesperrt. Tenantbezogene Arbeit
/// reiht die Aufgabe als Jobs je Tenant ein, statt sie selbst im Plattformkontext auszuführen.
/// </summary>
public interface IScheduledTask
{
    static abstract string Name { get; }

    static abstract TimeSpan Interval { get; }

    Task RunAsync(CancellationToken cancellationToken);
}

public sealed record ScheduledTaskRegistration(string Name, TimeSpan Interval, Type TaskType);

/// <summary>
/// Führt fällige Aufgaben aus. Die Zeile der Aufgabe wird mit <c>FOR UPDATE SKIP LOCKED</c> gesperrt und erst nach dem
/// Lauf mit neuem Fälligkeitszeitpunkt freigegeben; ein zweites Replikat überspringt eine gesperrte oder nicht fällige Aufgabe.
/// </summary>
internal sealed class ScheduleRunner(NpgsqlDataSource dataSource, IReadOnlyList<ScheduledTaskRegistration> tasks, ITenantScopeFactory scopes, TimeProvider clock, ILogger<ScheduleRunner> logger)
{
    public async Task EnsureRegisteredAsync(CancellationToken cancellationToken)
    {
        if (tasks.Count == 0)
        {
            return;
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        foreach (var task in tasks)
        {
            await using var command = new NpgsqlCommand(
                "insert into platform.job_schedule (name, interval_seconds, next_run_at) values ($1, $2, $3) on conflict (name) do update set interval_seconds = excluded.interval_seconds",
                connection);
            command.Parameters.Add(new NpgsqlParameter { Value = task.Name });
            command.Parameters.Add(new NpgsqlParameter { Value = (int)task.Interval.TotalSeconds });
            command.Parameters.Add(new NpgsqlParameter { Value = clock.GetUtcNow() });
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public async Task RunDueAsync(CancellationToken cancellationToken)
    {
        foreach (var task in tasks)
        {
            await RunIfDueAsync(task, cancellationToken);
        }
    }

    private async Task RunIfDueAsync(ScheduledTaskRegistration task, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var lockTransaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var due = new NpgsqlCommand("select 1 from platform.job_schedule where name = $1 and next_run_at <= $2 for update skip locked", connection, lockTransaction))
        {
            due.Parameters.Add(new NpgsqlParameter { Value = task.Name });
            due.Parameters.Add(new NpgsqlParameter { Value = clock.GetUtcNow() });
            if (await due.ExecuteScalarAsync(cancellationToken) is null)
            {
                return;
            }
        }

        var started = clock.GetUtcNow();
        string? error = null;
        try
        {
            await scopes.RunAsync(TenantContext.ForPlatform(), (sp, ct) => ((IScheduledTask)sp.GetRequiredService(task.TaskType)).RunAsync(ct), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            error = $"{ex.GetType().Name}: {Truncate(ex.Message)}";
            logger.LogError(ex, "Zeitgesteuerte Aufgabe {Task} fehlgeschlagen", task.Name);
        }

        var finished = clock.GetUtcNow();
        await using (var update = new NpgsqlCommand(
            "update platform.job_schedule set last_run_at = $2, next_run_at = $3, last_duration_ms = $4, last_error = $5 where name = $1", connection, lockTransaction))
        {
            update.Parameters.Add(new NpgsqlParameter { Value = task.Name });
            update.Parameters.Add(new NpgsqlParameter { Value = started });
            update.Parameters.Add(new NpgsqlParameter { Value = finished.Add(task.Interval) });
            update.Parameters.Add(new NpgsqlParameter { Value = (int)(finished - started).TotalMilliseconds });
            update.Parameters.Add(new NpgsqlParameter<string?> { TypedValue = error, NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Varchar });
            await update.ExecuteNonQueryAsync(CancellationToken.None);
        }

        await lockTransaction.CommitAsync(CancellationToken.None);
    }

    private static string Truncate(string message) => message.Length <= 400 ? message : message[..400];
}
