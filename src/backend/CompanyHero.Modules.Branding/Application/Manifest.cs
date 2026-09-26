using System.Globalization;
using System.Text.Json.Serialization;
using CompanyHero.Modules.Branding.Domain.Theme;
using CompanyHero.Platform.Tenancy;

namespace CompanyHero.Modules.Branding.Application;

/// <summary>Ein Icon-Eintrag des Web-App-Manifests.</summary>
public sealed record ManifestIcon(string Src, string Sizes, string Type, string? Purpose);

/// <summary>
/// Web-App-Manifest (A-079): dynamisch je Tenant unter tenant-spezifischem Pfad auf derselben Origin. <c>id</c>, <c>start_url</c>
/// und <c>scope</c> tragen den Tenant-Kennzeichner, Name aus dem Produktnamen, Farben aus dem Tokensatz, Icons aus dem
/// Bildzeichen in Tenant-Farbe. Der Kiosk hat ein eigenes Manifest im Vollbild.
/// </summary>
public sealed record WebManifest(
    string Id,
    string Name,
    [property: JsonPropertyName("short_name")] string ShortName,
    [property: JsonPropertyName("start_url")] string StartUrl,
    string Scope,
    string Display,
    string Lang,
    [property: JsonPropertyName("theme_color")] string ThemeColor,
    [property: JsonPropertyName("background_color")] string BackgroundColor,
    IReadOnlyList<ManifestIcon> Icons,
    string? Orientation);

public static class ManifestPaths
{
    public const string ContentType = "application/manifest+json";

    /// <summary>Tenant-Kennzeichner in Pfaden ist die Tenant-Kennung (UUIDv7): opak, ohne Personenbezug (A-026).</summary>
    public static string TenantScope(TenantId tenantId) => $"/t/{tenantId}/";

    public static string TenantManifest(TenantId tenantId) => $"/api/branding/tenants/{tenantId}/manifest.webmanifest";

    public static string TenantIcon(TenantId tenantId, string name) => $"/api/branding/tenants/{tenantId}/icons/{name}";

    public const string KioskManifest = "/api/branding/kiosk/manifest.webmanifest";

    public static IReadOnlyList<string> IconNames { get; } = ["icon-192.png", "icon-512.png", "maskable-512.png", "icon.svg"];
}

public static class ManifestBuilder
{
    private const int ShortNameMax = 12;

    public static WebManifest ForTenant(TenantId tenantId, PublishedTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        var hell = theme.Tokens.Hell;
        var scope = ManifestPaths.TenantScope(tenantId);
        return new WebManifest(
            Id: scope,
            Name: theme.Document.Produktname,
            ShortName: ShortName(theme.Document.Produktname),
            StartUrl: scope + "start",
            Scope: scope,
            Display: "standalone",
            Lang: "de",
            ThemeColor: hell[ColorRoles.Primary],
            BackgroundColor: hell[ColorRoles.Surface],
            Icons: Icons(name => ManifestPaths.TenantIcon(tenantId, name)),
            Orientation: null);
    }

    /// <summary>Kiosk (Marke 5): eigenes Manifest mit dem Kiosk-Einstieg als <c>start_url</c> und Anzeige im Vollbild.</summary>
    public static WebManifest ForKiosk(TenantId tenantId, PublishedTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        var hell = theme.Tokens.Hell;
        return new WebManifest(
            Id: "/kiosk/",
            Name: theme.Document.Produktname + " Kiosk",
            ShortName: "Kiosk",
            StartUrl: "/kiosk",
            Scope: "/kiosk/",
            Display: "fullscreen",
            Lang: "de",
            ThemeColor: hell[ColorRoles.Primary],
            BackgroundColor: hell[ColorRoles.Surface],
            Icons: Icons(name => ManifestPaths.TenantIcon(tenantId, name)),
            Orientation: "landscape");
    }

    private static IReadOnlyList<ManifestIcon> Icons(Func<string, string> src) =>
    [
        new(src("icon-192.png"), "192x192", "image/png", "any"),
        new(src("icon-512.png"), "512x512", "image/png", "any"),
        new(src("maskable-512.png"), "512x512", "image/png", "maskable"),
        new(src("icon.svg"), "any", "image/svg+xml", "any"),
    ];

    private static string ShortName(string name)
    {
        var trimmed = name.Trim();
        return trimmed.Length <= ShortNameMax ? trimmed : trimmed[..ShortNameMax].TrimEnd().ToString(CultureInfo.InvariantCulture);
    }
}
