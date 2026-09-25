namespace CompanyHero.Platform.Tenancy;

/// <summary>
/// Tenantbezogene Entität: trägt <c>tenant_id NOT NULL</c> (Backend 5.1 Nr. 3). Beim Speichern prüft die
/// Plattforminfrastruktur, dass die Kennung dem aktiven Kontext entspricht; die Datenbank prüft es über RLS erneut.
/// </summary>
public interface ITenantOwned
{
    TenantId TenantId { get; }
}
