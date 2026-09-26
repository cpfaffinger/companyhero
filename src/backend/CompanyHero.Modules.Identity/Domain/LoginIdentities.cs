using CompanyHero.Platform.Tenancy;

namespace CompanyHero.Modules.Identity.Domain;

/// <summary>Anmeldewege einer Person (Zugang 3.1).</summary>
public enum LoginWay
{
    ExternalProvider = 1,
    MagicLink = 2,
    Passkey = 3,
    RecoveryCode = 4,
    Kiosk = 5,
}

/// <summary>Passkey (WebAuthn): Credential-ID, öffentlicher Schlüssel, Zähler, Gerätename (Zugang 3.1). Mehrere je Person.</summary>
public sealed class Passkey : ITenantOwned
{
    private Passkey(TenantId tenantId, Guid id, PersonId personId, byte[] credentialId, byte[] publicKey, uint signCount, Guid aaGuid, string deviceName, DateTimeOffset createdAt)
    {
        TenantId = tenantId;
        Id = id;
        PersonId = personId;
        CredentialId = credentialId;
        PublicKey = publicKey;
        SignCount = signCount;
        AaGuid = aaGuid;
        DeviceName = deviceName;
        CreatedAt = createdAt;
    }

    public TenantId TenantId { get; }

    public Guid Id { get; }

    public PersonId PersonId { get; }

    public byte[] CredentialId { get; }

    public byte[] PublicKey { get; }

    public uint SignCount { get; private set; }

    public Guid AaGuid { get; }

    public string DeviceName { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset? LastUsedAt { get; private set; }

    public static Passkey Register(TenantId tenantId, PersonId personId, byte[] credentialId, byte[] publicKey, uint signCount, Guid aaGuid, string deviceName, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(credentialId);
        ArgumentNullException.ThrowIfNull(publicKey);
        return new Passkey(tenantId, Guid.CreateVersion7(), personId, credentialId, publicKey, signCount, aaGuid, string.IsNullOrWhiteSpace(deviceName) ? "Passkey" : deviceName.Trim(), now);
    }

    /// <summary>Nach erfolgreicher Anmeldung: Zähler übernehmen (Klonschutz nach WebAuthn 6.1.1 prüft die Bibliothek).</summary>
    public void Used(uint signCount, DateTimeOffset now)
    {
        SignCount = signCount;
        LastUsedAt = now;
    }
}

/// <summary>Ausdrücklich für den Magic-Link hinterlegte E-Mail (Zugang 3.1); höchstens eine je Person (A-016).</summary>
public sealed class EmailLogin : ITenantOwned
{
    private EmailLogin(TenantId tenantId, PersonId personId, string email, DateTimeOffset createdAt)
    {
        TenantId = tenantId;
        PersonId = personId;
        Email = email;
        CreatedAt = createdAt;
    }

    public TenantId TenantId { get; }

    public PersonId PersonId { get; }

    public string Email { get; }

    public DateTimeOffset CreatedAt { get; }

    public static EmailLogin Add(TenantId tenantId, PersonId personId, string email, DateTimeOffset now) =>
        new(tenantId, personId, Normalize(email), now);

    /// <summary>Kleinschreibung und Leerraum: dieselbe Adresse ergibt denselben Indexeintrag.</summary>
    public static string Normalize(string email)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        var trimmed = email.Trim().ToLowerInvariant();
        if (trimmed.IndexOf('@', StringComparison.Ordinal) is var at && (at <= 0 || at == trimmed.Length - 1 || trimmed.Contains(' ', StringComparison.Ordinal)))
        {
            throw new ArgumentException("Keine gültige E-Mail-Adresse.", nameof(email));
        }

        return trimmed;
    }
}

/// <summary>Verknüpfung mit einem externen Anbieter: nur der Hash aus Issuer und Subject (A-007 Claim-Minimierung).</summary>
public sealed class ExternalLogin : ITenantOwned
{
    private ExternalLogin(TenantId tenantId, Guid id, PersonId personId, string providerKey, string subjectHash, DateTimeOffset createdAt)
    {
        TenantId = tenantId;
        Id = id;
        PersonId = personId;
        ProviderKey = providerKey;
        SubjectHash = subjectHash;
        CreatedAt = createdAt;
    }

    public TenantId TenantId { get; }

    public Guid Id { get; }

    public PersonId PersonId { get; }

    /// <summary>„microsoft“, „google“ oder die Kennung eines tenant-eigenen Anbieters.</summary>
    public string ProviderKey { get; }

    public string SubjectHash { get; }

    public DateTimeOffset CreatedAt { get; }

    public static ExternalLogin Link(TenantId tenantId, PersonId personId, string providerKey, string subjectHash, DateTimeOffset now) =>
        new(tenantId, Guid.CreateVersion7(), personId, providerKey, subjectHash, now);
}

/// <summary>Wiederherstellungscode: einmalig, nach Verwendung sofort ersetzt (Zugang 3.1); gespeichert wird der Hash.</summary>
public sealed class RecoveryCode : ITenantOwned
{
    private RecoveryCode(TenantId tenantId, PersonId personId, string codeHash, DateTimeOffset createdAt)
    {
        TenantId = tenantId;
        PersonId = personId;
        CodeHash = codeHash;
        CreatedAt = createdAt;
    }

    public TenantId TenantId { get; }

    public PersonId PersonId { get; }

    public string CodeHash { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Erzeugt den Eintrag und liefert den Klartext genau einmal zur Anzeige.</summary>
    public static (RecoveryCode Entry, string Code) Issue(TenantId tenantId, PersonId personId, DateTimeOffset now)
    {
        var code = AccessCodes.NewRecoveryCode();
        return (new RecoveryCode(tenantId, personId, AccessCodes.Hash(code), now), code);
    }

    public bool Matches(string input) => AccessCodes.HashEquals(CodeHash, AccessCodes.Normalize(input));

    /// <summary>Nach Verwendung: neuer Code, alter ungültig; Klartext einmal zur Anzeige.</summary>
    public string Renew(DateTimeOffset now)
    {
        var code = AccessCodes.NewRecoveryCode();
        CodeHash = AccessCodes.Hash(code);
        CreatedAt = now;
        return code;
    }
}

/// <summary>Art eines Eintrags im plattformweiten Identitätsindex.</summary>
public enum IdentityKind
{
    External = 1,
    Email = 2,
}

/// <summary>
/// Plattformweiter Identitätsindex (Zugang 3.2, A-015): ordnet den Hash aus Issuer und Subject beziehungsweise der
/// E-Mail genau einer aktiven Person zu. Der Hash ist Primärschlüssel; eine zweite aktive Person mit derselben Identität
/// scheitert an der Datenbank (Zugang 10.4). Der Austritt entfernt den Eintrag (Zugang 8).
/// </summary>
public sealed class IdentityIndexEntry : ITenantOwned
{
    private IdentityIndexEntry(string hash, IdentityKind kind, TenantId tenantId, PersonId personId, DateTimeOffset createdAt)
    {
        Hash = hash;
        Kind = kind;
        TenantId = tenantId;
        PersonId = personId;
        CreatedAt = createdAt;
    }

    public string Hash { get; }

    public IdentityKind Kind { get; }

    public TenantId TenantId { get; }

    public PersonId PersonId { get; }

    public DateTimeOffset CreatedAt { get; }

    public static IdentityIndexEntry Create(string hash, IdentityKind kind, TenantId tenantId, PersonId personId, DateTimeOffset now) =>
        new(hash, kind, tenantId, personId, now);

    /// <summary>Hash aus Issuer und Subject; Name und E-Mail des Anbieters gehen nie in den Datenbestand (A-007).</summary>
    public static string HashExternal(string issuer, string subject)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        return "ext:" + AccessCodes.Hash(issuer.TrimEnd('/') + "\n" + subject);
    }

    public static string HashEmail(string email) => "mail:" + AccessCodes.Hash(EmailLogin.Normalize(email));
}
