using System.Text.Json;
using System.Text.Json.Serialization;
using CompanyHero.Modules.Branding.Domain.Theme;
using CompanyHero.Modules.Branding.Infrastructure;
using CompanyHero.Modules.Organisation.Application;
using CompanyHero.Modules.Organisation.Domain;
using CompanyHero.Platform.Data;
using CompanyHero.Platform.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CompanyHero.Modules.Branding.Application;

/// <summary>Gültiges Theme eines Tenants: Version 0 ist das geprüfte Standard-Theme ohne Veröffentlichung (A-013).</summary>
public sealed record PublishedTheme(int Version, ThemeDocument Document, TokenSet Tokens, DateTimeOffset? PublishedAt);

/// <summary>Ergebnis einer Veröffentlichung oder Vorschau: entweder ein Theme oder die Gründe der Ablehnung (Marke 3.3 Nr. 6).</summary>
public sealed record ThemeOutcome(PublishedTheme? Theme, IReadOnlyList<string> Gruende)
{
    public bool Accepted => Theme is not null;
}

/// <summary>Öffentliche Anwendungsfunktionen der Domäne Marke und Theme (Domänenkarte 2).</summary>
public interface IThemeService
{
    /// <summary>Tenant-Kontext: das gültige Theme, sonst das Standard-Theme mit dem Anzeigenamen des Tenants als Produktname.</summary>
    Task<PublishedTheme> GetCurrentAsync(CancellationToken cancellationToken);

    /// <summary>Tenant-Kontext: eine frühere Version innerhalb von zwölf Monaten (Marke 3.4).</summary>
    Task<PublishedTheme?> GetVersionAsync(int version, CancellationToken cancellationToken);

    /// <summary>Tenant-Admin (Organisation 4.2 „Marke und Anmeldewege“): validiert, leitet ab und veröffentlicht eine neue Version.</summary>
    Task<ThemeOutcome> PublishAsync(ThemeDocument document, CancellationToken cancellationToken);

    /// <summary>Tenant-Admin: Ableitung ohne Persistierung für die Live-Vorschau (A-013).</summary>
    ThemeOutcome Preview(ThemeDocument document);

    /// <summary>Plattformmarke und Standard-Theme (Marke 4.1), ohne Tenant-Bezug.</summary>
    PlatformBrand Platform { get; }

    /// <summary>Tokensatz der Plattform-Schale (Arena, Konsolen, Anmeldeseite vor Zuordnung).</summary>
    TokenSet PlatformTokens { get; }
}

/// <summary>Plattformmarke aus der Konfiguration (Abschnitt <c>Branding:Platform</c>); Standardwerte aus Marke 2.2.</summary>
public sealed class BrandingOptions
{
    public const string Section = "Branding";

    public PlatformBrand Platform { get; set; } = PlatformBrand.Default;
}

internal sealed class ThemeService(
    BrandingDbContext db,
    IContextTransaction transaction,
    ITenantContextAccessor context,
    IOrganisationDirectory organisations,
    IOptions<BrandingOptions> options,
    TimeProvider clock) : IThemeService
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = false,
    };

    private TokenSet? _platformTokens;

    public PlatformBrand Platform => options.Value.Platform;

    public TokenSet PlatformTokens => _platformTokens ??= ThemeDerivation.Derive(ThemeDocument.Standard(Platform.Plattformname, Platform), Platform);

    public async Task<PublishedTheme> GetCurrentAsync(CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        TenantTheme? latest;
        await using (var tx = await transaction.BeginAsync(cancellationToken))
        {
            latest = await db.Themes.Where(t => t.TenantId == tenantId).OrderByDescending(t => t.Version).FirstOrDefaultAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }

        if (latest is not null)
        {
            return Materialize(latest);
        }

        // Anzeigename des Tenants über die öffentliche Anwendungsfunktion von Organisation (eigene Kontexttransaktion).
        var tenant = await organisations.GetCurrentTenantAsync(cancellationToken);
        var standard = ThemeDocument.Standard(tenant?.DisplayName ?? Platform.Plattformname, Platform);
        return new PublishedTheme(0, standard, ThemeDerivation.Derive(standard, Platform), null);
    }

    public async Task<PublishedTheme?> GetVersionAsync(int version, CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        var since = clock.GetUtcNow() - TenantTheme.History;
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var theme = await db.Themes.SingleOrDefaultAsync(t => t.TenantId == tenantId && t.Version == version && t.PublishedAt >= since, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return theme is null ? null : Materialize(theme);
    }

    public async Task<ThemeOutcome> PublishAsync(ThemeDocument document, CancellationToken cancellationToken)
    {
        var current = context.Require();
        var tenantId = current.RequireTenant();
        if (!current.HasRole(Role.TenantAdmin))
        {
            throw new UnauthorizedAccessException("Marke veröffentlicht nur der Tenant-Admin (Organisation 4.2).");
        }

        var errors = ThemeDocumentValidator.Validate(document);
        if (errors.Count > 0)
        {
            return new ThemeOutcome(null, errors);
        }

        var normalized = Normalize(document);
        var tokens = ThemeDerivation.Derive(normalized, Platform);
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var last = await db.Themes.Where(t => t.TenantId == tenantId).MaxAsync(t => (int?)t.Version, cancellationToken) ?? 0;
        var theme = TenantTheme.Publish(tenantId, last + 1, JsonSerializer.Serialize(normalized, Json), JsonSerializer.Serialize(tokens, Json), clock.GetUtcNow(), current.PersonId);
        db.Themes.Add(theme);
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return new ThemeOutcome(new PublishedTheme(theme.Version, normalized, tokens, theme.PublishedAt), []);
    }

    public ThemeOutcome Preview(ThemeDocument document)
    {
        var current = context.Require();
        if (!current.HasRole(Role.TenantAdmin))
        {
            throw new UnauthorizedAccessException("Die Vorschau der Marke ist Teil der Verwaltung des Tenant-Admins (Organisation 4.2).");
        }

        var errors = ThemeDocumentValidator.Validate(document);
        if (errors.Count > 0)
        {
            return new ThemeOutcome(null, errors);
        }

        var normalized = Normalize(document);
        return new ThemeOutcome(new PublishedTheme(0, normalized, ThemeDerivation.Derive(normalized, Platform), null), []);
    }

    private static ThemeDocument Normalize(ThemeDocument document) => document with
    {
        Produktname = document.Produktname.Trim(),
        Saatfarbe = document.Saatfarbe.ToUpperInvariant(),
        Akzent = document.Akzent?.ToUpperInvariant(),
        Bezeichnungen = new Bezeichnungen(document.Bezeichnungen.Punkte.Trim(), document.Bezeichnungen.Serie.Trim(), document.Bezeichnungen.Stufen.Select(s => s.Trim()).ToList()),
        Willkommenstext = string.IsNullOrWhiteSpace(document.Willkommenstext) ? null : document.Willkommenstext.Trim(),
    };

    private static PublishedTheme Materialize(TenantTheme theme)
    {
        var document = JsonSerializer.Deserialize<ThemeDocument>(theme.Document, Json) ?? throw new InvalidOperationException("Theme-Dokument nicht lesbar.");
        var tokens = JsonSerializer.Deserialize<TokenSet>(theme.Tokens, Json) ?? throw new InvalidOperationException("Tokensatz nicht lesbar.");
        return new PublishedTheme(theme.Version, document, tokens, theme.PublishedAt);
    }
}
