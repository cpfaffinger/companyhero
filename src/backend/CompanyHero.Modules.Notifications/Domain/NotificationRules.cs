using System.Globalization;
using System.Text.Json;
using CompanyHero.Platform.Tenancy;

namespace CompanyHero.Modules.Notifications.Domain;

/// <summary>Kategorien des Durchstichs (Benachrichtigungen 4.1): Challenge und Fortschritt laufen durch die Pipeline; Konto geht nur per E-Mail ohne Abmeldelink.</summary>
public enum NotificationCategory
{
    Challenge = 1,
    Progress = 2,
}

public enum NotificationChannel
{
    InApp = 1,
    Push = 2,
    Email = 3,
}

public enum DeliveryStatus
{
    /// <summary>Wartet auf den Kanaljob.</summary>
    Queued = 1,

    /// <summary>In der Ruhezeit zurückgehalten; der Kanaljob läuft um deren Ende (Benachrichtigungen 4.2).</summary>
    Held = 2,

    Sent = 3,

    Failed = 4,

    /// <summary>Nicht gesendet: kein Abonnement, keine Adresse oder Kanal inzwischen pausiert.</summary>
    Dropped = 5,
}

/// <summary>Nutzlast eines Push (Benachrichtigungen 3.1): ausschließlich Kennung, Kategorie und Ziel; Titel und Text lädt der Service Worker mit der Sitzung.</summary>
public sealed record PushPayload(string Id, string Kategorie, string Ziel)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public byte[] ToBytes() => JsonSerializer.SerializeToUtf8Bytes(this, Json);

    public static string CategoryName(NotificationCategory category) => category switch
    {
        NotificationCategory.Challenge => "challenge",
        _ => "fortschritt",
    };
}

/// <summary>Regelwerk für alle Kategorien (Benachrichtigungen 4.2): Kontingent, Ruhezeiten, Deduplikation, Fristen.</summary>
public static class NotificationRules
{
    /// <summary>Höchstens drei Push je Person und Tag über alle Kategorien (Erinnerungen und Konto ausgenommen).</summary>
    public const int MaxPushPerPersonAndDay = 3;

    /// <summary>Nach fünf aufeinanderfolgenden Fehlern wird das Abonnement pausiert (Benachrichtigungen 3.1).</summary>
    public const int MaxPushFailures = 5;

    /// <summary>Drei aufeinanderfolgende Unzustellbarkeiten pausieren E-Mail für die Person (Benachrichtigungen 6.3).</summary>
    public const int MaxEmailFailures = 3;

    public static readonly TimeOnly DefaultQuietStart = new(20, 0);

    public static readonly TimeOnly DefaultQuietEnd = new(7, 0);

    /// <summary>Einträge im Benachrichtigungszentrum bleiben 90 Tage (Benachrichtigungen 7.2).</summary>
    public static TimeSpan EntryRetention { get; } = TimeSpan.FromDays(90);

    /// <summary>Zustellstatus ohne Inhalt 30 Tage (Benachrichtigungen 6.3, 7.3).</summary>
    public static TimeSpan DeliveryRetention { get; } = TimeSpan.FromDays(30);

    public static TimeSpan PushTimeToLive { get; } = TimeSpan.FromHours(24);

    /// <summary>Idempotenzschlüssel aus Ereignis, Person und Kanal: je Person und Kanal höchstens eine Zustellung, auch bei Wiederholung von Jobs.</summary>
    public static string DedupeKey(string eventKey, PersonId person, NotificationChannel channel) =>
        $"{eventKey}:{person.Value:D}:{(int)channel}";

    /// <summary>Ruhezeit mit Überlauf über Mitternacht (20:00 bis 07:00): Beginn eingeschlossen, Ende ausgeschlossen.</summary>
    public static bool IsQuiet(TimeOnly local, TimeOnly start, TimeOnly end)
    {
        if (start == end)
        {
            return false;
        }

        return start > end ? local >= start || local < end : local >= start && local < end;
    }

    /// <summary>Ende der Ruhezeit nach <paramref name="now"/>, sonst <c>null</c>. Persönliche Ruhezeit erweitert die des Tenants, verkürzt sie nie.</summary>
    public static DateTimeOffset? HoldUntil(DateTimeOffset now, TimeZoneInfo zone, (TimeOnly Start, TimeOnly End) tenant, (TimeOnly Start, TimeOnly End)? person)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var candidate = now;
        for (var i = 0; i < 4; i++)
        {
            var local = TenantTimeZone.TimeOf(candidate, zone);
            if (IsQuiet(local, tenant.Start, tenant.End))
            {
                candidate = TenantTimeZone.NextLocalTime(candidate, tenant.End, zone);
                continue;
            }

            if (person is { } p && IsQuiet(local, p.Start, p.End))
            {
                candidate = TenantTimeZone.NextLocalTime(candidate, p.End, zone);
                continue;
            }

            return candidate == now ? null : candidate;
        }

        return candidate;
    }

    /// <summary>Sammelschlüssel je Bezug (Benachrichtigungen 3.1), damit mehrere Meilensteine derselben Challenge zu einer Anzeige zusammenfallen.</summary>
    public static string Topic(string target)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(target));
        return System.Buffers.Text.Base64Url.EncodeToString(hash.AsSpan(0, 24));
    }

    public static string Percent(int percent) => percent.ToString(CultureInfo.InvariantCulture);
}
