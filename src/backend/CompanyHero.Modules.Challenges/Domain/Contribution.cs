using CompanyHero.Platform.Tenancy;

namespace CompanyHero.Modules.Challenges.Domain;

/// <summary>Erfassungskanal; bestimmt das Idempotenzregime (A-005, A-009): Handy mit Client-Idempotenzschlüssel, Kiosk mit Vorgangskennung.</summary>
public enum ContributionChannel
{
    Mobile = 1,
    Kiosk = 2,
}

/// <summary>Ein gültiger Beitrag einer Person zu einer Challenge (Challenges 3, A-039).</summary>
public sealed class Contribution : ITenantOwned
{
    private Contribution(TenantId tenantId, Guid id, Guid challengeId, PersonId personId, decimal value, DateTimeOffset recordedAt, DateTimeOffset receivedAt, ContributionChannel channel)
    {
        TenantId = tenantId;
        Id = id;
        ChallengeId = challengeId;
        PersonId = personId;
        Value = value;
        RecordedAt = recordedAt;
        ReceivedAt = receivedAt;
        Channel = channel;
    }

    public TenantId TenantId { get; }

    public Guid Id { get; }

    public Guid ChallengeId { get; }

    public PersonId PersonId { get; }

    /// <summary>Wert mit vier Nachkommastellen; Häkchen tragen 1.</summary>
    public decimal Value { get; }

    /// <summary>Erfassungszeitpunkt auf dem Gerät; gewertet in der Zeitzone des Tenants.</summary>
    public DateTimeOffset RecordedAt { get; }

    /// <summary>Eingang beim Server.</summary>
    public DateTimeOffset ReceivedAt { get; }

    public ContributionChannel Channel { get; }

    public static Contribution Record(TenantId tenantId, Guid challengeId, PersonId personId, decimal value, DateTimeOffset recordedAt, DateTimeOffset receivedAt, ContributionChannel channel) =>
        new(tenantId, Guid.CreateVersion7(), challengeId, personId, decimal.Round(value, 4, MidpointRounding.ToEven), recordedAt.ToUniversalTime(), receivedAt.ToUniversalTime(), channel);
}

/// <summary>Grund einer Ablehnung (Challenges 3).</summary>
public enum ContributionRejection
{
    None = 0,
    InFuture = 1,
    BackdatedTooFar = 2,
    OutsidePeriod = 3,
    GracePeriodOver = 4,
    ChallengeNotRunning = 5,
    InvalidValue = 6,
}

/// <summary>Reine Fachregeln der Erfassung (Challenges 3, A-039), ohne Infrastruktur.</summary>
public static class ContributionRules
{
    /// <summary>Rückdatierung bis zu drei Kalendertage in der Tenant-Zeitzone; nie in die Zukunft.</summary>
    public const int MaxBackdateDays = 3;

    public static ContributionRejection Validate(Challenge challenge, ChallengeMetric metric, decimal value, DateTimeOffset recordedAt, DateTimeOffset receivedAt)
    {
        ArgumentNullException.ThrowIfNull(challenge);
        if (challenge.State != ChallengeState.Running)
        {
            return ContributionRejection.ChallengeNotRunning;
        }

        if (metric == ChallengeMetric.Checkmark ? value != 1m : value <= 0m)
        {
            return ContributionRejection.InvalidValue;
        }

        if (recordedAt > receivedAt)
        {
            return ContributionRejection.InFuture;
        }

        var recordedDay = TenantTimeZone.DayOf(recordedAt);
        var receivedDay = TenantTimeZone.DayOf(receivedAt);
        if (receivedDay.DayNumber - recordedDay.DayNumber > MaxBackdateDays)
        {
            return ContributionRejection.BackdatedTooFar;
        }

        if (recordedAt < challenge.StartsAt || recordedAt > challenge.EndsAt)
        {
            return ContributionRejection.OutsidePeriod;
        }

        if (receivedAt > challenge.AcceptsContributionsUntil)
        {
            return ContributionRejection.GracePeriodOver;
        }

        return ContributionRejection.None;
    }
}
