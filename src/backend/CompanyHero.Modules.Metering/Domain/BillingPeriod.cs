using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CompanyHero.Platform.Tenancy;

namespace CompanyHero.Modules.Metering.Domain;

/// <summary>
/// Abrechnungsperiode eines Tenants (Metering 2.2, 2.3; A-069): offen bis zur Versiegelung am dritten Kalendertag des
/// Folgemonats um 03:00 Tenant-Zeit, danach unveränderlich. Trägt das Periodensalz für die Zähler-Slots; es wird sieben Tage
/// nach der Versiegelung vernichtet, danach ist keine Rückrechnung mehr möglich. Das Salz liegt nur geschützt
/// (Data Protection) in der Zeile; die Zeile selbst bleibt als Nachweis der Vernichtung erhalten.
/// </summary>
public sealed class BillingPeriod : ITenantOwned
{
    private BillingPeriod(TenantId tenantId, string period, DateTimeOffset openedAt, byte[] protectedSalt)
    {
        TenantId = tenantId;
        Period = period;
        OpenedAt = openedAt;
        ProtectedSalt = protectedSalt;
    }

    public TenantId TenantId { get; }

    /// <summary><c>YYYY-MM</c> in der Tenant-Zeitzone.</summary>
    public string Period { get; }

    public DateTimeOffset OpenedAt { get; }

    public DateTimeOffset? SealedAt { get; private set; }

    /// <summary>Geschütztes Periodensalz; <c>null</c> nach der Vernichtung.</summary>
    public byte[]? ProtectedSalt { get; private set; }

    public DateTimeOffset? SaltDestroyedAt { get; private set; }

    public bool IsSealed => SealedAt is not null;

    public bool SaltDestroyed => SaltDestroyedAt is not null;

    public static BillingPeriod Open(TenantId tenantId, string period, DateTimeOffset now, byte[] protectedSalt)
    {
        ArgumentNullException.ThrowIfNull(protectedSalt);
        if (!PeriodRules.IsValid(period))
        {
            throw new ArgumentException("Periode als YYYY-MM erwartet.", nameof(period));
        }

        return new BillingPeriod(tenantId, period, now, protectedSalt);
    }

    public void Seal(DateTimeOffset now)
    {
        if (IsSealed)
        {
            throw new InvalidOperationException("Die Periode ist bereits versiegelt.");
        }

        SealedAt = now;
    }

    /// <summary>Vernichtung des Salzes (Metering 2.3): nur nach der Versiegelung; danach sind Slots nicht mehr berechenbar.</summary>
    public void DestroySalt(DateTimeOffset now)
    {
        if (!IsSealed)
        {
            throw new InvalidOperationException("Das Salz wird erst nach der Versiegelung vernichtet.");
        }

        ProtectedSalt = null;
        SaltDestroyedAt = now;
    }
}

/// <summary>Regeln der Periode (Metering 2.2, 2.3) als reine Fachregeln.</summary>
public static class PeriodRules
{
    /// <summary>Versiegelung am dritten Kalendertag des Folgemonats um 03:00 Tenant-Zeit (A-039 Nachfrist für Offline-Beiträge).</summary>
    public const int SealingDay = 3;

    public static TimeOnly SealingTime { get; } = new(3, 0);

    /// <summary>Sieben Tage nach Versiegelung wird das Periodensalz vernichtet.</summary>
    public static TimeSpan SaltRetentionAfterSealing { get; } = TimeSpan.FromDays(7);

    public static bool IsValid(string period) =>
        period is { Length: 7 } && DateTime.TryParseExact(period + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    /// <summary>Erster Tag der Periode.</summary>
    public static DateOnly FirstDay(string period) =>
        DateOnly.ParseExact(period + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string Next(string period) => FirstDay(period).AddMonths(1).ToString("yyyy-MM", CultureInfo.InvariantCulture);

    public static int DaysIn(string period)
    {
        var first = FirstDay(period);
        return DateTime.DaysInMonth(first.Year, first.Month);
    }

    /// <summary>Zeitpunkt der Versiegelung einer Periode in der Zone: dritter Kalendertag des Folgemonats, 03:00.</summary>
    public static DateTimeOffset SealingDueAt(string period, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var day = FirstDay(period).AddMonths(1).AddDays(SealingDay - 1);
        var local = day.ToDateTime(SealingTime, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
    }

    public static bool IsSealingDue(string period, DateTimeOffset now, TimeZoneInfo zone) => now >= SealingDueAt(period, zone);

    public static bool IsSaltDestructionDue(DateTimeOffset sealedAt, DateTimeOffset now) => now >= sealedAt + SaltRetentionAfterSealing;

    /// <summary>
    /// Zähler-Slot (Metering 2.3): schlüsselabhängiger Hash (HMAC-SHA256) aus Personenkennung und Periodensalz; 40 Hexzeichen mit
    /// dem Präfix <c>slot:</c>. Ohne das Salz ist die Personenkennung aus dem Slot nicht rekonstruierbar.
    /// </summary>
    public static string Slot(byte[] salt, PersonId personId)
    {
        ArgumentNullException.ThrowIfNull(salt);
        if (salt.Length < 32)
        {
            throw new ArgumentException("Periodensalz mit mindestens 32 Byte erwartet.", nameof(salt));
        }

        var mac = HMACSHA256.HashData(salt, Encoding.UTF8.GetBytes(personId.Value.ToString("D")));
        return "slot:" + Convert.ToHexStringLower(mac)[..40];
    }

    public static byte[] NewSalt() => RandomNumberGenerator.GetBytes(32);
}

/// <summary>Metriken mit Personennähe (Metering 5, A-023): nur als Monatssumme, nie als Tagesschnitt sichtbar.</summary>
public static class MeteringMetrics
{
    public const string ActiveMonth = "member.active_month";
    public const string ActiveDay = "member.active_day";
    public const string Joined = "member.joined";
    public const string TenantMonth = "tenant.month";
    public const string TrialDay = "module.trial_day";
    public const string ParticipantDay = "challenge.participant_day";
    public const string NotificationSent = "notification.sent";
    public const string KioskDeviceMonth = "kiosk.device_month";

    private static readonly HashSet<string> Personal = new(StringComparer.Ordinal) { ActiveMonth, ActiveDay, ParticipantDay, "arena.entry" };

    public static bool IsPersonal(string metric) => Personal.Contains(metric);

    /// <summary>Klartextdefinition je Metrik als Textschlüssel des Katalogs (Metering 5: Definitionen immer sichtbar).</summary>
    public static string DefinitionKey(string metric) => "metrik." + metric.Replace('.', '_');
}
