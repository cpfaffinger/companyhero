using System.Text.Json;
using CompanyHero.Platform.Tenancy;

namespace CompanyHero.Modules.Notifications.Domain;

/// <summary>In-App-Eintrag (Benachrichtigungen 7.1 Nr. 3): immer vorhanden, Grundlage aller anderen Kanäle; ohne Anzeigenamen Dritter.</summary>
public sealed class NotificationEntry : ITenantOwned
{
    private NotificationEntry(TenantId tenantId, Guid id, PersonId personId, NotificationCategory category, string eventKey, string textKey, string paramsJson, string target, DateTimeOffset occurredAt)
    {
        TenantId = tenantId;
        Id = id;
        PersonId = personId;
        Category = category;
        EventKey = eventKey;
        TextKey = textKey;
        ParamsJson = paramsJson;
        Target = target;
        OccurredAt = occurredAt;
    }

    public TenantId TenantId { get; }

    public Guid Id { get; }

    public PersonId PersonId { get; }

    public NotificationCategory Category { get; }

    /// <summary>Ereignisschlüssel: je Person genau ein Eintrag je Ereignis (Deduplikation).</summary>
    public string EventKey { get; }

    public string TextKey { get; }

    public string ParamsJson { get; }

    /// <summary>Ziel in der App als Pfad, etwa <c>/challenges/&lt;id&gt;</c>; nie eine Person.</summary>
    public string Target { get; }

    public DateTimeOffset OccurredAt { get; }

    public DateTimeOffset? ReadAt { get; private set; }

    public static NotificationEntry Create(TenantId tenantId, PersonId personId, NotificationCategory category, string eventKey, string textKey, IReadOnlyDictionary<string, string> parameters, string target, DateTimeOffset occurredAt) =>
        new(tenantId, Guid.CreateVersion7(), personId, category, eventKey, textKey, JsonSerializer.Serialize(parameters, PushPayload.Json), target, occurredAt.ToUniversalTime());

    public void MarkRead(DateTimeOffset now) => ReadAt ??= now.ToUniversalTime();

    public IReadOnlyDictionary<string, string> Parameters() =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(ParamsJson, PushPayload.Json) ?? new Dictionary<string, string>(StringComparer.Ordinal);
}

/// <summary>Push-Abonnement je Person und Gerät (Benachrichtigungen 3.2); Endpunkt und Schlüssel gelten wie Zugangsdaten und werden nie protokolliert.</summary>
public sealed class PushSubscription : ITenantOwned
{
    private PushSubscription(TenantId tenantId, Guid id, PersonId personId, string endpoint, string p256dh, string auth, string deviceLabel, DateTimeOffset createdAt)
    {
        TenantId = tenantId;
        Id = id;
        PersonId = personId;
        Endpoint = endpoint;
        P256dh = p256dh;
        Auth = auth;
        DeviceLabel = deviceLabel;
        CreatedAt = createdAt;
    }

    public TenantId TenantId { get; }

    public Guid Id { get; }

    public PersonId PersonId { get; }

    public string Endpoint { get; private set; }

    public string P256dh { get; private set; }

    public string Auth { get; private set; }

    public string DeviceLabel { get; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset? LastSuccessAt { get; private set; }

    public int FailureCount { get; private set; }

    public DateTimeOffset? PausedAt { get; private set; }

    public bool Active => PausedAt is null;

    public static PushSubscription Register(TenantId tenantId, PersonId personId, string endpoint, string p256dh, string auth, string? deviceLabel, DateTimeOffset now)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("Endpunkt des Push-Dienstes muss eine https-Adresse sein.", nameof(endpoint));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(p256dh);
        ArgumentException.ThrowIfNullOrWhiteSpace(auth);
        var label = string.IsNullOrWhiteSpace(deviceLabel) ? "Gerät" : deviceLabel.Trim();
        return new PushSubscription(tenantId, Guid.CreateVersion7(), personId, endpoint, p256dh, auth, label.Length > 60 ? label[..60] : label, now.ToUniversalTime());
    }

    /// <summary><c>pushsubscriptionchange</c>: Erneuerung ohne Nutzeraktion (Benachrichtigungen 3.2 Nr. 4).</summary>
    public void Renew(string endpoint, string p256dh, string auth)
    {
        Endpoint = endpoint;
        P256dh = p256dh;
        Auth = auth;
        FailureCount = 0;
        PausedAt = null;
    }

    public void RecordSuccess(DateTimeOffset now)
    {
        LastSuccessAt = now.ToUniversalTime();
        FailureCount = 0;
    }

    /// <summary>Nach fünf aufeinanderfolgenden Fehlern pausiert; der nächste App-Start prüft und erneuert (Benachrichtigungen 3.1).</summary>
    public void RecordFailure(DateTimeOffset now)
    {
        FailureCount++;
        if (FailureCount >= NotificationRules.MaxPushFailures)
        {
            PausedAt = now.ToUniversalTime();
        }
    }
}

/// <summary>Persönliche Einstellungen je Person und Tenant (Benachrichtigungen 6.1): Push je Kategorie, E-Mail je Kategorie, erweiterte Ruhezeit.</summary>
public sealed class PersonNotificationSettings : ITenantOwned
{
    private PersonNotificationSettings(TenantId tenantId, PersonId personId)
    {
        TenantId = tenantId;
        PersonId = personId;
    }

    public TenantId TenantId { get; }

    public PersonId PersonId { get; }

    public bool PushChallenge { get; private set; } = true;

    public bool PushProgress { get; private set; } = true;

    public bool EmailChallenge { get; private set; }

    public bool EmailProgress { get; private set; }

    public TimeOnly? QuietStart { get; private set; }

    public TimeOnly? QuietEnd { get; private set; }

    public int EmailFailureCount { get; private set; }

    public DateTimeOffset? EmailPausedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public static PersonNotificationSettings Default(TenantId tenantId, PersonId personId) => new(tenantId, personId);

    public bool PushFor(NotificationCategory category) => category == NotificationCategory.Challenge ? PushChallenge : PushProgress;

    public bool EmailFor(NotificationCategory category) => category == NotificationCategory.Challenge ? EmailChallenge : EmailProgress;

    public void Update(bool pushChallenge, bool pushProgress, bool emailChallenge, bool emailProgress, TimeOnly? quietStart, TimeOnly? quietEnd, DateTimeOffset now)
    {
        if (quietStart.HasValue != quietEnd.HasValue)
        {
            throw new ArgumentException("Ruhezeit braucht Beginn und Ende.", nameof(quietStart));
        }

        PushChallenge = pushChallenge;
        PushProgress = pushProgress;
        EmailChallenge = emailChallenge;
        EmailProgress = emailProgress;
        QuietStart = quietStart;
        QuietEnd = quietEnd;
        UpdatedAt = now.ToUniversalTime();
    }

    /// <summary>Ein-Klick-Abmeldung je Kategorie (Benachrichtigungen 6.3) wirkt sofort.</summary>
    public void UnsubscribeEmail(NotificationCategory category, DateTimeOffset now)
    {
        if (category == NotificationCategory.Challenge)
        {
            EmailChallenge = false;
        }
        else
        {
            EmailProgress = false;
        }

        UpdatedAt = now.ToUniversalTime();
    }

    public void RecordEmailSuccess() => EmailFailureCount = 0;

    public void RecordEmailFailure(DateTimeOffset now)
    {
        EmailFailureCount++;
        if (EmailFailureCount >= NotificationRules.MaxEmailFailures)
        {
            EmailPausedAt = now.ToUniversalTime();
        }
    }
}

/// <summary>Tenant-Schalter je Kanal (Benachrichtigungen 2.1): Ausschalten pausiert, Abonnements bleiben; Ruhezeit des Tenants.</summary>
public sealed class TenantNotificationSettings : ITenantOwned
{
    private TenantNotificationSettings(TenantId tenantId)
    {
        TenantId = tenantId;
    }

    public TenantId TenantId { get; }

    public bool PushEnabled { get; private set; } = true;

    public bool EmailEnabled { get; private set; } = true;

    public bool AushangEnabled { get; private set; } = true;

    public TimeOnly QuietStart { get; private set; } = NotificationRules.DefaultQuietStart;

    public TimeOnly QuietEnd { get; private set; } = NotificationRules.DefaultQuietEnd;

    public DateTimeOffset? UpdatedAt { get; private set; }

    public static TenantNotificationSettings Default(TenantId tenantId) => new(tenantId);

    public void Update(bool pushEnabled, bool emailEnabled, bool aushangEnabled, TimeOnly quietStart, TimeOnly quietEnd, DateTimeOffset now)
    {
        PushEnabled = pushEnabled;
        EmailEnabled = emailEnabled;
        AushangEnabled = aushangEnabled;
        QuietStart = quietStart;
        QuietEnd = quietEnd;
        UpdatedAt = now.ToUniversalTime();
    }
}

/// <summary>Zustellung je Eintrag und Kanal (Benachrichtigungen 7.1 Nr. 3, 4): Status ohne Inhalt, Idempotenzschlüssel, Rückhaltezeit.</summary>
public sealed class Delivery : ITenantOwned
{
    private Delivery(TenantId tenantId, Guid id, Guid entryId, PersonId personId, NotificationChannel channel, string dedupeKey, DeliveryStatus status, DateTimeOffset createdAt, DateOnly day, DateTimeOffset? heldUntil)
    {
        TenantId = tenantId;
        Id = id;
        EntryId = entryId;
        PersonId = personId;
        Channel = channel;
        DedupeKey = dedupeKey;
        Status = status;
        CreatedAt = createdAt;
        Day = day;
        HeldUntil = heldUntil;
    }

    public TenantId TenantId { get; }

    public Guid Id { get; }

    public Guid EntryId { get; }

    public PersonId PersonId { get; }

    public NotificationChannel Channel { get; }

    public string DedupeKey { get; }

    public DeliveryStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    /// <summary>Kalendertag in der Tenant-Zeitzone für das Kontingent (Benachrichtigungen 4.2).</summary>
    public DateOnly Day { get; }

    public DateTimeOffset? HeldUntil { get; }

    public DateTimeOffset? FinishedAt { get; private set; }

    /// <summary>Technischer Grund ohne Personenbezug, etwa <c>push:410</c>.</summary>
    public string? Error { get; private set; }

    public static Delivery Create(TenantId tenantId, NotificationEntry entry, NotificationChannel channel, DateTimeOffset now, DateOnly day, DateTimeOffset? heldUntil)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new Delivery(tenantId, Guid.CreateVersion7(), entry.Id, entry.PersonId, channel, NotificationRules.DedupeKey(entry.EventKey, entry.PersonId, channel), heldUntil is null ? DeliveryStatus.Queued : DeliveryStatus.Held, now.ToUniversalTime(), day, heldUntil?.ToUniversalTime());
    }

    public void Finish(DeliveryStatus status, DateTimeOffset now, string? error = null)
    {
        Status = status;
        FinishedAt = now.ToUniversalTime();
        Error = error;
    }
}

/// <summary>Wöchentlicher Aushang (Benachrichtigungen 6.2) als erzeugtes PDF ohne Personenbezug.</summary>
public sealed class Aushang : ITenantOwned
{
    private Aushang(TenantId tenantId, Guid id, string week, DateTimeOffset generatedAt, byte[] pdf)
    {
        TenantId = tenantId;
        Id = id;
        Week = week;
        GeneratedAt = generatedAt;
        Pdf = pdf;
    }

    public TenantId TenantId { get; }

    public Guid Id { get; }

    /// <summary>ISO-Woche, etwa <c>2026-W39</c>.</summary>
    public string Week { get; }

    public DateTimeOffset GeneratedAt { get; }

#pragma warning disable CA1819 // Byte-Inhalt des PDF
    public byte[] Pdf { get; }
#pragma warning restore CA1819

    public static Aushang Create(TenantId tenantId, string week, DateTimeOffset now, byte[] pdf) => new(tenantId, Guid.CreateVersion7(), week, now.ToUniversalTime(), pdf);

    public static string WeekOf(DateOnly day)
    {
        var date = day.ToDateTime(TimeOnly.MinValue);
        return $"{System.Globalization.ISOWeek.GetYear(date)}-W{System.Globalization.ISOWeek.GetWeekOfYear(date):00}";
    }
}
