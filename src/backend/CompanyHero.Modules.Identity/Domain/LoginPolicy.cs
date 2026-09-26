using CompanyHero.Platform.Tenancy;

namespace CompanyHero.Modules.Identity.Domain;

/// <summary>
/// Festlegung der Anmeldewege durch den Tenant (Zugang 3.4, A-015): je Weg aktivieren oder deaktivieren, Anbieter
/// erzwingen. Mindestens ein Weg außer dem Kiosk bleibt aktiv; Voreinstellung sind alle Wege. Ein deaktivierter Weg
/// bleibt für Personen, die nur ihn besitzen, 30 Tage nutzbar. Passkey bleibt für privilegierte Rollen immer erlaubt.
/// </summary>
public sealed class LoginPolicy : ITenantOwned
{
    // EF Core: Materialisierung ohne Konstruktorbindung.
    private LoginPolicy()
    {
    }

    public static TimeSpan GracePeriod { get; } = TimeSpan.FromDays(30);

    private readonly List<string> _disabledProviderKeys = [];
    private readonly List<string> _forcedProviderKeys = [];

    private LoginPolicy(TenantId tenantId, DateTimeOffset now)
    {
        TenantId = tenantId;
        UpdatedAt = now;
    }

    public TenantId TenantId { get; }

    public bool MagicLinkEnabled { get; private set; } = true;

    public DateTimeOffset? MagicLinkDisabledAt { get; private set; }

    public bool PasskeyEnabled { get; private set; } = true;

    public DateTimeOffset? PasskeyDisabledAt { get; private set; }

    public bool KioskEnabled { get; private set; } = true;

    public DateTimeOffset? KioskDisabledAt { get; private set; }

    /// <summary>Plattformweite oder tenant-eigene Anbieter, die der Tenant für seine Mitglieder abgeschaltet hat.</summary>
    public IReadOnlyList<string> DisabledProviderKeys => _disabledProviderKeys;

    /// <summary>Erzwungene Anbieter für Mitglieder: Beitritt nur über sie; Kiosk-Beitritt entfällt (Zugang 3.4).</summary>
    public IReadOnlyList<string> ForcedProviderKeys => _forcedProviderKeys;

    /// <summary>Auto-Logout der Kiosk-Personensitzung ohne Eingabe, 30 bis 120 Sekunden (A-005).</summary>
    public int KioskIdleSeconds { get; private set; } = SessionPolicy.KioskIdleDefaultSeconds;

    public DateTimeOffset UpdatedAt { get; private set; }

    public static LoginPolicy Default(TenantId tenantId, DateTimeOffset now) => new(tenantId, now);

    public bool ProviderForced => _forcedProviderKeys.Count > 0;

    /// <summary>Beitritt am Kiosk entfällt bei erzwungenem Anbieter; die Kiosk-Nutzung bleibt (Zugang 3.4).</summary>
    public bool KioskJoinAllowed => KioskEnabled && !ProviderForced;

    public bool ProviderAllowed(string providerKey) => !_disabledProviderKeys.Contains(providerKey, StringComparer.Ordinal) && (!ProviderForced || _forcedProviderKeys.Contains(providerKey, StringComparer.Ordinal));

    /// <summary>Weg offen: aktiv, oder in der 30-Tage-Frist für eine Person, die nur diesen Weg besitzt.</summary>
    public bool WayOpen(LoginWay way, bool personHasOnlyThisWay, DateTimeOffset now) => way switch
    {
        LoginWay.MagicLink => Open(MagicLinkEnabled, MagicLinkDisabledAt, personHasOnlyThisWay, now),
        LoginWay.Passkey or LoginWay.RecoveryCode => Open(PasskeyEnabled, PasskeyDisabledAt, personHasOnlyThisWay, now),
        LoginWay.Kiosk => Open(KioskEnabled, KioskDisabledAt, personHasOnlyThisWay, now),
        LoginWay.ExternalProvider => true,
        _ => false,
    };

    /// <summary>Neue Festlegung; wirft, wenn kein Weg außer dem Kiosk aktiv bliebe oder der Kiosk-Auto-Logout außerhalb 30 bis 120 Sekunden liegt.</summary>
    public void Update(bool magicLink, bool passkey, bool kiosk, IEnumerable<string> disabledProviderKeys, IEnumerable<string> forcedProviderKeys, int kioskIdleSeconds, int availableProviderCount, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(disabledProviderKeys);
        ArgumentNullException.ThrowIfNull(forcedProviderKeys);
        var disabled = disabledProviderKeys.Distinct(StringComparer.Ordinal).ToList();
        var forced = forcedProviderKeys.Distinct(StringComparer.Ordinal).ToList();
        if (forced.Any(f => disabled.Contains(f, StringComparer.Ordinal)))
        {
            throw new LoginPolicyException("Ein erzwungener Anbieter kann nicht zugleich deaktiviert sein.");
        }

        var providersRemaining = availableProviderCount - disabled.Count;
        if (!magicLink && !passkey && providersRemaining <= 0 && forced.Count == 0)
        {
            throw new LoginPolicyException("Mindestens ein Anmeldeweg außer dem Kiosk bleibt aktiv (Zugang 3.4).");
        }

        if (kioskIdleSeconds is < SessionPolicy.KioskIdleMinSeconds or > SessionPolicy.KioskIdleMaxSeconds)
        {
            throw new LoginPolicyException("Der Auto-Logout am Kiosk liegt zwischen 30 und 120 Sekunden (A-005).");
        }

        MagicLinkDisabledAt = Transition(MagicLinkEnabled, magicLink, MagicLinkDisabledAt, now);
        PasskeyDisabledAt = Transition(PasskeyEnabled, passkey, PasskeyDisabledAt, now);
        KioskDisabledAt = Transition(KioskEnabled, kiosk, KioskDisabledAt, now);
        MagicLinkEnabled = magicLink;
        PasskeyEnabled = passkey;
        KioskEnabled = kiosk;
        _disabledProviderKeys.Clear();
        _disabledProviderKeys.AddRange(disabled);
        _forcedProviderKeys.Clear();
        _forcedProviderKeys.AddRange(forced);
        KioskIdleSeconds = kioskIdleSeconds;
        UpdatedAt = now;
    }

    private static bool Open(bool enabled, DateTimeOffset? disabledAt, bool personHasOnlyThisWay, DateTimeOffset now) =>
        enabled || (personHasOnlyThisWay && disabledAt is not null && now < disabledAt.Value + GracePeriod);

    private static DateTimeOffset? Transition(bool wasEnabled, bool enabled, DateTimeOffset? disabledAt, DateTimeOffset now) =>
        enabled ? null : (wasEnabled || disabledAt is null ? now : disabledAt);
}

public sealed class LoginPolicyException : InvalidOperationException
{
    public LoginPolicyException()
    {
    }

    public LoginPolicyException(string message)
        : base(message)
    {
    }

    public LoginPolicyException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
