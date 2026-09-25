using CompanyHero.Platform.Tenancy;

namespace CompanyHero.Modules.Identity.Domain;

/// <summary>
/// Person eines Tenants (Zugang 4): trägt den frei gewählten Anzeigenamen. Kein Klarname, keine E-Mail und kein
/// Geburtsdatum sind Voraussetzung (Datenschutz 2 Regel 10). Anmeldewege folgen in Stufe 4.
/// </summary>
public sealed class Person : ITenantOwned
{
    private Person(TenantId tenantId, PersonId id, string displayName, DateTimeOffset createdAt)
    {
        TenantId = tenantId;
        Id = id;
        DisplayName = displayName;
        CreatedAt = createdAt;
    }

    public TenantId TenantId { get; }

    public PersonId Id { get; }

    public string DisplayName { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public static Person Create(TenantId tenantId, string displayName, DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        return new Person(tenantId, PersonId.New(), displayName.Trim(), createdAt);
    }
}
