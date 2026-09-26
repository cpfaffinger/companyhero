using CompanyHero.Platform.Tenancy;

namespace CompanyHero.Modules.Identity.Domain;

/// <summary>
/// Kiosk-Gerät (Zugang 6.1, A-018): vom Tenant-Admin angelegt, mit einmaligem Registrierungscode (15 Minuten) registriert;
/// danach führt ein Gerätegeheimnis die Gerätesitzung. Gespeichert werden nur Hashes; das Gerät kennt keine Personen.
/// </summary>
public sealed class KioskDevice : ITenantOwned
{
    public static TimeSpan RegistrationCodeLifetime { get; } = TimeSpan.FromMinutes(15);

    private KioskDevice(TenantId tenantId, Guid id, string name, string registrationCodeHash, DateTimeOffset createdAt)
    {
        TenantId = tenantId;
        Id = id;
        Name = name;
        RegistrationCodeHash = registrationCodeHash;
        RegistrationExpiresAt = createdAt + RegistrationCodeLifetime;
        CreatedAt = createdAt;
    }

    public TenantId TenantId { get; }

    public Guid Id { get; }

    public string Name { get; }

    public string? RegistrationCodeHash { get; private set; }

    public DateTimeOffset RegistrationExpiresAt { get; }

    public string? SecretHash { get; private set; }

    public DateTimeOffset? SecretIssuedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset? RegisteredAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public DateTimeOffset? LastSeenAt { get; private set; }

    /// <summary>Anzahl der Anmeldungen am Gerät (Zugang 6.1: „nie Personen“).</summary>
    public int LoginCount { get; private set; }

    public bool IsActive => RegisteredAt is not null && RevokedAt is null;

    public static (KioskDevice Device, string RegistrationCode) Create(TenantId tenantId, string name, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var code = AccessCodes.NewKioskRegistrationCode();
        return (new KioskDevice(tenantId, Guid.CreateVersion7(), name.Trim(), AccessCodes.Hash(code), now), code);
    }

    public bool RegistrationCodeMatches(string input, DateTimeOffset now) =>
        RegisteredAt is null && RevokedAt is null && now <= RegistrationExpiresAt && AccessCodes.HashEquals(RegistrationCodeHash, AccessCodes.Normalize(input));

    /// <summary>Registrierung am Gerät: Code verbraucht, Gerätegeheimnis ausgestellt (einmal im Klartext).</summary>
    public string Register(DateTimeOffset now)
    {
        RegistrationCodeHash = null;
        RegisteredAt = now;
        return IssueSecret(now);
    }

    public bool SecretMatches(string secret) => IsActive && AccessCodes.HashEquals(SecretHash, secret);

    public bool NeedsRotation(DateTimeOffset now) => SecretIssuedAt is not null && now - SecretIssuedAt.Value >= SessionPolicy.KioskDeviceSecretRotation;

    /// <summary>Automatische Rotation alle 30 Tage: neues Geheimnis, altes sofort ungültig.</summary>
    public string Rotate(DateTimeOffset now) => IssueSecret(now);

    public void Revoke(DateTimeOffset now)
    {
        RevokedAt ??= now;
        SecretHash = null;
    }

    public void Seen(DateTimeOffset now) => LastSeenAt = now;

    public void CountLogin(DateTimeOffset now)
    {
        LoginCount++;
        LastSeenAt = now;
    }

    private string IssueSecret(DateTimeOffset now)
    {
        var secret = AccessCodes.NewSecret();
        SecretHash = AccessCodes.Hash(secret);
        SecretIssuedAt = now;
        return secret;
    }
}

/// <summary>PIN-Regeln (Zugang 6.2): vier Ziffern, keine trivialen Folgen und Wiederholungen.</summary>
public static class KioskPin
{
    public const int Length = 4;

    public static bool IsAcceptable(string pin)
    {
        if (pin is null || pin.Length != Length || !pin.All(char.IsAsciiDigit))
        {
            return false;
        }

        if (pin.Distinct().Count() == 1)
        {
            return false;
        }

        var ascending = true;
        var descending = true;
        for (var i = 1; i < pin.Length; i++)
        {
            ascending &= pin[i] == pin[i - 1] + 1;
            descending &= pin[i] == pin[i - 1] - 1;
        }

        if (ascending || descending)
        {
            return false;
        }

        // Paarwiederholung (1212, 3434) und Spiegelung (1221) sind ebenfalls trivial.
        return !(pin[0] == pin[2] && pin[1] == pin[3]) && !(pin[0] == pin[3] && pin[1] == pin[2]);
    }
}

/// <summary>Kiosk-Kennung und PIN einer Person (Zugang 6.2); gespeichert wird der PIN-Hash mit personenbezogenem Salz.</summary>
public sealed class KioskCredential : ITenantOwned
{
    // EF Core: Materialisierung ohne Konstruktorbindung.
    private KioskCredential()
    {
        KioskId = string.Empty;
    }

    private KioskCredential(TenantId tenantId, PersonId personId, string kioskId, string? pinHash, DateTimeOffset now)
    {
        TenantId = tenantId;
        PersonId = personId;
        KioskId = kioskId;
        PinHash = pinHash;
        PinSetAt = pinHash is null ? null : now;
    }

    public TenantId TenantId { get; }

    public PersonId PersonId { get; }

    /// <summary>Sechs Ziffern, je Tenant eindeutig.</summary>
    public string KioskId { get; }

    /// <summary>Ohne PIN (Beitritt auf dem eigenen Gerät) ist die Kennung bekannt, die Kiosk-Anmeldung aber erst nach dem Setzen der PIN möglich.</summary>
    public string? PinHash { get; private set; }

    public DateTimeOffset? PinSetAt { get; private set; }

    public static KioskCredential Create(TenantId tenantId, PersonId personId, string kioskId, string? pin, DateTimeOffset now)
    {
        if (pin is not null && !KioskPin.IsAcceptable(pin))
        {
            throw new ArgumentException("PIN ist trivial oder nicht vierstellig.", nameof(pin));
        }

        return new KioskCredential(tenantId, personId, kioskId, pin is null ? null : HashPin(personId, pin), now);
    }

    public bool PinMatches(string pin) => PinHash is not null && AccessCodes.HashEquals(PinHash, Salted(PersonId, pin));

    public void ChangePin(string pin, DateTimeOffset now)
    {
        if (!KioskPin.IsAcceptable(pin))
        {
            throw new ArgumentException("PIN ist trivial oder nicht vierstellig.", nameof(pin));
        }

        PinHash = HashPin(PersonId, pin);
        PinSetAt = now;
    }

    private static string HashPin(PersonId personId, string pin) => AccessCodes.Hash(Salted(personId, pin));

    private static string Salted(PersonId personId, string pin) => personId.ToString() + ":" + pin;
}

/// <summary>Pseudonymisierter Fehlversuch am Kiosk (Zugang 6.2): Gerät und Hash der Kennung, nie die Kennung selbst.</summary>
public sealed class KioskFailedAttempt : ITenantOwned
{
    private KioskFailedAttempt(TenantId tenantId, Guid id, Guid deviceId, string kioskIdHash, DateTimeOffset attemptedAt)
    {
        TenantId = tenantId;
        Id = id;
        DeviceId = deviceId;
        KioskIdHash = kioskIdHash;
        AttemptedAt = attemptedAt;
    }

    public TenantId TenantId { get; }

    public Guid Id { get; }

    public Guid DeviceId { get; }

    public string KioskIdHash { get; }

    public DateTimeOffset AttemptedAt { get; }

    public static KioskFailedAttempt Record(TenantId tenantId, Guid deviceId, string kioskId, DateTimeOffset now) =>
        new(tenantId, Guid.CreateVersion7(), deviceId, PseudonymFor(tenantId, kioskId), now);

    public static string PseudonymFor(TenantId tenantId, string kioskId) => AccessCodes.Hash(tenantId.ToString() + ":" + kioskId);
}

/// <summary>Drosselung am Kiosk (Zugang 6.2, A-018): Kennung nach fünf, Gerät nach 20 Fehlversuchen in 15 Minuten je 15 Minuten gesperrt.</summary>
public static class KioskThrottle
{
    public const int KioskIdLimit = 5;
    public const int DeviceLimit = 20;

    public static TimeSpan Window { get; } = TimeSpan.FromMinutes(15);

    public static TimeSpan LockDuration { get; } = TimeSpan.FromMinutes(15);

    public enum Verdict
    {
        Open = 0,
        KioskIdLocked = 1,
        DeviceLocked = 2,
    }

    /// <summary>
    /// Bewertet die Fehlversuche des Geräts im Fenster: Zeitpunkte aller Fehlversuche am Gerät und davon die der Kennung.
    /// Eine Sperre gilt 15 Minuten ab dem Fehlversuch, der die Grenze erreicht hat.
    /// </summary>
    public static Verdict Evaluate(IReadOnlyList<DateTimeOffset> deviceFailures, IReadOnlyList<DateTimeOffset> kioskIdFailures, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(deviceFailures);
        ArgumentNullException.ThrowIfNull(kioskIdFailures);
        if (LockedUntil(deviceFailures, DeviceLimit, now) is not null)
        {
            return Verdict.DeviceLocked;
        }

        return LockedUntil(kioskIdFailures, KioskIdLimit, now) is not null ? Verdict.KioskIdLocked : Verdict.Open;
    }

    /// <summary>Ende der Sperre oder <c>null</c>, wenn im Fenster weniger Fehlversuche als die Grenze liegen.</summary>
    public static DateTimeOffset? LockedUntil(IReadOnlyList<DateTimeOffset> failures, int limit, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(failures);
        var recent = failures.Where(f => f > now - Window && f <= now).OrderBy(f => f).ToList();
        if (recent.Count < limit)
        {
            return null;
        }

        var lockedUntil = recent[^1] + LockDuration;
        return lockedUntil > now ? lockedUntil : null;
    }
}
