using System.Text.Json;
using CompanyHero.Platform.Tenancy;

namespace CompanyHero.Modules.Challenges.Domain;

/// <summary>Ereignistypen der Domäne Challenges (Backend 6.4): versioniert, ohne Anzeigenamen, ohne Messwerte Dritter.</summary>
public static class ChallengeEventTypes
{
    public const string ContributionRecorded = "challenges.contribution.recorded";
}

/// <summary>Nutzdaten von „Beitrag erfasst“, Version 1.</summary>
public sealed record ContributionRecordedPayload(Guid ContributionId, Guid ChallengeId, DateTimeOffset RecordedAt);

/// <summary>
/// Fachereignis in der eigenen Ereignistabelle des Moduls (Backend 6.4, A-006). Abonnenten anderer Module erhalten
/// die Kennung als Job und lesen das Ereignis über <c>IChallengeEvents</c>.
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

    public PersonId? CausedBy { get; }

    public static ChallengeEvent ContributionRecorded(TenantId tenantId, Contribution contribution, DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(contribution);
        var payload = JsonSerializer.Serialize(new ContributionRecordedPayload(contribution.Id, contribution.ChallengeId, contribution.RecordedAt), PayloadJson.Options);
        return new ChallengeEvent(tenantId, Guid.CreateVersion7(), ChallengeEventTypes.ContributionRecorded, 1, occurredAt.ToUniversalTime(), payload, contribution.PersonId);
    }

    public ContributionRecordedPayload ReadContributionRecorded() =>
        Type == ChallengeEventTypes.ContributionRecorded
            ? JsonSerializer.Deserialize<ContributionRecordedPayload>(Payload, PayloadJson.Options) ?? throw new InvalidOperationException("Leere Nutzdaten.")
            : throw new InvalidOperationException($"Ereignis vom Typ '{Type}' ist kein „Beitrag erfasst“.");
}

internal static class PayloadJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web);
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

    public static CollectiveState For(TenantId tenantId, Guid challengeId) => new(tenantId, challengeId);

    public void Set(decimal total, int contributionCount, int contributorCount, DateTimeOffset now)
    {
        Total = total;
        ContributionCount = contributionCount;
        ContributorCount = contributorCount;
        UpdatedAt = now.ToUniversalTime();
    }
}
