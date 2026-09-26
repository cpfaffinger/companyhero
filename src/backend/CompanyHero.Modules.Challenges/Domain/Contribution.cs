using CompanyHero.Platform.Tenancy;

namespace CompanyHero.Modules.Challenges.Domain;

/// <summary>Erfassungskanal; bestimmt das Idempotenzregime (A-005, A-009): Handy mit Client-Idempotenzschlüssel, Kiosk mit Vorgangskennung.</summary>
public enum ContributionChannel
{
    Mobile = 1,
    Kiosk = 2,
}

/// <summary>
/// Ein gültiger Beitrag einer Person zu einer Challenge (Challenges 3, A-039). Er trägt die Gruppen der Person zum
/// Beitragszeitpunkt (Organisation 3.3) und den Verweis auf sein Aktivitätsereignis. Eine Korrektur ist eine Gegenbuchung
/// mit negativem Wert; der ursprüngliche Beitrag bleibt als Beleg erhalten.
/// </summary>
public sealed class Contribution : ITenantOwned
{
    private Contribution(TenantId tenantId, Guid id, Guid challengeId, PersonId personId, decimal value, DateTimeOffset recordedAt, DateTimeOffset receivedAt, ContributionChannel channel, IReadOnlyList<Guid> groupIds, Guid? activityEventId, Guid? reversalOf)
    {
        TenantId = tenantId;
        Id = id;
        ChallengeId = challengeId;
        PersonId = personId;
        Value = value;
        RecordedAt = recordedAt;
        ReceivedAt = receivedAt;
        Channel = channel;
        GroupIds = groupIds;
        ActivityEventId = activityEventId;
        ReversalOf = reversalOf;
    }

    public TenantId TenantId { get; }

    public Guid Id { get; }

    public Guid ChallengeId { get; }

    public PersonId PersonId { get; private set; }

    /// <summary>Wert mit vier Nachkommastellen; Häkchen tragen 1; Gegenbuchungen sind negativ.</summary>
    public decimal Value { get; }

    /// <summary>Erfassungszeitpunkt auf dem Gerät; gewertet in der Zeitzone des Tenants.</summary>
    public DateTimeOffset RecordedAt { get; }

    /// <summary>Eingang beim Server.</summary>
    public DateTimeOffset ReceivedAt { get; }

    public ContributionChannel Channel { get; }

    /// <summary>Gruppen der Person zum Beitragszeitpunkt (Organisation 3.3); ein späterer Wechsel ändert vergangene Stände nicht.</summary>
    public IReadOnlyList<Guid> GroupIds { get; private set; }

    /// <summary>Das pauschale Aktivitätsereignis für Fortschritt (Challenges 8).</summary>
    public Guid? ActivityEventId { get; }

    public Guid? ReversalOf { get; }

    public bool IsReversal => ReversalOf is not null;

    public bool Reversed { get; private set; }

    public static Contribution Record(TenantId tenantId, Guid challengeId, PersonId personId, decimal value, DateTimeOffset recordedAt, DateTimeOffset receivedAt, ContributionChannel channel, IReadOnlyList<Guid>? groupIds = null, Guid? activityEventId = null) =>
        new(tenantId, Guid.CreateVersion7(), challengeId, personId, decimal.Round(value, 4, MidpointRounding.ToEven), recordedAt.ToUniversalTime(), receivedAt.ToUniversalTime(), channel, groupIds ?? [], activityEventId, null);

    /// <summary>Korrektur als Gegenbuchung (Challenges 3, A-039): der Wert wird zurückgenommen, Abzeichen bleiben.</summary>
    public Contribution Reverse(DateTimeOffset now)
    {
        if (IsReversal || Reversed)
        {
            throw new ChallengeLifecycleException("already_reversed", "Ein Beitrag wird höchstens einmal korrigiert.");
        }

        Reversed = true;
        return new Contribution(TenantId, Guid.CreateVersion7(), ChallengeId, PersonId, -Value, RecordedAt, now.ToUniversalTime(), Channel, GroupIds, ActivityEventId, Id);
    }

    /// <summary>Austritt (Datenschutz 5.2): der Beitrag bleibt als Summenanteil ohne Personenbezug.</summary>
    public void Anonymise()
    {
        PersonId = AnonymousPerson;
        GroupIds = [];
    }

    /// <summary>Anonymer Beitragender nach Austritt: keine Person, zählt in Summen, nie in Beitragendenzahlen.</summary>
    public static PersonId AnonymousPerson { get; } = new(Guid.Empty);
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

    public static ContributionRejection Validate(Challenge challenge, ChallengeMetric metric, decimal value, DateTimeOffset recordedAt, DateTimeOffset receivedAt) =>
        Validate(challenge, metric, value, recordedAt, receivedAt, TenantTimeZone.Default);

    public static ContributionRejection Validate(Challenge challenge, ChallengeMetric metric, decimal value, DateTimeOffset recordedAt, DateTimeOffset receivedAt, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(challenge);
        if (!challenge.AcceptsContributions)
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

        var recordedDay = TenantTimeZone.DayOf(recordedAt, zone);
        var receivedDay = TenantTimeZone.DayOf(receivedAt, zone);
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

    /// <summary>Korrektur bis zum Ende der Nachfrist; am Kiosk keine Korrektur (Challenges 3).</summary>
    public static bool MayReverse(Challenge challenge, DateTimeOffset now) => challenge is not null && challenge.AcceptsContributions && now <= challenge.AcceptsContributionsUntil;
}
