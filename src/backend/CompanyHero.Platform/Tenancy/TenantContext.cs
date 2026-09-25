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
/// Der geprüfte Kontext genau einer Anfrage oder genau eines Jobs. Unveränderlich; kein global veränderlicher
/// Tenant-Zustand (Backend 5.1 Nr. 2). Wird von der Plattforminfrastruktur gesetzt und von allen Modulen nur gelesen.
/// </summary>
public sealed record TenantContext
{
    private TenantContext(TenantContextKind kind, TenantId? tenantId, PersonId? personId, IReadOnlySet<string> roles)
    {
        Kind = kind;
        TenantId = tenantId;
        PersonId = personId;
        Roles = roles;
    }

    public TenantContextKind Kind { get; }

    /// <summary>Gesetzt für <see cref="TenantContextKind.Tenant"/>.</summary>
    public TenantId? TenantId { get; }

    /// <summary>Die handelnde Person, falls die Anfrage einer Person zugeordnet ist. Jobs haben keine Person.</summary>
    public PersonId? PersonId { get; }

    /// <summary>Rollen der handelnden Person laut Organisation (Rollenkatalog), nie aus Anbieterclaims.</summary>
    public IReadOnlySet<string> Roles { get; }

    /// <summary>Kontext eines Jobs oder eines Systemablaufs im Tenant, ohne handelnde Person.</summary>
    public static TenantContext ForTenant(TenantId tenantId) =>
        new(TenantContextKind.Tenant, tenantId, null, new HashSet<string>(StringComparer.Ordinal));

    /// <summary>Kontext einer angemeldeten Person mit ihren geprüften Rollen.</summary>
    public static TenantContext ForPerson(TenantId tenantId, PersonId personId, IEnumerable<string> roles) =>
        new(TenantContextKind.Tenant, tenantId, personId, new HashSet<string>(roles, StringComparer.Ordinal));

    /// <summary>Plattformkontext für Operator-Anwendungsfälle.</summary>
    public static TenantContext ForPlatform(PersonId? operatorPerson = null) =>
        new(TenantContextKind.Platform, null, operatorPerson, new HashSet<string>(StringComparer.Ordinal));

    public bool HasRole(string role) => Roles.Contains(role);

    /// <summary>Der Tenant dieses Kontexts; wirft, wenn der Kontext kein Tenant-Kontext ist.</summary>
    public TenantId RequireTenant() =>
        TenantId ?? throw new TenantContextMissingException("Der Kontext ist kein Tenant-Kontext.");

    /// <summary>Die handelnde Person; wirft, wenn der Kontext keiner Person zugeordnet ist.</summary>
    public PersonId RequirePerson() =>
        PersonId ?? throw new TenantContextMissingException("Der Kontext ist keiner Person zugeordnet.");
}
