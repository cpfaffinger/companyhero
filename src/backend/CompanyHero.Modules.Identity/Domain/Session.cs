using CompanyHero.Platform.Tenancy;

namespace CompanyHero.Modules.Identity.Domain;

/// <summary>Laufzeiten je Sitzungsart (Zugang 5, A-017, A-005).</summary>
public static class SessionPolicy
{
    public static TimeSpan MemberSliding { get; } = TimeSpan.FromDays(30);

    public static TimeSpan MemberAbsolute { get; } = TimeSpan.FromDays(180);

    public static TimeSpan PrivilegedSliding { get; } = TimeSpan.FromHours(8);

    public static TimeSpan PrivilegedAbsolute { get; } = TimeSpan.FromHours(24);

    public static TimeSpan KioskPersonAbsolute { get; } = TimeSpan.FromMinutes(10);

    public const int KioskIdleDefaultSeconds = 60;
    public const int KioskIdleMinSeconds = 30;
    public const int KioskIdleMaxSeconds = 120;

    /// <summary>Gerätegeheimnis rotiert alle 30 Tage automatisch (Zugang 6.1).</summary>
    public static TimeSpan KioskDeviceSecretRotation { get; } = TimeSpan.FromDays(30);

    /// <summary>Gleitende Verlängerung und absolutes Ende je Art; die Gerätesitzung hat keines von beiden.</summary>
    public static (TimeSpan? Sliding, TimeSpan? Absolute) For(SessionKind kind, int kioskIdleSeconds = KioskIdleDefaultSeconds) => kind switch
    {
        SessionKind.Member => (MemberSliding, MemberAbsolute),
        SessionKind.Privileged => (PrivilegedSliding, PrivilegedAbsolute),
        SessionKind.KioskPerson => (TimeSpan.FromSeconds(Math.Clamp(kioskIdleSeconds, KioskIdleMinSeconds, KioskIdleMaxSeconds)), KioskPersonAbsolute),
        SessionKind.KioskDevice => (null, null),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}

/// <summary>
/// Serverseitige Sitzung in PostgreSQL (A-007, A-017): das Cookie trägt nur ein zufälliges Geheimnis, die Zeile dessen
/// Hash. Widerruf wirkt beim nächsten Request über alle Instanzen. Gleitendes und absolutes Ende je Art; die
/// Kiosk-Personensitzung endet zusätzlich mit ihrer Gerätesitzung (A-005).
/// </summary>
public sealed class Session : ITenantOwned
{
    // EF Core: Materialisierung ohne Konstruktorbindung.
    private Session()
    {
        TokenHash = string.Empty;
        CsrfToken = string.Empty;
    }

    private Session(TenantId tenantId, Guid id, string tokenHash, string csrfToken, PersonId? personId, SessionKind kind, DateTimeOffset now, DateTimeOffset? slidingUntil, DateTimeOffset? absoluteUntil, Guid? kioskDeviceId)
    {
        TenantId = tenantId;
        Id = id;
        TokenHash = tokenHash;
        CsrfToken = csrfToken;
        PersonId = personId;
        Kind = kind;
        CreatedAt = now;
        AuthenticatedAt = now;
        LastSeenAt = now;
        SlidingUntil = slidingUntil;
        AbsoluteUntil = absoluteUntil;
        KioskDeviceId = kioskDeviceId;
    }

    public TenantId TenantId { get; }

    public Guid Id { get; }

    public string TokenHash { get; }

    /// <summary>Zufälliges Token für den CSRF-Schutz (A-007): als lesbares Cookie gesetzt, als Header zurückerwartet.</summary>
    public string CsrfToken { get; }

    public PersonId? PersonId { get; }

    public SessionKind Kind { get; }

    public DateTimeOffset CreatedAt { get; }

    /// <summary>Zeitpunkt der Anmeldung; Grundlage für die frische Anmeldung sensibler Aktionen (Zugang 3.3).</summary>
    public DateTimeOffset AuthenticatedAt { get; }

    public DateTimeOffset LastSeenAt { get; private set; }

    public DateTimeOffset? SlidingUntil { get; private set; }

    public DateTimeOffset? AbsoluteUntil { get; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public Guid? KioskDeviceId { get; }

    /// <summary>Neue Sitzung; das Geheimnis wird genau einmal zurückgegeben und nur als Hash gespeichert.</summary>
    /// <param name="csrfToken">
    /// Vorgegebenes CSRF-Token; Kiosk-Personensitzungen übernehmen das Token der Gerätesitzung, damit das Gerät nach dem Ende
    /// der Personensitzung (Auto-Logout, Abmelden, Personenwechsel) ohne Neuladen weiter zustandsändernd arbeiten kann.
    /// </param>
    public static (Session Session, string Token) Issue(TenantId tenantId, PersonId? personId, SessionKind kind, DateTimeOffset now, Guid? kioskDeviceId = null, int kioskIdleSeconds = SessionPolicy.KioskIdleDefaultSeconds, string? csrfToken = null)
    {
        if (kind == SessionKind.KioskPerson && kioskDeviceId is null)
        {
            throw new ArgumentException("Eine Kiosk-Personensitzung entsteht nur innerhalb einer Gerätesitzung (A-005).", nameof(kioskDeviceId));
        }

        if (kind != SessionKind.KioskDevice && personId is null)
        {
            throw new ArgumentException("Nur die Gerätesitzung hat keine Person.", nameof(personId));
        }

        var (sliding, absolute) = SessionPolicy.For(kind, kioskIdleSeconds);
        var token = AccessCodes.NewSecret();
        var session = new Session(tenantId, Guid.CreateVersion7(), AccessCodes.Hash(token), csrfToken ?? AccessCodes.NewSecret(), personId, kind, now, sliding is null ? null : now + sliding, absolute is null ? null : now + absolute, kioskDeviceId);
        return (session, token);
    }

    public bool IsValidAt(DateTimeOffset now) =>
        RevokedAt is null && (SlidingUntil is null || now < SlidingUntil) && (AbsoluteUntil is null || now < AbsoluteUntil);

    /// <summary>Aktivität verlängert gleitend, nie über das absolute Ende hinaus (Zugang 5).</summary>
    public void Touch(DateTimeOffset now, int kioskIdleSeconds = SessionPolicy.KioskIdleDefaultSeconds)
    {
        LastSeenAt = now;
        var (sliding, _) = SessionPolicy.For(Kind, kioskIdleSeconds);
        if (sliding is not null)
        {
            var next = now + sliding.Value;
            SlidingUntil = AbsoluteUntil is not null && next > AbsoluteUntil ? AbsoluteUntil : next;
        }
    }

    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;

    /// <summary>Frische Anmeldung für sensible Aktionen (Zugang 3.3): nicht älter als 15 Minuten.</summary>
    public bool IsFreshAt(DateTimeOffset now, TimeSpan maxAge) => now - AuthenticatedAt <= maxAge;
}

/// <summary>Sitzungsart aus den Rollen laut Organisation (Zugang 5): privilegierte Rollen erhalten kurze Sitzungen.</summary>
public static class AccessRules
{
    public static IReadOnlySet<string> PrivilegedRoles { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "tenant_admin", "programme_manager", "partner_admin", "operator_admin", "operator_support",
    };

    /// <summary>Funktionsrollen handeln unter Klarnamen (Zugang 2.3).</summary>
    public static IReadOnlySet<string> RealNameRoles { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "tenant_admin", "programme_manager", "editor", "insight", "partner_admin", "operator_admin", "operator_support",
    };

    /// <summary>Rollen, deren Einlösung Passkey oder externen Anbieter voraussetzt (Zugang 2.3, 3.3).</summary>
    public static IReadOnlySet<string> PhishingResistantRoles { get; } = PrivilegedRoles;

    public static bool IsPrivileged(IEnumerable<string> roles) => roles.Any(PrivilegedRoles.Contains);

    public static SessionKind SessionKindFor(IEnumerable<string> roles) => IsPrivileged(roles) ? SessionKind.Privileged : SessionKind.Member;

    /// <summary>Magic-Link allein ist für privilegierte Rollen kein Anmeldeweg (Zugang 3.3).</summary>
    public static bool MagicLinkAllowedFor(IEnumerable<string> roles) => !IsPrivileged(roles);

    public static bool RequiresRealName(string role) => RealNameRoles.Contains(role);

    public static bool RequiresPhishingResistantWay(string role) => PhishingResistantRoles.Contains(role);
}
