using CompanyHero.Platform.Tenancy;

namespace CompanyHero.Modules.Identity.Domain;

/// <summary>
/// Beitrittscode (Zugang 2.1, A-014): acht Zeichen, bestimmt den Tenant serverseitig, 180 Tage Standardgültigkeit,
/// optionales Nutzungslimit, widerrufbar. Der Code steht im Klartext, weil er im QR-Code und im Aushang erscheint und
/// keine Person, sondern nur den Tenant bezeichnet.
/// </summary>
public sealed class JoinCode : ITenantOwned
{
    public static TimeSpan DefaultLifetime { get; } = TimeSpan.FromDays(180);

    private JoinCode(TenantId tenantId, Guid id, string code, PersonId? createdBy, DateTimeOffset createdAt, DateTimeOffset expiresAt, int? usageLimit)
    {
        TenantId = tenantId;
        Id = id;
        Code = code;
        CreatedBy = createdBy;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
        UsageLimit = usageLimit;
    }

    public TenantId TenantId { get; }

    public Guid Id { get; }

    public string Code { get; }

    public PersonId? CreatedBy { get; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public int? UsageLimit { get; }

    public int UsedCount { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public static JoinCode Create(TenantId tenantId, PersonId? createdBy, DateTimeOffset now, TimeSpan? lifetime = null, int? usageLimit = null)
    {
        if (usageLimit is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(usageLimit));
        }

        return new JoinCode(tenantId, Guid.CreateVersion7(), AccessCodes.NewJoinCode(), createdBy, now, now + (lifetime ?? DefaultLifetime), usageLimit);
    }

    public bool IsRedeemableAt(DateTimeOffset now) =>
        RevokedAt is null && now <= ExpiresAt && (UsageLimit is null || UsedCount < UsageLimit);

    public void Use() => UsedCount++;

    public void Extend(DateTimeOffset newExpiry) => ExpiresAt = newExpiry;

    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;
}

/// <summary>
/// Rollencode (Zugang 2.3, A-014): personalisierter Einmalcode, 14 Tage gültig, an genau eine Rolle und einen Tenant
/// gebunden, widerrufbar. Gespeichert wird nur der Hash; eine eingegebene E-Mail des Ausstellers wird nach Versand nicht
/// gespeichert (die Plattform speichert keine Zuordnung zwischen Aussteller-Wissen und Anzeigename).
/// </summary>
public sealed class RoleCode : ITenantOwned
{
    public static TimeSpan Lifetime { get; } = TimeSpan.FromDays(14);

    private RoleCode(TenantId tenantId, Guid id, string codeHash, string role, PersonId? issuedBy, DateTimeOffset issuedAt)
    {
        TenantId = tenantId;
        Id = id;
        CodeHash = codeHash;
        Role = role;
        IssuedBy = issuedBy;
        IssuedAt = issuedAt;
        ExpiresAt = issuedAt + Lifetime;
    }

    public TenantId TenantId { get; }

    public Guid Id { get; }

    public string CodeHash { get; }

    public string Role { get; }

    public PersonId? IssuedBy { get; }

    public DateTimeOffset IssuedAt { get; }

    public DateTimeOffset ExpiresAt { get; }

    public DateTimeOffset? RedeemedAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public static (RoleCode Entry, string Code) Issue(TenantId tenantId, string role, PersonId? issuedBy, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        var code = AccessCodes.NewRoleCode();
        return (new RoleCode(tenantId, Guid.CreateVersion7(), AccessCodes.Hash(code), role, issuedBy, now), code);
    }

    public bool IsRedeemableAt(DateTimeOffset now) => RedeemedAt is null && RevokedAt is null && now <= ExpiresAt;

    public void Redeem(DateTimeOffset now) => RedeemedAt = now;

    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;
}

/// <summary>Einmaliger Link per E-Mail, 15 Minuten gültig, nicht an das anfordernde Gerät gebunden (Zugang 3.1).</summary>
public sealed class MagicLink : ITenantOwned
{
    // EF Core: Materialisierung ohne Konstruktorbindung.
    private MagicLink()
    {
        TokenHash = string.Empty;
    }

    public static TimeSpan Lifetime { get; } = TimeSpan.FromMinutes(15);

    private MagicLink(TenantId tenantId, Guid id, string tokenHash, PersonId personId, DateTimeOffset now)
    {
        TenantId = tenantId;
        Id = id;
        TokenHash = tokenHash;
        PersonId = personId;
        CreatedAt = now;
        ExpiresAt = now + Lifetime;
    }

    public TenantId TenantId { get; }

    public Guid Id { get; }

    public string TokenHash { get; }

    public PersonId PersonId { get; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset ExpiresAt { get; }

    public DateTimeOffset? UsedAt { get; private set; }

    public static (MagicLink Entry, string Token) Issue(TenantId tenantId, PersonId personId, DateTimeOffset now)
    {
        var token = AccessCodes.NewSecret();
        return (new MagicLink(tenantId, Guid.CreateVersion7(), AccessCodes.Hash(token), personId, now), token);
    }

    public bool IsUsableAt(DateTimeOffset now) => UsedAt is null && now <= ExpiresAt;

    public void Use(DateTimeOffset now) => UsedAt = now;
}

/// <summary>Übertragung vom Kiosk auf das eigene Gerät: einmaliger Link, fünf Minuten gültig (Zugang 6.4).</summary>
public sealed class TransferLink : ITenantOwned
{
    // EF Core: Materialisierung ohne Konstruktorbindung.
    private TransferLink()
    {
        TokenHash = string.Empty;
    }

    public static TimeSpan Lifetime { get; } = TimeSpan.FromMinutes(5);

    private TransferLink(TenantId tenantId, Guid id, string tokenHash, PersonId personId, DateTimeOffset now)
    {
        TenantId = tenantId;
        Id = id;
        TokenHash = tokenHash;
        PersonId = personId;
        CreatedAt = now;
        ExpiresAt = now + Lifetime;
    }

    public TenantId TenantId { get; }

    public Guid Id { get; }

    public string TokenHash { get; }

    public PersonId PersonId { get; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset ExpiresAt { get; }

    public DateTimeOffset? UsedAt { get; private set; }

    public static (TransferLink Entry, string Token) Issue(TenantId tenantId, PersonId personId, DateTimeOffset now)
    {
        var token = AccessCodes.NewSecret();
        return (new TransferLink(tenantId, Guid.CreateVersion7(), AccessCodes.Hash(token), personId, now), token);
    }

    public bool IsUsableAt(DateTimeOffset now) => UsedAt is null && now <= ExpiresAt;

    public void Use(DateTimeOffset now) => UsedAt = now;
}
