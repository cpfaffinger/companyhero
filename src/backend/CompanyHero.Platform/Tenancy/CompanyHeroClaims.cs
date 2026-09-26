namespace CompanyHero.Platform.Tenancy;

/// <summary>
/// Claims, die die geprüfte Sitzung an den Request hängt. Sie benennen Tenant, Person, Sitzungsart, Anmeldezeitpunkt und
/// Kiosk-Gerät; Mitgliedschaft und Rollen werden daraus nicht übernommen, sondern je Request bei Organisation geprüft
/// (<see cref="IMembershipVerification"/>). Die serverseitige Cookie-Sitzung des Moduls Identity füllt diese Claims (A-007).
/// </summary>
public static class CompanyHeroClaims
{
    public const string Tenant = "ch:tenant";
    public const string Person = "ch:person";

    /// <summary>„tenant“ oder „platform“; ohne Angabe gilt „tenant“.</summary>
    public const string Context = "ch:context";

    /// <summary>Sitzungsart als Name von <see cref="SessionKind"/>; ohne Angabe gilt „Member“.</summary>
    public const string Session = "ch:session";

    /// <summary>Anmeldezeitpunkt der Sitzung (ISO 8601, UTC).</summary>
    public const string AuthenticatedAt = "ch:auth_time";

    /// <summary>Kennung des Kiosk-Geräts bei Kiosk-Sitzungen.</summary>
    public const string KioskDevice = "ch:kiosk_device";

    public const string ContextTenant = "tenant";
    public const string ContextPlatform = "platform";
}
