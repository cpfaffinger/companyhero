using System.Text.Json;
using CompanyHero.Platform.Tenancy;

namespace CompanyHero.Modules.Progress.Domain;

/// <summary>Ereignistypen der Domäne Fortschritt (Backend 6.4): Abzeichen, Stufe, Check-in; nie Messwerte, nie Rückstände.</summary>
public static class ProgressEventTypes
{
    public const string Prefix = "progress.";
    public const string BadgeAwarded = "progress.badge.awarded";
    public const string LevelReached = "progress.level.reached";
    public const string CheckInRecorded = "progress.checkin.recorded";
}

public sealed record BadgeAwardedPayload(Guid PersonId, string BadgeKey, string TextKey, string Category);

public sealed record LevelReachedPayload(Guid PersonId, int Level);

public sealed record CheckInRecordedPayload(Guid PersonId, DateOnly Day);

/// <summary>Fachereignis in der eigenen Ereignistabelle des Moduls (Backend 6.4, A-006); Abonnenten lesen über die Ereignisquelle.</summary>
public sealed class ProgressEvent : ITenantOwned
{
    private ProgressEvent(TenantId tenantId, Guid id, string type, int version, DateTimeOffset occurredAt, string payload, PersonId? causedBy)
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

    public string Payload { get; }

    public PersonId? CausedBy { get; }

    public static ProgressEvent Create<TPayload>(TenantId tenantId, string type, TPayload payload, DateTimeOffset occurredAt, PersonId causedBy) =>
        new(tenantId, Guid.CreateVersion7(), type, 1, occurredAt.ToUniversalTime(), JsonSerializer.Serialize(payload, PayloadJson.Options), causedBy);
}

internal static class PayloadJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web);
}
