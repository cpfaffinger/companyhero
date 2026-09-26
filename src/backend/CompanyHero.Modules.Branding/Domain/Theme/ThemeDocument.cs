using CompanyHero.Modules.Branding.Domain.Color;

namespace CompanyHero.Modules.Branding.Domain.Theme;

/// <summary>Anrede des Tenants (Marke 3.1, A-080): in jeder Textvariante ausgearbeitet, keine automatische Umformung.</summary>
public enum Anrede
{
    Du = 1,
    Sie = 2,
}

/// <summary>Tonalitätsstufe des Tenants (Marke 6.1, A-080).</summary>
public enum Tonalitaet
{
    Sachlich = 1,
    Freundlich = 2,
    Motivierend = 3,
}

/// <summary>Bezeichnungen, die der Tenant ersetzen darf (Marke 3.1): Punkte, Serie und die fünf Stufen (A-047).</summary>
public sealed record Bezeichnungen(string Punkte, string Serie, IReadOnlyList<string> Stufen)
{
    public static Bezeichnungen Standard { get; } = new("Punkte", "Serie", ["Neu dabei", "Dabei", "Dranbleiber", "Vorbild", "Urgestein"]);
}

/// <summary>
/// Tenant-Theme als versioniertes Dokument mit Schemaversion (Marke 3.2, A-077). Logos und Startbild sind Medienreferenzen
/// und folgen mit der Medienverarbeitung; im Durchstich bleibt das Bildzeichen der Plattform in Tenant-Farbe (Marke 5).
/// </summary>
public sealed record ThemeDocument(
    int Schema,
    string Produktname,
    string Saatfarbe,
    string? Akzent,
    Anrede Anrede,
    Tonalitaet Tonalitaet,
    Bezeichnungen Bezeichnungen,
    string? Willkommenstext)
{
    public const int CurrentSchema = 1;

    public const int MaxProduktname = 40;

    public const int MaxWillkommenstext = 400;

    public const int MaxBezeichnung = 30;

    /// <summary>Standard-Theme eines Tenants ohne veröffentlichtes Theme (A-013: geprüftes Standard-Theme).</summary>
    public static ThemeDocument Standard(string produktname, PlatformBrand brand)
    {
        ArgumentNullException.ThrowIfNull(brand);
        return new ThemeDocument(CurrentSchema, produktname, brand.Saatfarbe, null, Anrede.Du, Tonalitaet.Freundlich, Bezeichnungen.Standard, null);
    }
}

/// <summary>Validierung des Theme-Dokuments (Marke 3.2): Schema, Farbformat, Textlängen; jeder Grund ist lesbar für die Verwaltung.</summary>
public static class ThemeDocumentValidator
{
    public static IReadOnlyList<string> Validate(ThemeDocument? document)
    {
        var errors = new List<string>();
        if (document is null)
        {
            errors.Add("Theme-Dokument fehlt.");
            return errors;
        }

        if (document.Schema != ThemeDocument.CurrentSchema)
        {
            errors.Add($"Schemaversion {document.Schema} wird nicht unterstützt; erwartet {ThemeDocument.CurrentSchema}.");
        }

        if (string.IsNullOrWhiteSpace(document.Produktname) || document.Produktname.Trim().Length > ThemeDocument.MaxProduktname)
        {
            errors.Add($"Produktname fehlt oder ist länger als {ThemeDocument.MaxProduktname} Zeichen.");
        }

        if (!ColorUtils.TryParseHex(document.Saatfarbe, out _))
        {
            errors.Add("Saatfarbe muss die Form #RRGGBB haben.");
        }

        if (document.Akzent is not null && !ColorUtils.TryParseHex(document.Akzent, out _))
        {
            errors.Add("Akzentfarbe muss die Form #RRGGBB haben oder fehlen.");
        }

        if (!Enum.IsDefined(document.Anrede))
        {
            errors.Add("Anrede ist du oder sie.");
        }

        if (!Enum.IsDefined(document.Tonalitaet))
        {
            errors.Add("Tonalität ist sachlich, freundlich oder motivierend.");
        }

        if (document.Bezeichnungen is null)
        {
            errors.Add("Bezeichnungen fehlen.");
        }
        else
        {
            if (IsBadLabel(document.Bezeichnungen.Punkte) || IsBadLabel(document.Bezeichnungen.Serie))
            {
                errors.Add($"Bezeichnungen für Punkte und Serie sind Pflicht und höchstens {ThemeDocument.MaxBezeichnung} Zeichen lang.");
            }

            if (document.Bezeichnungen.Stufen is null || document.Bezeichnungen.Stufen.Count != 5 || document.Bezeichnungen.Stufen.Any(IsBadLabel))
            {
                errors.Add($"Genau fünf Stufenbezeichnungen mit höchstens {ThemeDocument.MaxBezeichnung} Zeichen (A-047).");
            }
        }

        if (document.Willkommenstext is not null && document.Willkommenstext.Length > ThemeDocument.MaxWillkommenstext)
        {
            errors.Add($"Willkommenstext ist länger als {ThemeDocument.MaxWillkommenstext} Zeichen.");
        }

        return errors;
    }

    private static bool IsBadLabel(string? label) => string.IsNullOrWhiteSpace(label) || label.Trim().Length > ThemeDocument.MaxBezeichnung;
}
