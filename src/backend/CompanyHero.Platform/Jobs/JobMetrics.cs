using System.Diagnostics.Metrics;

namespace CompanyHero.Platform.Jobs;

/// <summary>
/// Metriken der Queue (Backend 6.3): Warteschlangenlänge, Alter des ältesten wartenden Jobs, Fehlerquote über die
/// Zähler je Ausgang, Dead-Letter-Bestand. Die Alarmregeln der Beobachtung verwenden diese Namen.
/// </summary>
public sealed class JobMetrics : IDisposable
{
    public const string MeterName = "CompanyHero.Jobs";

    private readonly Meter _meter = new(MeterName);
    private readonly Counter<long> _processed;
    private long _queued;
    private double _oldestPendingAgeSeconds;
    private long _deadLetters;

    public JobMetrics()
    {
        _processed = _meter.CreateCounter<long>("companyhero.jobs.processed", description: "Verarbeitete Jobs je Ausgang (succeeded, failed, dead, released, lease_lost)");
        _meter.CreateObservableGauge("companyhero.jobs.queued", () => Interlocked.Read(ref _queued), description: "Wartende Jobs einschließlich abgelaufener Leases");
        _meter.CreateObservableGauge("companyhero.jobs.oldest_pending_age_seconds", () => Volatile.Read(ref _oldestPendingAgeSeconds), description: "Alter des ältesten wartenden Jobs in Sekunden");
        _meter.CreateObservableGauge("companyhero.jobs.dead_letter", () => Interlocked.Read(ref _deadLetters), description: "Jobs im Dead-Letter-Status");
    }

    public void Record(string outcome) => _processed.Add(1, new KeyValuePair<string, object?>("outcome", outcome));

    internal void Update(QueueSample sample)
    {
        Interlocked.Exchange(ref _queued, sample.Queued);
        Volatile.Write(ref _oldestPendingAgeSeconds, sample.OldestPendingAgeSeconds);
        Interlocked.Exchange(ref _deadLetters, sample.DeadLetters);
    }

    public void Dispose() => _meter.Dispose();
}
