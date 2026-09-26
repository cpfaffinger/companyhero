using System.Text.Json;
using CompanyHero.Platform.Tenancy;

namespace CompanyHero.Modules.Challenges.Domain;

/// <summary>Ereignistypen der Domäne Challenges (Backend 6.4): versioniert, ohne Anzeigenamen, ohne Messwerte Dritter, nie individuelle Rückstände.</summary>
public static class ChallengeEventTypes
{
    public const string Prefix = "challenges.";
    public const string ContributionRecorded = "challenges.contribution.recorded";
    public const string ContributionReversed = "challenges.contribution.reversed";
    public const string Started = "challenges.started";
    public const string MilestoneReached = "challenges.milestone.reached";
    public const string Ended = "challenges.ended";
}

/// <summary>Nutzdaten von „Beitrag erfasst“, Version 1.</summary>
public sealed record ContributionRecordedPayload(Guid ContributionId, Guid ChallengeId, DateTimeOffset RecordedAt);

public sealed record ContributionReversedPayload(Guid ContributionId, Guid ReversalId, Guid ChallengeId);

/// <summary>Start, Meilenstein und Ende tragen nur Challenge, Titel und Prozent (Challenges 8: nie Beitragssummen Dritter, nie Rückstände).</summary>
public sealed record ChallengeStatePayload(Guid ChallengeId, string Title, int Percent);

/// <summary>Meilenstein 25, 50, 75, 100 Prozent (Challenges 6.2): genau ein Ereignis je Meilenstein.</summary>
public sealed record MilestonePayload(Guid ChallengeId, string Title, int Percent);

/// <summary>
/// Fachereignis in der eigenen Ereignistabelle des Moduls (Backend 6.4, A-006). Abonnenten anderer Module erhalten
/// die Kennung als Job und lesen das Ereignis über die Ereignisquelle oder <c>IChallengeEvents</c>.
/// </summary>
public sealed class ChallengeEvent : ITenantOwned
{
    private ChallengeEvent(TenantId tenantId, Guid id, string type, int version, DateTimeOffset occurredAt, string payload, PersonId? causedBy)
    {
        TenantId = tenantId;
        Id = id;
        Type = type;
        Version = version;
        OccurredAt = occurredAt;
        Payload = payload;
        CausedBy = causedBy;
    }

    public TenantId TenantId { get; }

    public Guid Id { get; }

    public string Type { get; }

    public int Version { get; }

    public DateTimeOffset OccurredAt { get; }

    /// <summary>JSON der versionierten Nutzdaten.</summary>
    public string Payload { get; }

    public PersonId? CausedBy { get; private set; }

    public static ChallengeEvent ContributionRecorded(TenantId tenantId, Contribution contribution, DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(contribution);
        return Create(tenantId, ChallengeEventTypes.ContributionRecorded, new ContributionRecordedPayload(contribution.Id, contribution.ChallengeId, contribution.RecordedAt), occurredAt, contribution.PersonId);
    }

    public static ChallengeEvent Create<TPayload>(TenantId tenantId, string type, TPayload payload, DateTimeOffset occurredAt, PersonId? causedBy) =>
        new(tenantId, Guid.CreateVersion7(), type, 1, occurredAt.ToUniversalTime(), JsonSerializer.Serialize(payload, PayloadJson.Options), causedBy);

    public ContributionRecordedPayload ReadContributionRecorded() =>
        Type == ChallengeEventTypes.ContributionRecorded
            ? JsonSerializer.Deserialize<ContributionRecordedPayload>(Payload, PayloadJson.Options) ?? throw new InvalidOperationException("Leere Nutzdaten.")
            : throw new InvalidOperationException($"Ereignis vom Typ '{Type}' ist kein „Beitrag erfasst“.");
}

internal static class PayloadJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web);
}

/// <summary>Meilensteine des Kollektivstands (Challenges 6.2).</summary>
public static class Milestones
{
    public static IReadOnlyList<int> All { get; } = [25, 50, 75, 100];
}

/// <summary>Kollektivstand einer Challenge (Challenges 2.3), von einem Job aus den Beiträgen berechnet; idempotent.</summary>
public sealed class CollectiveState : ITenantOwned
{
    private CollectiveState(TenantId tenantId, Guid challengeId)
    {
        TenantId = tenantId;
        ChallengeId = challengeId;
    }

    public TenantId TenantId { get; }

    public Guid ChallengeId { get; }

    public decimal Total { get; private set; }

    public int ContributionCount { get; private set; }

    public int ContributorCount { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Höchster gemeldeter Meilenstein (0, 25, 50, 75, 100): jeder Meilenstein erzeugt genau ein Feed-Ereignis (Challenges 6.2).</summary>
    public int MilestoneReached { get; private set; }

    public static CollectiveState For(TenantId tenantId, Guid challengeId) => new(tenantId, challengeId);

    public void Set(decimal total, int contributionCount, int contributorCount, DateTimeOffset now)
    {
        Total = total;
        ContributionCount = contributionCount;
        ContributorCount = contributorCount;
        UpdatedAt = now.ToUniversalTime();
    }

    /// <summary>Neu erreichte Meilensteine in aufsteigender Reihenfolge; merkt sie sich, damit sie nur einmal gemeldet werden.</summary>
    public IReadOnlyList<int> AdvanceMilestones(int percent)
    {
        var reached = Milestones.All.Where(m => m > MilestoneReached && percent >= m).ToList();
        if (reached.Count > 0)
        {
            MilestoneReached = reached[^1];
        }

        return reached;
    }
}
