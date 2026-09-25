using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CompanyHero.Platform.Tenancy;

namespace CompanyHero.Modules.Challenges.Domain;

/// <summary>Die zwei Idempotenzregime aus A-009 in einem Namensraum je Tenant und Person.</summary>
public enum KeyRegime
{
    /// <summary>Vorgangskennung: vom Backend vor dem Absenden reserviert (Kiosk, A-005).</summary>
    Operation = 1,

    /// <summary>Idempotenzschlüssel: vom Client als zeitlich sortierbare Kennung erzeugt (Offline-Beiträge, A-008).</summary>
    Client = 2,
}

public enum KeyState
{
    /// <summary>Reserviert, noch kein Beitrag.</summary>
    Reserved = 1,

    /// <summary>Beitrag gespeichert; Kennung und Inhalt sind festgeschrieben.</summary>
    Committed = 2,

    /// <summary>Terminal abgebrochen; nimmt keinen verspäteten Beitrag mehr an (A-005).</summary>
    Aborted = 3,
}

/// <summary>
/// Idempotenznachweis eines Beitrags (Domänenkarte 3 „Idempotenz von Beiträgen“, A-009): Vorgangskennungen und
/// Client-Idempotenzschlüssel liegen in einem Namensraum je Tenant und Person. Gleiche Kennung mit gleichem Inhalt
/// liefert denselben Beitrag; gleiche Kennung mit abweichendem Inhalt wird abgelehnt. Speicherung und Nachweis sind atomar.
/// </summary>
public sealed class ContributionKey : ITenantOwned
{
    private ContributionKey(TenantId tenantId, PersonId personId, string key, KeyRegime regime, KeyState state, DateTimeOffset reservedAt)
    {
        TenantId = tenantId;
        PersonId = personId;
        Key = key;
        Regime = regime;
        State = state;
        ReservedAt = reservedAt;
    }

    public TenantId TenantId { get; }

    public PersonId PersonId { get; }

    public string Key { get; }

    public KeyRegime Regime { get; }

    public KeyState State { get; private set; }

    public string? ContentHash { get; private set; }

    public Guid? ContributionId { get; private set; }

    public DateTimeOffset ReservedAt { get; }

    public DateTimeOffset? CommittedAt { get; private set; }

    /// <summary>Backend reserviert eine Vorgangskennung, gebunden an Tenant und Person (A-005).</summary>
    public static ContributionKey ReserveOperation(TenantId tenantId, PersonId personId, DateTimeOffset now) =>
        new(tenantId, personId, Guid.CreateVersion7().ToString("D"), KeyRegime.Operation, KeyState.Reserved, now.ToUniversalTime());

    /// <summary>Client-Idempotenzschlüssel: zeitlich sortierbare eindeutige Kennung (UUIDv7) des Offline-Repositorys.</summary>
    public static ContributionKey ForClientKey(TenantId tenantId, PersonId personId, string key, DateTimeOffset now)
    {
        if (!IsValidClientKey(key))
        {
            throw new ArgumentException("Der Idempotenzschlüssel ist keine zeitlich sortierbare Kennung (UUIDv7).", nameof(key));
        }

        return new ContributionKey(tenantId, personId, Normalize(key), KeyRegime.Client, KeyState.Reserved, now.ToUniversalTime());
    }

    public static bool IsValidClientKey(string? key) =>
        Guid.TryParseExact(key, "D", out var guid) && guid.Version == 7;

    public static string Normalize(string key) => key.Trim().ToLowerInvariant();

    public void Commit(Guid contributionId, string contentHash, DateTimeOffset now)
    {
        if (State != KeyState.Reserved)
        {
            throw new InvalidOperationException("Nur eine reservierte Kennung kann festgeschrieben werden.");
        }

        State = KeyState.Committed;
        ContributionId = contributionId;
        ContentHash = contentHash;
        CommittedAt = now.ToUniversalTime();
    }

    /// <summary>Terminal abgebrochen (A-005 Wiederaufnahme): ein verspäteter Beitrag wird nicht mehr angenommen.</summary>
    public void Abort()
    {
        if (State == KeyState.Committed)
        {
            throw new InvalidOperationException("Ein festgeschriebener Vorgang wird nicht abgebrochen; Korrekturen laufen als Gegenbuchung.");
        }

        State = KeyState.Aborted;
    }

    public bool MatchesContent(string contentHash) => string.Equals(ContentHash, contentHash, StringComparison.Ordinal);

    /// <summary>Kanonischer Inhalt des Beitrags; abweichender Inhalt unter gleicher Kennung wird abgelehnt (A-009).</summary>
    public static string HashContent(Guid challengeId, decimal value, DateTimeOffset recordedAt, ContributionChannel channel)
    {
        var canonical = string.Create(CultureInfo.InvariantCulture, $"{challengeId:D}|{decimal.Round(value, 4, MidpointRounding.ToEven):0.0000}|{recordedAt.ToUniversalTime():O}|{(int)channel}");
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
