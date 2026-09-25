namespace CompanyHero.Platform.Tenancy;

/// <summary>Kennung eines Tenants. Zeitlich sortierbar (UUIDv7), keine laufende Nummer (Backend 4).</summary>
public readonly record struct TenantId(Guid Value)
{
    public static TenantId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString("D");
}
