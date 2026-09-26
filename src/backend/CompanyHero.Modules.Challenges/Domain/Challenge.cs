using CompanyHero.Platform.Tenancy;

namespace CompanyHero.Modules.Challenges.Domain;

/// <summary>Erfassungsart der Challenge (Challenges 2.1, A-038): im Durchstich Häkchen und Zahl.</summary>
public enum ChallengeMetric
{
    /// <summary>Häkchen: genau ein Beitrag mit Wert 1 je Erfassung.</summary>
    Checkmark = 1,

    /// <summary>Zahl: positiver Wert mit bis zu vier Nachkommastellen.</summary>
    Count = 2,
}

/// <summary>Lebenszyklus (Challenges 4.1, A-040); der Durchstich legt Challenges direkt laufend an.</summary>
public enum ChallengeState
{
    Draft = 1,
    Planned = 2,
    Running = 3,
    Ended = 4,
    Archived = 5,
}

/// <summary>Challenge eines Tenants mit Sammelziel (Challenges 2, A-038).</summary>
public sealed class Challenge : ITenantOwned
{
    private Challenge(TenantId tenantId, Guid id, string title, ChallengeMetric metric, decimal target, ChallengeState state, DateTimeOffset startsAt, DateTimeOffset endsAt, DateTimeOffset createdAt)
    {
        TenantId = tenantId;
        Id = id;
        Title = title;
        Metric = metric;
        Target = target;
        State = state;
        StartsAt = startsAt;
        EndsAt = endsAt;
        CreatedAt = createdAt;
    }

    public TenantId TenantId { get; }

    public Guid Id { get; }

    public string Title { get; private set; }

    public ChallengeMetric Metric { get; }

    /// <summary>Sammelziel (Challenges 2.1 Achse 6, Standardform): Zielwert des Kollektivs mit vier Nachkommastellen.</summary>
    public decimal Target { get; }

    public ChallengeState State { get; private set; }

    public DateTimeOffset StartsAt { get; }

    public DateTimeOffset EndsAt { get; }

    public DateTimeOffset CreatedAt { get; }

    /// <summary>Nachfrist: 48 Stunden nach Ende werden Beiträge mit Erfassungszeitpunkt im Zeitraum noch angenommen (Challenges 3, A-039).</summary>
    public static TimeSpan GracePeriod { get; } = TimeSpan.FromHours(48);

    public DateTimeOffset AcceptsContributionsUntil => EndsAt.Add(GracePeriod);

    public static Challenge StartRunning(TenantId tenantId, string title, ChallengeMetric metric, decimal target, DateTimeOffset startsAt, DateTimeOffset endsAt, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        if (endsAt <= startsAt)
        {
            throw new ArgumentException("Das Ende liegt nach dem Beginn.", nameof(endsAt));
        }

        if (target <= 0m)
        {
            throw new ArgumentException("Das Sammelziel ist positiv.", nameof(target));
        }

        return new Challenge(tenantId, Guid.CreateVersion7(), title.Trim(), metric, decimal.Round(target, 4, MidpointRounding.ToEven), ChallengeState.Running, startsAt.ToUniversalTime(), endsAt.ToUniversalTime(), now.ToUniversalTime());
    }

    /// <summary>Kollektivfortschritt in ganzen Prozent, gedeckelt bei 100 (Challenges 6.2: gerundeter Prozentwert).</summary>
    public int PercentOf(decimal total) => (int)Math.Min(100m, Math.Round(total / Target * 100m, 0, MidpointRounding.AwayFromZero));
}
