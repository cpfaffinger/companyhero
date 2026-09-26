namespace CompanyHero.Modules.Branding.Domain.Theme;

/// <summary>Farbrollen des Tokenmodells (Marke 2.2, A-076); im Frontend als <c>--ch-&lt;rolle&gt;</c>.</summary>
public static class ColorRoles
{
    public const string Primary = "primary";
    public const string OnPrimary = "on-primary";
    public const string PrimaryContainer = "primary-container";
    public const string OnPrimaryContainer = "on-primary-container";
    public const string Accent = "accent";
    public const string OnAccent = "on-accent";
    public const string Progress = "progress";
    public const string OnProgress = "on-progress";
    public const string ProgressContainer = "progress-container";
    public const string OnProgressContainer = "on-progress-container";
    public const string Team = "team";
    public const string TeamContainer = "team-container";
    public const string OnTeamContainer = "on-team-container";
    public const string Success = "success";
    public const string OnSuccess = "on-success";
    public const string Warning = "warning";
    public const string OnWarning = "on-warning";
    public const string Error = "error";
    public const string OnError = "on-error";
    public const string ErrorContainer = "error-container";
    public const string OnErrorContainer = "on-error-container";
    public const string Surface = "surface";
    public const string SurfaceContainer = "surface-container";
    public const string SurfaceContainerHigh = "surface-container-high";
    public const string OnSurface = "on-surface";
    public const string OnSurfaceVariant = "on-surface-variant";
    public const string Outline = "outline";
    public const string OutlineVariant = "outline-variant";

    public static string Chart(int index) => $"chart-{index}";

    /// <summary>Alle verbindlichen Rollen; Akzentrollen sind optional (Marke 3.1).</summary>
    public static IReadOnlyList<string> Required { get; } =
    [
        Primary, OnPrimary, PrimaryContainer, OnPrimaryContainer,
        Progress, OnProgress, ProgressContainer, OnProgressContainer,
        Team, TeamContainer, OnTeamContainer,
        Success, OnSuccess, Warning, OnWarning, Error, OnError, ErrorContainer, OnErrorContainer,
        Surface, SurfaceContainer, SurfaceContainerHigh, OnSurface, OnSurfaceVariant, Outline, OutlineVariant,
        Chart(1), Chart(2), Chart(3), Chart(4), Chart(5), Chart(6),
    ];
}

public enum ThemeMode
{
    Hell = 1,
    Dunkel = 2,
}

/// <summary>Eine Ersetzung mit Begründung (Marke 3.3 Nr. 5): welche Rolle in welchem Modus warum ersetzt wurde.</summary>
public sealed record Ersetzung(ThemeMode Modus, string Rolle, string Angefordert, string Ersetzt, string Grund);

/// <summary>Alle <c>sys.*</c>-Werte eines Modus als Rolle → <c>#RRGGBB</c>.</summary>
public sealed record ModeTokens(IReadOnlyDictionary<string, string> Werte)
{
    public string this[string role] => Werte[role];
}

/// <summary>Ergebnis der Ableitung: Tokensatz je Modus, Ersetzungsliste, Verfahrensversion (Marke 3.3 Nr. 5).</summary>
public sealed record TokenSet(ModeTokens Hell, ModeTokens Dunkel, IReadOnlyList<Ersetzung> Ersetzungen, string Verfahren)
{
    public ModeTokens For(ThemeMode mode) => mode == ThemeMode.Hell ? Hell : Dunkel;
}
