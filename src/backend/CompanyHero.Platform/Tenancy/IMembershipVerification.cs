namespace CompanyHero.Platform.Tenancy;

/// <summary>
/// Ergebnis der Mitgliedschaftsprüfung: Ist die Person aktives Mitglied eines aktiven Tenants, und welche Rollen hält sie?
/// </summary>
/// <param name="Active">Aktives Mitglied eines Tenants, der Zugang gewährt.</param>
/// <param name="Roles">Rollen laut Rollenkatalog.</param>
/// <param name="ReadOnly">Nach der Kündigung: nur lesender Zugriff für Tenant-Admin und Einsichtsrolle (Organisation 1.3).</param>
/// <param name="DeniedReason">Neutraler Grund einer Ablehnung, etwa <c>tenant_suspended</c>; ohne Angabe „keine Mitgliedschaft“.</param>
public sealed record MembershipVerdict(bool Active, IReadOnlySet<string> Roles, bool ReadOnly = false, string? DeniedReason = null)
{
    public static MembershipVerdict Denied { get; } = new(false, new HashSet<string>(StringComparer.Ordinal));

    public static MembershipVerdict DeniedBecause(string reason) => new(false, new HashSet<string>(StringComparer.Ordinal), false, reason);
}

/// <summary>
/// Der Tenant wird aus geprüfter Sitzung und Mitgliedschaft ermittelt (Backend 5.1 Nr. 1). Die Sitzung liefert Tenant
/// und Person; ob diese Person dort aktives Mitglied ist und welche Rollen sie hält, beantwortet ausschließlich die
/// Domäne Organisation über diese Schnittstelle. Rollen kommen nie aus Anbieterclaims (Domänenkarte 5).
/// Die Prüfung läuft bereits im provisorischen Tenant-Kontext, weil die Mitgliedschaft selbst unter RLS liegt.
/// </summary>
public interface IMembershipVerification
{
    Task<MembershipVerdict> VerifyAsync(TenantId tenantId, PersonId personId, CancellationToken cancellationToken);

    /// <summary>Kiosk-Gerätesitzung ohne Person: Gewährt der Tenant Mitgliedern derzeit Zugang (Organisation 1.3)?</summary>
    Task<bool> VerifyTenantAsync(TenantId tenantId, CancellationToken cancellationToken);
}
