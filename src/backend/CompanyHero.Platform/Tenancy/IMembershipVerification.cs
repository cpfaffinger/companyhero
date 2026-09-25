namespace CompanyHero.Platform.Tenancy;

/// <summary>
/// Ergebnis der Mitgliedschaftsprüfung: Ist die Person aktives Mitglied eines aktiven Tenants, und welche Rollen hält sie?
/// </summary>
public sealed record MembershipVerdict(bool Active, IReadOnlySet<string> Roles)
{
    public static MembershipVerdict Denied { get; } = new(false, new HashSet<string>(StringComparer.Ordinal));
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
}
