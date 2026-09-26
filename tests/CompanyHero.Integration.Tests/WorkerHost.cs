using System.Collections.Concurrent;
using CompanyHero.ModuleCatalog;
using CompanyHero.Modules.Challenges.Application;
using CompanyHero.Modules.Challenges.Domain;
using CompanyHero.Modules.Notifications.Application;
using CompanyHero.Modules.Progress.Application;
using CompanyHero.Modules.Progress.Domain;
using CompanyHero.Modules.Progress.Infrastructure;
using CompanyHero.Platform.Data;
using CompanyHero.Platform.Events;
using CompanyHero.Platform.Hosting;
using CompanyHero.Platform.Jobs;
using CompanyHero.Platform.Tenancy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CompanyHero.Integration.Tests;

/// <summary>
/// Steuerung der Testhandler von außen: Blockieren an einem Tor, Fehlschlagen erzwingen, Reihenfolge und Läufe beobachten.
/// Ein Exemplar je Test; Worker-Hosts erhalten es als Singleton.
/// </summary>
public sealed class TestJobControl
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _gates = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> _entries = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, bool> _failing = new(StringComparer.Ordinal);

    public ConcurrentQueue<(TenantId Tenant, JobId Job, string Reference)> Order { get; } = new();

    public ConcurrentQueue<DateTimeOffset> ScheduledRuns { get; } = new();

    public PersonId SubscriberPerson { get; set; }

    public void Block(string reference) => _gates[reference] = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Release(string reference)
    {
        if (_gates.TryRemove(reference, out var gate))
        {
            gate.TrySetResult();
        }
    }

    public void Fail(string reference, bool failing = true) => _failing[reference] = failing;

    public int Entries(string reference) => _entries.GetValueOrDefault(reference);

    internal async Task EnterAsync(string reference, CancellationToken cancellationToken)
    {
        _entries.AddOrUpdate(reference, 1, (_, n) => n + 1);
        if (_failing.GetValueOrDefault(reference))
        {
            throw new InvalidOperationException("Simulierter Fehler des Handlers (technisch, ohne Personenbezug).");
        }

        if (_gates.TryGetValue(reference, out var gate))
        {
            await gate.Task.WaitAsync(cancellationToken);
        }
    }

    public static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (!condition())
        {
            if (DateTimeOffset.UtcNow > deadline)
            {
                throw new TimeoutException("Bedingung nicht innerhalb der Wartezeit erfüllt.");
            }

            await Task.Delay(25, cancellationToken);
        }
    }
}

/// <summary>Testjob mit sichtbarer Wirkung in einer Modultabelle unter RLS: ein Aktivitätsereignis der referenzierten Person.</summary>
internal sealed class EffectJobHandler(TestJobControl control, ProgressDbContext progress, IContextTransaction transaction, ITenantContextAccessor context, TimeProvider clock) : IJobHandler
{
    public static string JobType => "test.effect";

    public static string KindFor(JobId job) => $"test.effect:{job}";

    public async Task HandleAsync(JobExecution job, CancellationToken cancellationToken)
    {
        var person = new PersonId(Guid.ParseExact(job.Reference.Split('#')[0], "D"));
        // Wirkung zuerst, dann blockieren: ein abgebrochener Versuch darf die Wirkung nicht hinterlassen. Die Wirkung ist eine
        // eigene Zeile ohne gemeinsam gesperrte Zeile, damit zwei Worker denselben Job wirklich gleichzeitig versuchen können.
        var now = clock.GetUtcNow();
        await using (var tx = await transaction.BeginAsync(cancellationToken))
        {
            progress.ActivityEvents.Add(ActivityEvent.Record(context.Require().RequireTenant(), person, KindFor(job.Id), ActivitySource.Platform, now, TenantTimeZone.DayOf(now), 0));
            await progress.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }

        control.Order.Enqueue((context.Require().RequireTenant(), job.Id, job.Reference));
        await control.EnterAsync(job.Reference, cancellationToken);
    }
}

/// <summary>Abonnent des Ereignisses „Beitrag erfasst“ aus einem fremden Modul: liest über die Schnittstelle von Challenges.</summary>
internal sealed class SubscriberJobHandler(TestJobControl control, IChallengeEvents events, IActivityRecorder activities, TimeProvider clock) : IJobHandler
{
    public static string JobType => "test.subscriber";

    public const string Kind = "test.subscriber";

    public async Task HandleAsync(JobExecution job, CancellationToken cancellationToken)
    {
        var payload = await events.GetContributionRecordedAsync(Guid.ParseExact(job.Reference, "D"), cancellationToken)
            ?? throw new InvalidOperationException("Ereignis im Kontext des Tenants nicht lesbar.");
        await activities.RecordAsync(control.SubscriberPerson, $"{Kind}:{payload.ContributionId:D}"[..Math.Min(60, Kind.Length + 37)], ActivitySource.Platform, clock.GetUtcNow(), cancellationToken);
    }
}

internal sealed class TickScheduledTask(TestJobControl control, ITenantContextAccessor context) : IScheduledTask
{
    public static string Name => "test.tick";

    public static TimeSpan Interval => TimeSpan.FromSeconds(1);

    public Task RunAsync(CancellationToken cancellationToken)
    {
        if (context.Require().Kind != TenantContextKind.Platform)
        {
            throw new InvalidOperationException("Zeitgesteuerte Aufgaben laufen im Plattformkontext.");
        }

        control.ScheduledRuns.Enqueue(DateTimeOffset.UtcNow);
        return Task.CompletedTask;
    }
}

/// <summary>Worker-Replikat als eigener Host gegen dieselbe Datenbank (Backend 3.1: unabhängig replizierbar).</summary>
internal static class WorkerHost
{
    public static IHost Create(PostgresFixture pg, TestJobControl control, string workerId, Action<JobWorkerOptions>? configure = null, string? connectionString = null)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = connectionString ?? pg.AppConnectionString,
            ["Identity:PublicOrigin"] = PostgresFixture.Origin,
            ["Notifications:Vapid:PrivateKeyPem"] = PostgresFixture.VapidPem,
            ["Notifications:Vapid:Subject"] = "mailto:betrieb@localhost",
        });
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.AddPlatformData(builder.Configuration);
        foreach (var module in AllModules.Create())
        {
            module.AddModule(builder.Services, builder.Configuration);
        }

        builder.Services.AddSingleton(control);
        builder.Services.Replace(ServiceDescriptor.Singleton<IWebPushTransport>(pg.Push));
        builder.Services.Replace(ServiceDescriptor.Singleton<IMailTransport>(pg.Mail));
        builder.Services.AddJobHandler<EffectJobHandler>();
        builder.Services.AddEventSubscription<SubscriberJobHandler>(ChallengeEventTypes.ContributionRecorded);
        builder.Services.AddScheduledTask<TickScheduledTask>();
        builder.Services.AddJobWorker();
        builder.Services.PostConfigure<JobWorkerOptions>(o =>
        {
            o.WorkerId = workerId;
            o.PollInterval = TimeSpan.FromMilliseconds(50);
            o.ScheduleInterval = TimeSpan.FromMilliseconds(100);
            o.MetricsInterval = TimeSpan.FromMilliseconds(200);
            o.BaseBackoff = TimeSpan.FromMilliseconds(50);
            o.MaxBackoff = TimeSpan.FromMilliseconds(200);
            configure?.Invoke(o);
        });
        return builder.Build();
    }

    /// <summary>Führt eine zeitgesteuerte Aufgabe des Worker-Hosts sofort im Plattformkontext aus (Backend 6.2), unabhängig von ihrer Fälligkeit.</summary>
    public static Task RunScheduledTaskAsync(IHost host, string name, CancellationToken cancellationToken)
    {
        var registration = host.Services.GetServices<ScheduledTaskRegistration>().Single(r => r.Name == name);
        return host.Services.GetRequiredService<ITenantScopeFactory>().RunAsync(TenantContext.ForPlatform(), (sp, ct) => ((IScheduledTask)sp.GetRequiredService(registration.TaskType)).RunAsync(ct), cancellationToken);
    }
}

/// <summary>Push-Dienst der Tests: speichert jede Nachricht und antwortet je Endpunkt mit dem eingestellten Status (Voreinstellung 201).</summary>
public sealed class CapturingPushTransport : IWebPushTransport
{
    private readonly ConcurrentDictionary<string, int> _status = new(StringComparer.Ordinal);

    public ConcurrentQueue<PushMessage> Sent { get; } = new();

    public void Respond(string endpoint, int status) => _status[endpoint] = status;

    public Task<PushSendResult> SendAsync(PushMessage message, CancellationToken cancellationToken)
    {
        Sent.Enqueue(message);
        return Task.FromResult(new PushSendResult(_status.GetValueOrDefault(message.Endpoint.ToString(), 201)));
    }
}

/// <summary>SMTP-Transport der Tests: konfiguriert, speichert Umschläge, kann Unzustellbarkeit simulieren.</summary>
public sealed class CapturingMailTransport : IMailTransport
{
    public ConcurrentQueue<MailEnvelope> Sent { get; } = new();

    public bool Configured => true;

    public Task SendAsync(MailEnvelope envelope, CancellationToken cancellationToken)
    {
        Sent.Enqueue(envelope);
        return Task.CompletedTask;
    }
}
