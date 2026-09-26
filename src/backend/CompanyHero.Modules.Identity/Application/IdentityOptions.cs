namespace CompanyHero.Modules.Identity.Application;

/// <summary>Plattformweit registrierter Anbieter (Zugang 3.2): Microsoft, Google; Registrierung pflegt der Operator, Geheimnisse aus OpenBao.</summary>
public sealed class PlatformProviderOptions
{
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Authority für Discovery, etwa <c>https://login.microsoftonline.com/common/v2.0</c> oder <c>https://accounts.google.com</c>.</summary>
    public string Authority { get; set; } = string.Empty;

    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Regulärer Ausdruck für gültige Issuer, wenn der Anbieter mandantenabhängige Issuer ausstellt (Microsoft „common“).</summary>
    public string? IssuerPattern { get; set; }
}

/// <summary>Konfiguration des Zugangs: öffentliche Origin (Cookies, WebAuthn-Relying-Party, Redirects) und plattformweite Anbieter.</summary>
public sealed class IdentityOptions
{
    public const string Section = "Identity";

    /// <summary>Öffentliche Origin der Plattform (A-032), etwa <c>https://app.example</c>; in Tests <c>https://localhost</c>.</summary>
    public string PublicOrigin { get; set; } = "https://localhost";

    public string ServerName { get; set; } = "CompanyHero";

    public Dictionary<string, PlatformProviderOptions> Providers { get; } = new(StringComparer.Ordinal);

    public Uri PublicOriginUri => new(PublicOrigin, UriKind.Absolute);

    public string RelyingPartyId => PublicOriginUri.Host;
}
