using CompanyHero.Platform.Tenancy;

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
/// <param name="ReversalOf">Gegenbuchung (Metering 2.2, 3.3): Idempotenzschlüssel des Ereignisses, das zurückgenommen wird; sonst <c>null</c>.</param>
public sealed record MeteringEmission(string Module, string Metric, string SubjectRef, decimal Quantity, MeteringSource Source, DateTimeOffset OccurredAt, string IdempotencyKey, string? ReversalOf = null);

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

/// <summary>
/// Zähler-Slot des aktiven Mitglieds (Metering 2.3, A-069): schlüsselabhängiger Hash aus Personenkennung und dem
/// Periodensalz des Tenants. Eigentümer ist Metering; Fortschritt verwendet den Slot als anonymen Bezug für
/// <c>member.active_month</c> und <c>member.active_day</c>. Innerhalb der Periode liefert dieselbe Person denselben Slot,
/// über Perioden hinweg sind Slots nicht verkettbar; nach Vernichtung des Salzes ist keine Rückrechnung möglich.
/// Für eine bereits versiegelte fachliche Periode gilt der Slot der offenen Buchungsperiode (Nachlauf, Metering 2.2).
/// </summary>
public interface IMeteringSlots
{
    Task<string> SlotForAsync(PersonId personId, DateTimeOffset occurredAt, CancellationToken cancellationToken);
}
