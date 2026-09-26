namespace CompanyHero.Platform.Jobs;

/// <summary>Einstellungen des Worker-Prozesses (Backend 6.3); Konfigurationsabschnitt <c>Jobs</c>.</summary>
public sealed class JobWorkerOptions
{
    public const string SectionName = "Jobs";

    /// <summary>Kennung dieses Worker-Replikats; Voreinstellung Maschinenname plus Prozess-ID.</summary>
    public string WorkerId { get; set; } = $"{Environment.MachineName}-{Environment.ProcessId}";

    /// <summary>Abstand zwischen zwei Beanspruchungen, wenn nichts wartet.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Gleichzeitig laufende Jobs je Worker-Replikat.</summary>
    public int MaxParallel { get; set; } = 8;

    /// <summary>Gleichzeitig laufende Jobs je Tenant über alle Replikate (Fairness, A-006).</summary>
    public int MaxConcurrentPerTenant { get; set; } = 4;

    /// <summary>Dauer eines Lease; ein Job, der länger läuft, gilt als verloren und wird erneut beansprucht.</summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Versuche bis zum Dead-Letter-Status, wenn der Auftrag nichts anderes vorgibt.</summary>
    public int DefaultMaxAttempts { get; set; } = 5;

    /// <summary>Wartezeit vor dem zweiten Versuch; verdoppelt sich je Versuch bis <see cref="MaxBackoff"/>.</summary>
    public TimeSpan BaseBackoff { get; set; } = TimeSpan.FromSeconds(5);

    public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Abstand der Stichproben für Warteschlangenlänge, Alter des ältesten Jobs und Dead-Letter-Bestand.</summary>
    public TimeSpan MetricsInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>Abstand der Prüfung, ob zeitgesteuerte Aufgaben fällig sind.</summary>
    public TimeSpan ScheduleInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Aufbewahrung erfolgreicher Jobs vor dem Aufräumlauf (A-108 Folgen).</summary>
    public TimeSpan SucceededRetention { get; set; } = TimeSpan.FromDays(7);
}
