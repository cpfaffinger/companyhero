using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace CompanyHero.Platform.Jobs;

/// <summary>
/// Aufräumlauf erfolgreicher Jobs (A-108 Folgen: erfolgreiche Jobs bleiben zur Nachvollziehbarkeit, ein Aufräumlauf
/// folgt mit dem Fachpfad). Entfernt bestätigte Jobs, die älter als die Aufbewahrung sind; Dead-Letter-Jobs bleiben,
/// weil ihr Replay eine bewusste Operator-Aktion ist (Backend 6.3).
/// </summary>
internal sealed class JobCleanupTask(NpgsqlDataSource dataSource, IOptions<JobWorkerOptions> options, TimeProvider clock, ILogger<JobCleanupTask> logger) : IScheduledTask
{
    public static string Name => "platform.jobs.cleanup";

    public static TimeSpan Interval => TimeSpan.FromHours(6);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var cutoff = clock.GetUtcNow() - options.Value.SucceededRetention;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("delete from platform.job where status = 3 and completed_at < $1", connection);
        command.Parameters.Add(new NpgsqlParameter { Value = cutoff });
        var removed = await command.ExecuteNonQueryAsync(cancellationToken);
        if (removed > 0)
        {
            logger.LogInformation("Aufräumlauf: {Count} erfolgreiche Jobs älter als {Retention} entfernt", removed, options.Value.SucceededRetention);
        }
    }
}
