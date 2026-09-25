namespace CompanyHero.Platform.Tenancy;

/// <summary>
/// Claims, die die geprüfte Sitzung an den Request hängt. Sie benennen Tenant und Person; Mitgliedschaft und Rollen
/// werden daraus nicht übernommen, sondern je Request bei Organisation geprüft (<see cref="IMembershipVerification"/>).
/// Stufe 4 füllt diese Claims aus der serverseitigen Cookie-Sitzung (A-007).
/// </summary>
public static class CompanyHeroClaims
{
    public const string Tenant = "ch:tenant";
    public const string Person = "ch:person";

    /// <summary>„tenant“ oder „platform“; ohne Angabe gilt „tenant“.</summary>
    public const string Context = "ch:context";

    public const string ContextTenant = "tenant";
    public const string ContextPlatform = "platform";
}
