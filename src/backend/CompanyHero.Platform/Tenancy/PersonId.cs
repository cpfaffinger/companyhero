namespace CompanyHero.Platform.Tenancy;

/// <summary>
/// Kennung einer Person innerhalb ihres Tenants. Module referenzieren Personen ausschließlich über diese Kennung,
/// nie über fremde Tabellen (Domänenkarte 2: „Personen werden nur über ihre Kennung referenziert“).
/// </summary>
public readonly record struct PersonId(Guid Value)
{
    public static PersonId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString("D");
}
