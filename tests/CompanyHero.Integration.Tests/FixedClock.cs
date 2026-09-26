namespace CompanyHero.Integration.Tests;

/// <summary>Feste Testuhr (Backend 9): reproduzierbare Zeitstempel in Testdaten; Sitzungs- und Sperrtests springen und stellen zurück.</summary>
public sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public static DateTimeOffset Start { get; } = new(2026, 9, 25, 8, 0, 0, TimeSpan.Zero);

    private DateTimeOffset _now = now;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now = _now.Add(by);

    public void Set(DateTimeOffset at) => _now = at;
}
