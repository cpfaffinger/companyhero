namespace CompanyHero.Modules.Organisation.Domain;

/// <summary>Rollenkatalog (Organisation 4.1, A-036). Rollen werden nur von Organisation geführt, nie aus Anbieterclaims.</summary>
public static class Role
{
    public const string OperatorAdmin = "operator_admin";
    public const string OperatorSupport = "operator_support";
    public const string PartnerAdmin = "partner_admin";
    public const string TenantAdmin = "tenant_admin";
    public const string ProgrammeManager = "programme_manager";
    public const string Editor = "editor";
    public const string Insight = "insight";
    public const string HealthAmbassador = "health_ambassador";
    public const string Member = "member";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        OperatorAdmin, OperatorSupport, PartnerAdmin, TenantAdmin, ProgrammeManager, Editor, Insight, HealthAmbassador, Member,
    };

    /// <summary>Rollen, die eine Person innerhalb eines Tenants halten kann.</summary>
    public static IReadOnlySet<string> TenantRoles { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        TenantAdmin, ProgrammeManager, Editor, Insight, HealthAmbassador, Member,
    };

    /// <summary>
    /// Funktionsrollen des Arbeitgebers, die unter Klarnamen handeln (Organisation 4.1). Sie erhalten nie individuelle
    /// Aktivitätswerte anderer Personen (Datenschutz 2 Regel 1, A-021), unabhängig von deren Sichtbarkeitsstufe.
    /// </summary>
    public static IReadOnlySet<string> EmployerRoles { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        TenantAdmin, ProgrammeManager, Editor, Insight,
    };

    public static bool IsTenantRole(string role) => TenantRoles.Contains(role);

    public static bool IsEmployerRole(string role) => EmployerRoles.Contains(role);
}
