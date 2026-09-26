namespace CompanyHero.Modules.Branding.Domain.Theme;

/// <summary>
/// Plattformmarke und Startwerte des Standard-Themes (Marke 2.2, 4.1, A-078): Konfiguration des Operators, keine
/// Codeänderung für einen Namenswechsel. Die plattformweit festen Rollen (Team, Erfolg, Warnung, Fehler, Diagramme) und
/// die Startwerte der Fortschrittsfarbe stehen hier; Tenants überschreiben nur Saat-, Akzent- und abgeleitete
/// Fortschrittsrollen.
/// </summary>
public sealed record PlatformBrand
{
    public string Plattformname { get; init; } = "CompanyHero";

    /// <summary>Saatfarbe des Standard-Themes (Startwert <c>primary</c> hell).</summary>
    public string Saatfarbe { get; init; } = "#2A45C9";

    /// <summary>Startwert der Fortschrittsfarbe; Farbton und Chroma gelten als Vorgabe für abgeleitete Fortschrittsrollen.</summary>
    public string Fortschritt { get; init; } = "#E4670A";

    public FixedRole Team { get; init; } = new("#0F766E", "#7FD8CE");

    public FixedRole TeamContainer { get; init; } = new("#B8F0EA", "#00504A");

    public FixedRole OnTeamContainer { get; init; } = new("#00201D", "#B8F0EA");

    public FixedRole Success { get; init; } = new("#1F7A3D", "#7BDA96");

    public FixedRole Warning { get; init; } = new("#9A6300", "#F2C063");

    public FixedRole Error { get; init; } = new("#B3261E", "#FFB4AB");

    /// <summary>Diagrammreihen: eigene, für Farbfehlsichtigkeit geprüfte Sequenz; verwenden nie semantische Rollen (Marke 2.2).</summary>
    public IReadOnlyList<FixedRole> Charts { get; init; } =
    [
        new("#0072B2", "#56B4E9"),
        new("#D55E00", "#F0A860"),
        new("#009E73", "#5FD3B0"),
        new("#CC79A7", "#F2A8D2"),
        new("#7A5C00", "#E6C34A"),
        new("#5B5B5B", "#B0B0B0"),
    ];

    public static PlatformBrand Default { get; } = new();
}

/// <summary>Plattformweit fester Wert einer Rolle je Modus.</summary>
public sealed record FixedRole(string Hell, string Dunkel);
