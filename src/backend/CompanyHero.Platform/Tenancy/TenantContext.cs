namespace CompanyHero.Platform.Tenancy;

/// <summary>Art des geprüften Kontexts einer Anfrage oder eines Jobs (Backend 5.1, 5.3).</summary>
public enum TenantContextKind
{
    /// <summary>Kontext eines Tenants: Row Level Security beschränkt auf genau diesen Tenant.</summary>
    Tenant,

    /// <summary>
    /// Plattformbereich für begrenzte Operator-Anwendungsfälle (Backend 5.3). Tenantbezogene Tabellen bleiben unsichtbar;
    /// nur Plattformdaten wie Organisationen sind erreichbar. Kein Schalter zum Abschalten der Isolation.
    /// </summary>
    Platform,
}

/// <summary>
/// Art der Sitzung, aus der ein Kontext stammt (Zugang 5, A-017, A-005). Jobs und Systemabläufe haben keine Sitzung.
/// Die Art bestimmt Laufzeiten und den erlaubten Funktionsumfang: Kiosk-Sitzungen erreichen nur Endpunkte, die den
/// Kiosk ausdrücklich zulassen (Zugang 6.3).
/// </summary>
public enum SessionKind
{
    None = 0,

    /// <summary>Mitglied auf persönlichem Gerät: 30 Tage gleitend, 180 Tage absolut.</summary>
    Member = 1,

    /// <summary>Privilegierte Rolle: 8 Stunden gleitend, 24 Stunden absolut; frische Anmeldung für sensible Aktionen.</summary>
    Privileged = 2,

    /// <summary>Kiosk-Gerätesitzung: an einen Tenant gebunden, gewährt keine Personenrechte (A-005).</summary>
    KioskDevice = 3,

    /// <summary>Kiosk-Personensitzung innerhalb einer Gerätesitzung: kurz, ohne Historie und Verwaltung (A-018).</summary>
    KioskPerson = 4,
}

/// <summary>
/// Der geprüfte Kontext genau einer Anfrage oder genau eines Jobs. Unveränderlich; kein global veränderlicher
/// Tenant-Zustand (Backend 5.1 Nr. 2). Wird von der Plattforminfrastruktur gesetzt und von allen Modulen nur gelesen.
/// </summary>
public sealed record TenantContext
{
    private TenantContext(TenantContextKind kind, TenantId? tenantId, PersonId? personId, IReadOnlySet<string> roles, SessionKind session, DateTimeOffset? authenticatedAt, Guid? kioskDeviceId)
    {
        Kind = kind;
        TenantId = tenantId;
        PersonId = personId;
        Roles = roles;
        Session = session;
        AuthenticatedAt = authenticatedAt;
        KioskDeviceId = kioskDeviceId;
    }

    public TenantContextKind Kind { get; }

    /// <summary>Gesetzt für <see cref="TenantContextKind.Tenant"/>.</summary>
    public TenantId? TenantId { get; }

    /// <summary>Die handelnde Person, falls die Anfrage einer Person zugeordnet ist. Jobs und Kiosk-Gerätesitzungen haben keine Person.</summary>
    public PersonId? PersonId { get; }

    /// <summary>Rollen der handelnden Person laut Organisation (Rollenkatalog), nie aus Anbieterclaims.</summary>
    public IReadOnlySet<string> Roles { get; }

    /// <summary>Art der Sitzung, aus der der Kontext stammt; <see cref="SessionKind.None"/> für Jobs und Systemabläufe.</summary>
    public SessionKind Session { get; }

    /// <summary>Zeitpunkt der Anmeldung dieser Sitzung; Grundlage für „frische Anmeldung“ bei sensiblen Aktionen (Zugang 3.3).</summary>
    public DateTimeOffset? AuthenticatedAt { get; }

    /// <summary>Kiosk-Gerät der Sitzung (Geräte- und Personensitzung).</summary>
    public Guid? KioskDeviceId { get; }

    /// <summary>Kontext eines Jobs oder eines Systemablaufs im Tenant, ohne handelnde Person.</summary>
    public static TenantContext ForTenant(TenantId tenantId) =>
        new(TenantContextKind.Tenant, tenantId, null, new HashSet<string>(StringComparer.Ordinal), SessionKind.None, null, null);

    /// <summary>Kontext einer angemeldeten Person mit ihren geprüften Rollen.</summary>
    public static TenantContext ForPerson(TenantId tenantId, PersonId personId, IEnumerable<string> roles) =>
        ForPerson(tenantId, personId, roles, SessionKind.Member, null, null);

    /// <summary>Kontext einer angemeldeten Person mit Sitzungsart, Anmeldezeitpunkt und gegebenenfalls Kiosk-Gerät.</summary>
    public static TenantContext ForPerson(TenantId tenantId, PersonId personId, IEnumerable<string> roles, SessionKind session, DateTimeOffset? authenticatedAt, Guid? kioskDeviceId) =>
        new(TenantContextKind.Tenant, tenantId, personId, new HashSet<string>(roles, StringComparer.Ordinal), session, authenticatedAt, kioskDeviceId);

    /// <summary>Kontext einer Kiosk-Gerätesitzung: Tenant bekannt, keine Person, keine Rollen (A-005).</summary>
    public static TenantContext ForKioskDevice(TenantId tenantId, Guid kioskDeviceId) =>
        new(TenantContextKind.Tenant, tenantId, null, new HashSet<string>(StringComparer.Ordinal), SessionKind.KioskDevice, null, kioskDeviceId);

    /// <summary>Plattformkontext für Operator-Anwendungsfälle.</summary>
    public static TenantContext ForPlatform(PersonId? operatorPerson = null) =>
        new(TenantContextKind.Platform, null, operatorPerson, new HashSet<string>(StringComparer.Ordinal), SessionKind.None, null, null);

    public bool HasRole(string role) => Roles.Contains(role);

    /// <summary>Kiosk-Sitzung (Gerät oder Person): eingeschränkter Funktionsumfang nach Zugang 6.3.</summary>
    public bool IsKiosk => Session is SessionKind.KioskDevice or SessionKind.KioskPerson;

    /// <summary>Der Tenant dieses Kontexts; wirft, wenn der Kontext kein Tenant-Kontext ist.</summary>
    public TenantId RequireTenant() =>
        TenantId ?? throw new TenantContextMissingException("Der Kontext ist kein Tenant-Kontext.");

    /// <summary>Die handelnde Person; wirft, wenn der Kontext keiner Person zugeordnet ist.</summary>
    public PersonId RequirePerson() =>
        PersonId ?? throw new TenantContextMissingException("Der Kontext ist keiner Person zugeordnet.");
}
