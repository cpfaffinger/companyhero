namespace CompanyHero.Platform.Metering;

/// <summary>Quelle eines Metering-Ereignisses (Metering 2.1).</summary>
public enum MeteringSource
{
    Self = 1,
    Platform = 2,
    Automatic = 3,
    Administration = 4,
}

/// <summary>
/// Ein Metering-Ereignis aus Sicht der emittierenden Domäne (Metering 2.1, 3.3): anonymer Bezug, nie eine Personen-ID;
/// Idempotenzschlüssel aus Fachereignis-ID und Metrik. Menge mit vier Nachkommastellen.
/// </summary>
/// <param name="Module">Kern oder M1 bis M12.</param>
/// <param name="Metric">Metrik aus dem Katalog (Metering 3.1), etwa <c>challenge.participant_day</c>.</param>
/// <param name="SubjectRef">Anonymer Bezug: Objektkennung (Challenge, Inhalt, Gerät) oder Zähler-Slot.</param>
/// <param name="Quantity">Menge; negative Menge ist eine Gegenbuchung.</param>
/// <param name="OccurredAt">Fachlicher Zeitpunkt.</param>
/// <param name="IdempotencyKey">Eindeutig je Tenant; verhindert Doppelzählung bei Wiederholungen.</param>
public sealed record MeteringEmission(string Module, string Metric, string SubjectRef, decimal Quantity, MeteringSource Source, DateTimeOffset OccurredAt, string IdempotencyKey);

public enum EmissionOutcome
{
    Recorded = 1,

    /// <summary>Ereignis mit diesem Idempotenzschlüssel liegt bereits im Ledger.</summary>
    Duplicate = 2,
}

/// <summary>
/// Querschnitt „Metering-Emission“ (Domänenkarte 3): Eigentümer ist Metering und Abrechnung, verwendet wird die
/// Schnittstelle von allen Domänen. Die Emission läuft in derselben Kontexttransaktion wie die fachliche Änderung
/// (A-006, Metering 3.3); außerhalb einer laufenden Transaktion wird sie abgelehnt.
/// </summary>
public interface IMeteringEmitter
{
    Task<EmissionOutcome> EmitAsync(MeteringEmission emission, CancellationToken cancellationToken);
}
