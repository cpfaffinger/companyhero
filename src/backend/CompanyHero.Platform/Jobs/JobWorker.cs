using CompanyHero.Platform.Data;
using CompanyHero.Platform.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CompanyHero.Platform.Jobs;

/// <summary>
/// Der Worker-Prozess (Backend 3.1, 6.3, A-006): beansprucht Jobs mit Lease und Fairness, führt jeden Job in eigenem
/// Tenant-Kontext und eigener Kontexttransaktion aus und bestätigt ihn in derselben Transaktion wie seine Wirkung.
/// Fehlversuche warten mit wachsender Wartezeit; nach dem letzten Versuch Dead-Letter. Geordnetes Beenden gibt laufende
/// Leases frei; ein abgestürzter Worker hinterlässt Leases, die nach Ablauf erneut beansprucht werden.
/// </summary>
internal sealed class JobWorker(
    JobStore store,
    JobHandlerRegistry handlers,
    ScheduleRunner schedules,
    ITenantScopeFactory scopes,
    JobMetrics metrics,
    IOptions<JobWorkerOptions> options,
    ILogger<JobWorker> logger) : BackgroundService
{
    private readonly JobWorkerOptions _options = options.Value;

    public string WorkerId => _options.WorkerId;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Worker {WorkerId} gestartet; Jobtypen: {JobTypes}", WorkerId, string.Join(", ", handlers.JobTypes));
        await RegisterSchedulesWithRetryAsync(stoppingToken);

        var running = new HashSet<Task>();
        var lastSample = DateTimeOffset.MinValue;
        var lastSchedule = DateTimeOffset.MinValue;

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                running.RemoveWhere(t => t.IsCompleted);
                var claimed = await ClaimSafelyAsync(_options.MaxParallel - running.Count, stoppingToken);
                foreach (var job in claimed)
                {
                    running.Add(RunAsync(job, stoppingToken));
                }

                var now = DateTimeOffset.UtcNow;
                if (now - lastSchedule >= _options.ScheduleInterval)
                {
                    lastSchedule = now;
                    await RunSchedulesSafelyAsync(stoppingToken);
                }

                if (now - lastSample >= _options.MetricsInterval)
                {
                    lastSample = now;
                    await SampleSafelyAsync(stoppingToken);
                }

                if (claimed.Count == 0 || running.Count >= _options.MaxParallel)
                {
                    await WaitAsync(running, stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // geordnetes Beenden
        }
        finally
        {
            await Task.WhenAll(running.Select(t => t.ContinueWith(_ => { }, TaskScheduler.Default)));
            logger.LogInformation("Worker {WorkerId} beendet", WorkerId);
        }
    }

    private async Task WaitAsync(HashSet<Task> running, CancellationToken stoppingToken)
    {
        var delay = Task.Delay(_options.PollInterval, stoppingToken);
        if (running.Count == 0)
        {
            await delay.ContinueWith(_ => { }, TaskScheduler.Default);
            return;
        }

        await Task.WhenAny(running.Append(delay)).ContinueWith(_ => { }, TaskScheduler.Default);
    }

    private async Task<IReadOnlyList<ClaimedJob>> ClaimSafelyAsync(int free, CancellationToken stoppingToken)
    {
        try
        {
            return await store.ClaimAsync(free, _options.MaxConcurrentPerTenant, _options.LeaseDuration, WorkerId, stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Beanspruchung fehlgeschlagen; nächster Versuch nach dem Poll-Intervall");
            return [];
        }
    }

    /// <summary>Beim Start kann die Datenbank noch hochfahren; der Worker wartet, statt den Prozess zu beenden.</summary>
    private async Task RegisterSchedulesWithRetryAsync(CancellationToken stoppingToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await schedules.EnsureRegisteredAsync(stoppingToken);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Zeitpläne noch nicht registrierbar (Versuch {Attempt}); nächster Versuch in 2 s", attempt);
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }
    }

    private async Task RunSchedulesSafelyAsync(CancellationToken stoppingToken)
    {
        try
        {
            await schedules.RunDueAsync(stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Prüfung der zeitgesteuerten Aufgaben fehlgeschlagen");
        }
    }

    private async Task SampleSafelyAsync(CancellationToken stoppingToken)
    {
        try
        {
            metrics.Update(await store.SampleAsync(stoppingToken));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Stichprobe der Queue-Metriken fehlgeschlagen");
        }
    }

    private async Task RunAsync(ClaimedJob job, CancellationToken stoppingToken)
    {
        await Task.Yield();
        var context = job.TenantId is { } tenant ? TenantContext.ForTenant(tenant) : TenantContext.ForPlatform();
        var execution = new JobExecution(job.Id, job.TenantId, job.JobType, job.Reference, job.Attempt, job.MaxAttempts);

        try
        {
            await scopes.RunAsync(context, async (sp, ct) =>
            {
                var transaction = sp.GetRequiredService<ContextTransaction>();
                await using var scope = await transaction.BeginAsync(ct);
                await handlers.Resolve(sp, job.JobType).HandleAsync(execution, ct);
                if (!await store.ConfirmWithinAsync(transaction, job, WorkerId, ct))
                {
                    throw new JobLeaseLostException();
                }

                await scope.CommitAsync(ct);
            }, stoppingToken);

            metrics.Record("succeeded");
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            await ReleaseSafelyAsync(job);
            metrics.Record("released");
        }
        catch (JobLeaseLostException ex)
        {
            logger.LogWarning("Job {JobId} ({JobType}): {Message}", job.Id, job.JobType, ex.Message);
            metrics.Record("lease_lost");
        }
        catch (Exception ex)
        {
            await FailSafelyAsync(job, ex);
        }
    }

    private async Task ReleaseSafelyAsync(ClaimedJob job)
    {
        try
        {
            await store.ReleaseAsync(job, WorkerId, CancellationToken.None);
            logger.LogInformation("Job {JobId} ({JobType}) beim Beenden freigegeben", job.Id, job.JobType);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Job {JobId} konnte beim Beenden nicht freigegeben werden; der Lease läuft aus", job.Id);
        }
    }

    private async Task FailSafelyAsync(ClaimedJob job, Exception exception)
    {
        var error = $"{exception.GetType().Name}: {Truncate(exception.Message)}";
        var backoff = TimeSpan.FromTicks(Math.Min(_options.MaxBackoff.Ticks, _options.BaseBackoff.Ticks * (1L << Math.Min(job.Attempt - 1, 20))));
        try
        {
            var status = await store.FailAsync(job, WorkerId, error, backoff, CancellationToken.None);
            if (status == JobStatus.Dead)
            {
                logger.LogError(exception, "Job {JobId} ({JobType}) nach {Attempts} Versuchen im Dead-Letter-Status", job.Id, job.JobType, job.Attempt);
                metrics.Record("dead");
            }
            else
            {
                logger.LogWarning(exception, "Job {JobId} ({JobType}) Versuch {Attempt}/{Max} fehlgeschlagen; erneut in {Backoff}", job.Id, job.JobType, job.Attempt, job.MaxAttempts, backoff);
                metrics.Record("failed");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Fehlversuch von Job {JobId} konnte nicht vermerkt werden; der Lease läuft aus", job.Id);
        }
    }

    private static string Truncate(string message) => message.Length <= 400 ? message : message[..400];
}

public static class JobWorkerExtensions
{
    /// <summary>Queue-Bausteine für API und Worker: Einreihung, Verwaltung, Registrierung von Handlern und Aufgaben, Metriken.</summary>
    public static IServiceCollection AddJobQueue(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddOptions<JobWorkerOptions>().BindConfiguration(JobWorkerOptions.SectionName);
        services.AddScoped<IJobQueue, JobQueue>();
        services.AddScoped<IJobAdministration, JobAdministration>();
        services.AddSingleton<JobStore>();
        services.AddSingleton<JobMetrics>();
        services.AddSingleton(sp => new JobHandlerRegistry(sp.GetServices<JobHandlerRegistration>()));
        services.AddSingleton(sp => sp.GetServices<ScheduledTaskRegistration>().ToList() as IReadOnlyList<ScheduledTaskRegistration>);
        services.AddSingleton<ScheduleRunner>();
        services.AddScheduledTask<JobCleanupTask>();
        return services;
    }

    /// <summary>Nur der Worker-Host: verarbeitet Jobs und zeitgesteuerte Aufgaben.</summary>
    public static IServiceCollection AddJobWorker(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddHostedService<JobWorker>();
        return services;
    }
}
