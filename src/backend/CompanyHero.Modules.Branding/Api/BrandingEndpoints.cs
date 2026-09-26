using System.Text.Json.Serialization;
using CompanyHero.Modules.Branding.Application;
using CompanyHero.Modules.Branding.Application.Icons;
using CompanyHero.Modules.Branding.Domain.Color;
using CompanyHero.Modules.Branding.Domain.Text;
using CompanyHero.Modules.Branding.Domain.Theme;
using CompanyHero.Modules.Organisation.Domain;
using CompanyHero.Platform.Hosting;
using CompanyHero.Platform.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CompanyHero.Modules.Branding.Api;

/// <summary>Vertrag nach A-009 und K13: Farben als <c>#RRGGBB</c>-Strings, Kennungen als Strings, Aufzählungen als benannte Werte.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AnredeDto>))]
public enum AnredeDto
{
    [JsonStringEnumMemberName("du")] Du,
    [JsonStringEnumMemberName("sie")] Sie,
}

[JsonConverter(typeof(JsonStringEnumConverter<TonalitaetDto>))]
public enum TonalitaetDto
{
    [JsonStringEnumMemberName("sachlich")] Sachlich,
    [JsonStringEnumMemberName("freundlich")] Freundlich,
    [JsonStringEnumMemberName("motivierend")] Motivierend,
}

[JsonConverter(typeof(JsonStringEnumConverter<ThemeModeDto>))]
public enum ThemeModeDto
{
    [JsonStringEnumMemberName("hell")] Hell,
    [JsonStringEnumMemberName("dunkel")] Dunkel,
}

public sealed record BezeichnungenDto(string Punkte, string Serie, IReadOnlyList<string> Stufen);

/// <summary>Theme-Dokument, wie es die Verwaltung veröffentlicht oder in der Vorschau prüft (Marke 3.2, Schema 1).</summary>
public sealed record ThemeDocumentRequest(int Schema, string Produktname, string Saatfarbe, string? Akzent, AnredeDto Anrede, TonalitaetDto Tonalitaet, BezeichnungenDto Bezeichnungen, string? Willkommenstext);

/// <summary>Eine Ersetzung des Kontrastberichts (Marke 3.3 Nr. 5).</summary>
public sealed record ErsetzungDto(ThemeModeDto Modus, string Rolle, string Angefordert, string Ersetzt, string Grund);

/// <summary>Ein geprüftes Rollenpaar des Kontrastberichts: gefordertes und erreichtes Verhältnis als Dezimalstring.</summary>
public sealed record KontrastDto(ThemeModeDto Modus, string Vordergrund, string Hintergrund, string Gefordert, string Erreicht);

/// <summary>
/// Gültiges Theme des Tenants: versionierter Tokensatz je Modus (Rolle → <c>#RRGGBB</c>), Ersetzungen, Bezeichnungen,
/// Texte in Tonalität und Anrede des Tenants und der Pfad des Manifests (A-077, A-079, A-080).
/// </summary>
public sealed record ThemeResponse(
    string TenantId,
    int Version,
    string Verfahren,
    int Schema,
    string Produktname,
    AnredeDto Anrede,
    TonalitaetDto Tonalitaet,
    BezeichnungenDto Bezeichnungen,
    string? Willkommenstext,
    IReadOnlyDictionary<string, string> Hell,
    IReadOnlyDictionary<string, string> Dunkel,
    IReadOnlyList<ErsetzungDto> Ersetzungen,
    IReadOnlyDictionary<string, string> Texte,
    string ManifestPfad,
    DateTimeOffset? VeroeffentlichtAm);

/// <summary>Vorschau ohne Persistierung mit Kontrastbericht (A-013, Marke 3.5).</summary>
public sealed record ThemePreviewResponse(
    string Verfahren,
    IReadOnlyDictionary<string, string> Hell,
    IReadOnlyDictionary<string, string> Dunkel,
    IReadOnlyList<ErsetzungDto> Ersetzungen,
    IReadOnlyList<KontrastDto> Kontrast);

/// <summary>Plattformmarke und Standard-Theme für die Plattform-Schale (Marke 4.1, 4.2); ohne Sitzung abrufbar.</summary>
public sealed record PlatformThemeResponse(
    string Plattformname,
    string Verfahren,
    IReadOnlyDictionary<string, string> Hell,
    IReadOnlyDictionary<string, string> Dunkel,
    IReadOnlyDictionary<string, string> Texte);

internal static class BrandingEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/branding/theme", async (ITenantContextAccessor context, IThemeService themes, CancellationToken ct) =>
            {
                var theme = await themes.GetCurrentAsync(ct);
                return Results.Ok(ToResponse(context.Require().RequireTenant(), theme, themes.Platform));
            })
            .RequireTenantContext().AllowKiosk(device: true)
            .WithName("GetTheme")
            .Produces<ThemeResponse>();

        endpoints.MapGet("/api/branding/theme/versions/{version:int}", async (int version, ITenantContextAccessor context, IThemeService themes, CancellationToken ct) =>
            {
                var theme = await themes.GetVersionAsync(version, ct);
                return theme is null ? Results.NotFound() : Results.Ok(ToResponse(context.Require().RequireTenant(), theme, themes.Platform));
            })
            .RequireTenantContext()
            .WithName("GetThemeVersion")
            .Produces<ThemeResponse>()
            .Produces(StatusCodes.Status404NotFound);

        // Marke veröffentlichen: Tenant-Admin, sensible Aktion mit frischer Anmeldung (Zugang 3.3).
        endpoints.MapPut("/api/branding/theme", async (ThemeDocumentRequest request, ITenantContextAccessor context, IThemeService themes, CancellationToken ct) =>
            {
                if (!context.Require().HasRole(Role.TenantAdmin))
                {
                    return Results.Forbid();
                }

                var outcome = await themes.PublishAsync(ToDocument(request), ct);
                return outcome.Accepted
                    ? Results.Ok(ToResponse(context.Require().RequireTenant(), outcome.Theme!, themes.Platform))
                    : Results.ValidationProblem(new Dictionary<string, string[]> { ["theme"] = [.. outcome.Gruende] }, title: "Theme abgelehnt; das Standard-Theme oder die letzte Version bleibt gültig.");
            })
            .RequireTenantContext().RequireFreshLogin()
            .WithName("PublishTheme")
            .Produces<ThemeResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status403Forbidden);

        endpoints.MapPost("/api/branding/theme/preview", (ThemeDocumentRequest request, ITenantContextAccessor context, IThemeService themes) =>
            {
                if (!context.Require().HasRole(Role.TenantAdmin))
                {
                    return Results.Forbid();
                }

                var outcome = themes.Preview(ToDocument(request));
                if (!outcome.Accepted)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["theme"] = [.. outcome.Gruende] }, title: "Theme ungültig; die Vorschau zeigt das Standard-Theme.");
                }

                var tokens = outcome.Theme!.Tokens;
                return Results.Ok(new ThemePreviewResponse(tokens.Verfahren, tokens.Hell.Werte, tokens.Dunkel.Werte, Ersetzungen(tokens), Kontrastbericht(tokens)));
            })
            .RequireTenantContext()
            .WithName("PreviewTheme")
            .Produces<ThemePreviewResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status403Forbidden);

        endpoints.MapGet("/api/branding/platform", (IThemeService themes) =>
            {
                var brand = themes.Platform;
                var tokens = themes.PlatformTokens;
                var texte = TextCatalog.Embedded.Resolve(Tonalitaet.Freundlich, Anrede.Du, brand.Plattformname, Bezeichnungen.Standard)
                    .ToDictionary(p => p.Key, p => p.Value.Replace("{plattform}", brand.Plattformname, StringComparison.Ordinal), StringComparer.Ordinal);
                return Results.Ok(new PlatformThemeResponse(brand.Plattformname, tokens.Verfahren, tokens.Hell.Werte, tokens.Dunkel.Werte, texte));
            })
            .WithName("GetPlatformTheme")
            .Produces<PlatformThemeResponse>();

        // Manifest und Icons: nur für den Tenant der Sitzung; jeder andere Tenant-Pfad antwortet „nicht gefunden“ (A-026, Datenschutz 7).
        endpoints.MapGet("/api/branding/tenants/{tenantId:guid}/manifest.webmanifest", async (Guid tenantId, ITenantContextAccessor context, IThemeService themes, CancellationToken ct) =>
            {
                var current = context.Require().RequireTenant();
                if (current.Value != tenantId)
                {
                    return Results.NotFound();
                }

                var theme = await themes.GetCurrentAsync(ct);
                return Results.Json(ManifestBuilder.ForTenant(current, theme), contentType: ManifestPaths.ContentType);
            })
            .RequireTenantContext()
            .WithName("GetTenantManifest")
            .Produces<WebManifest>(StatusCodes.Status200OK, ManifestPaths.ContentType)
            .Produces(StatusCodes.Status404NotFound);

        endpoints.MapGet(ManifestPaths.KioskManifest, async (ITenantContextAccessor context, IThemeService themes, CancellationToken ct) =>
            {
                var theme = await themes.GetCurrentAsync(ct);
                return Results.Json(ManifestBuilder.ForKiosk(context.Require().RequireTenant(), theme), contentType: ManifestPaths.ContentType);
            })
            .RequireTenantContext().AllowKiosk(device: true)
            .WithName("GetKioskManifest")
            .Produces<WebManifest>(StatusCodes.Status200OK, ManifestPaths.ContentType);

        endpoints.MapGet("/api/branding/tenants/{tenantId:guid}/icons/{name}", async (Guid tenantId, string name, ITenantContextAccessor context, IThemeService themes, CancellationToken ct) =>
            {
                var current = context.Require().RequireTenant();
                if (current.Value != tenantId || !ManifestPaths.IconNames.Contains(name, StringComparer.Ordinal))
                {
                    return Results.NotFound();
                }

                var theme = await themes.GetCurrentAsync(ct);
                var hell = theme.Tokens.Hell;
                var primary = hell[ColorRoles.Primary];
                var progress = hell[ColorRoles.Progress];
                var background = hell[ColorRoles.PrimaryContainer];
                return name switch
                {
                    "icon-192.png" => Results.Bytes(IconRenderer.Png(192, primary, progress, background, maskable: false), "image/png"),
                    "icon-512.png" => Results.Bytes(IconRenderer.Png(512, primary, progress, background, maskable: false), "image/png"),
                    "maskable-512.png" => Results.Bytes(IconRenderer.Png(512, primary, progress, background, maskable: true), "image/png"),
                    _ => Results.Text(IconRenderer.Svg(primary, progress, background), "image/svg+xml"),
                };
            })
            .RequireTenantContext().AllowKiosk(device: true)
            .WithName("GetTenantIcon")
            .Produces(StatusCodes.Status200OK, contentType: "image/png")
            .Produces(StatusCodes.Status404NotFound);
    }

    internal static ThemeResponse ToResponse(TenantId tenantId, PublishedTheme theme, PlatformBrand brand)
    {
        var d = theme.Document;
        var texte = TextCatalog.Embedded.Resolve(d.Tonalitaet, d.Anrede, d.Produktname, d.Bezeichnungen)
            .ToDictionary(p => p.Key, p => p.Value.Replace("{plattform}", brand.Plattformname, StringComparison.Ordinal), StringComparer.Ordinal);
        return new ThemeResponse(
            tenantId.ToString(),
            theme.Version,
            theme.Tokens.Verfahren,
            d.Schema,
            d.Produktname,
            d.Anrede == Anrede.Sie ? AnredeDto.Sie : AnredeDto.Du,
            d.Tonalitaet switch { Tonalitaet.Sachlich => TonalitaetDto.Sachlich, Tonalitaet.Motivierend => TonalitaetDto.Motivierend, _ => TonalitaetDto.Freundlich },
            new BezeichnungenDto(d.Bezeichnungen.Punkte, d.Bezeichnungen.Serie, d.Bezeichnungen.Stufen),
            d.Willkommenstext,
            theme.Tokens.Hell.Werte,
            theme.Tokens.Dunkel.Werte,
            Ersetzungen(theme.Tokens),
            texte,
            ManifestPaths.TenantManifest(tenantId),
            theme.PublishedAt);
    }

    private static ThemeDocument ToDocument(ThemeDocumentRequest r) => new(
        r.Schema,
        r.Produktname ?? string.Empty,
        r.Saatfarbe ?? string.Empty,
        r.Akzent,
        r.Anrede == AnredeDto.Sie ? Anrede.Sie : Anrede.Du,
        r.Tonalitaet switch { TonalitaetDto.Sachlich => Tonalitaet.Sachlich, TonalitaetDto.Motivierend => Tonalitaet.Motivierend, _ => Tonalitaet.Freundlich },
        r.Bezeichnungen is null ? new Bezeichnungen(string.Empty, string.Empty, []) : new Bezeichnungen(r.Bezeichnungen.Punkte ?? string.Empty, r.Bezeichnungen.Serie ?? string.Empty, r.Bezeichnungen.Stufen ?? []),
        r.Willkommenstext);

    private static List<ErsetzungDto> Ersetzungen(TokenSet tokens) =>
        tokens.Ersetzungen.Select(e => new ErsetzungDto(e.Modus == ThemeMode.Hell ? ThemeModeDto.Hell : ThemeModeDto.Dunkel, e.Rolle, e.Angefordert, e.Ersetzt, e.Grund)).ToList();

    private static List<KontrastDto> Kontrastbericht(TokenSet tokens)
    {
        var report = new List<KontrastDto>();
        foreach (var (mode, modeDto) in new[] { (tokens.Hell, ThemeModeDto.Hell), (tokens.Dunkel, ThemeModeDto.Dunkel) })
        {
            foreach (var (fg, bg, ratio) in ThemeDerivation.RequiredPairs(mode.Werte.ContainsKey(ColorRoles.Accent)))
            {
                var actual = Contrast.Ratio(ColorUtils.ArgbFromHex(mode[fg]), ColorUtils.ArgbFromHex(mode[bg]));
                report.Add(new KontrastDto(modeDto, fg, bg, ratio.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture), actual.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)));
            }
        }

        return report;
    }
}
