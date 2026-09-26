using System.Globalization;
using CompanyHero.Modules.Branding.Domain.Color;

namespace CompanyHero.Modules.Branding.Domain.Theme;

/// <summary>
/// Ableitung des Tokensatzes aus der Saatfarbe (Marke 3.3, A-013, A-077, K06), deterministisch und ohne Zufall:
/// <list type="number">
/// <item>Tonpalette der Saatfarbe nach dem Material-Verfahren im HCT-Farbraum (Farbton und Chroma der Saatfarbe, Ton variabel).</item>
/// <item><c>progress</c> aus der Saatfarbe mit erzwungenem Farbtonabstand innerhalb eines warmen Bandes; nie ein Blau. <c>team</c> bleibt plattformweit.</item>
/// <item>Der optionale Akzent wird nur übernommen, wenn er den Mindestabstand zu Saat-, Fortschritts- und Teamfarbe einhält.</item>
/// <item>Alle Rollenpaare gegen WCAG 2.2 AA: 4,5:1 für Text, 3:1 für Bedienelemente und Fortschrittsbahnen. Die Saatfarbe wird immer
/// angenommen; wo sie eine Prüfung nicht besteht, tritt die nächstliegende zulässige Ableitung derselben Farbe (gleicher Farbton,
/// gleiches Chroma, nächster Ton) an ihre Stelle, mit Grund in der Ersetzungsliste.</item>
/// </list>
/// Neutrale Flächen, Team-, Erfolgs-, Warn-, Fehler- und Diagrammfarben sind Plattformeigenschaft (Marke 2.2).
/// </summary>
public static class ThemeDerivation
{
    /// <summary>Kennung des Verfahrens im Tokensatz; ändert sich mit jeder Änderung an der Ableitung.</summary>
    public const string Verfahren = "hct-m3/1";

    public const double TextRatio = 4.5;
    public const double UiRatio = 3.0;

    /// <summary>Warmes Farbtonband für <c>progress</c> in HCT-Grad (Rot-Orange bis Bernstein); Blau (250 bis 270) liegt weit außerhalb.</summary>
    public const double WarmBandStart = 25.0;
    public const double WarmBandEnd = 95.0;

    /// <summary>Mindestabstand des Farbtons zwischen Saat- und Fortschrittsfarbe sowie zum Akzent (Marke 3.3 Nr. 2 und 3).</summary>
    public const double MinHueDistance = 40.0;

    private const double NeutralChroma = 4.0;
    private const double NeutralVariantChroma = 8.0;

    public static TokenSet Derive(ThemeDocument document, PlatformBrand brand)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(brand);
        var errors = ThemeDocumentValidator.Validate(document);
        if (errors.Count > 0)
        {
            throw new ArgumentException("Ungültiges Theme-Dokument: " + string.Join(" ", errors), nameof(document));
        }

        var seed = Hct.FromInt(ColorUtils.ArgbFromHex(document.Saatfarbe));
        var platformSeed = Hct.FromInt(ColorUtils.ArgbFromHex(brand.Saatfarbe));
        var progressDefault = Hct.FromInt(ColorUtils.ArgbFromHex(brand.Fortschritt));
        var teamHue = Hct.FromInt(ColorUtils.ArgbFromHex(brand.Team.Hell)).Hue;

        var primaryPalette = TonalPalette.FromHct(seed);
        var progressHue = ProgressHue(seed.Hue, progressDefault.Hue);
        var progressPalette = TonalPalette.FromHueAndChroma(progressHue, progressDefault.Chroma);
        var neutral = TonalPalette.FromHueAndChroma(platformSeed.Hue, NeutralChroma);
        var neutralVariant = TonalPalette.FromHueAndChroma(platformSeed.Hue, NeutralVariantChroma);
        var accent = AcceptedAccent(document.Akzent, seed.Hue, progressHue, teamHue);

        var replacements = new List<Ersetzung>();
        var hell = DeriveMode(ThemeMode.Hell, seed, primaryPalette, progressPalette, progressDefault, neutral, neutralVariant, accent, brand, replacements);
        var dunkel = DeriveMode(ThemeMode.Dunkel, seed, primaryPalette, progressPalette, progressDefault, neutral, neutralVariant, accent, brand, replacements);
        return new TokenSet(hell, dunkel, replacements, Verfahren);
    }

    /// <summary>
    /// Farbton der Fortschrittsfarbe: die Plattformvorgabe, solange sie den Mindestabstand zur Saatfarbe hält. Sonst wird der
    /// Farbton innerhalb des warmen Bandes gerade so weit von der Saatfarbe weggeschoben, dass der Abstand erreicht ist; liegt
    /// kein solcher Farbton im Band, gilt das Bandende mit dem größten Abstand. Bei warmer Saatfarbe entsteht so ein anderer
    /// warmer Ton (etwa Bernstein neben Rot oder Gold neben Orange), nie ein Blau.
    /// </summary>
    public static double ProgressHue(double seedHue, double defaultProgressHue)
    {
        if (MathUtils.DifferenceDegrees(seedHue, defaultProgressHue) >= MinHueDistance)
        {
            return defaultProgressHue;
        }

        var candidates = new[] { MathUtils.SanitizeDegreesDouble(seedHue + MinHueDistance), MathUtils.SanitizeDegreesDouble(seedHue - MinHueDistance) }
            .Where(h => h >= WarmBandStart && h <= WarmBandEnd)
            .OrderBy(h => MathUtils.DifferenceDegrees(h, defaultProgressHue))
            .ThenBy(h => h)
            .ToList();
        if (candidates.Count > 0)
        {
            return candidates[0];
        }

        return MathUtils.DifferenceDegrees(seedHue, WarmBandStart) >= MathUtils.DifferenceDegrees(seedHue, WarmBandEnd) ? WarmBandStart : WarmBandEnd;
    }

    private static Hct? AcceptedAccent(string? accentHex, double seedHue, double progressHue, double teamHue)
    {
        if (accentHex is null)
        {
            return null;
        }

        var accent = Hct.FromInt(ColorUtils.ArgbFromHex(accentHex));
        var keepsDistance = MathUtils.DifferenceDegrees(accent.Hue, seedHue) >= MinHueDistance
            && MathUtils.DifferenceDegrees(accent.Hue, progressHue) >= MinHueDistance
            && MathUtils.DifferenceDegrees(accent.Hue, teamHue) >= MinHueDistance;
        return keepsDistance ? accent : null;
    }

    private static ModeTokens DeriveMode(
        ThemeMode mode,
        Hct seed,
        TonalPalette primary,
        TonalPalette progress,
        Hct progressDefault,
        TonalPalette neutral,
        TonalPalette neutralVariant,
        Hct? accent,
        PlatformBrand brand,
        List<Ersetzung> replacements)
    {
        var light = mode == ThemeMode.Hell;
        var t = new Dictionary<string, int>(StringComparer.Ordinal);
        var ctx = new ModeContext(mode, t, replacements);

        // Neutrale Flächen (Material-3-Töne; Plattformeigenschaft, für alle Tenants gleich).
        t[ColorRoles.Surface] = neutral.Tone(light ? 98 : 6);
        t[ColorRoles.SurfaceContainer] = neutral.Tone(light ? 94 : 12);
        t[ColorRoles.SurfaceContainerHigh] = neutral.Tone(light ? 92 : 17);
        t[ColorRoles.OnSurface] = neutral.Tone(light ? 10 : 90);
        t[ColorRoles.OnSurfaceVariant] = neutralVariant.Tone(light ? 30 : 80);
        t[ColorRoles.Outline] = neutralVariant.Tone(light ? 50 : 60);
        t[ColorRoles.OutlineVariant] = neutralVariant.Tone(light ? 80 : 30);

        // Marke: hell die Saatfarbe selbst, dunkel Ton 80 derselben Palette; ersetzt nur, wo der Kontrast fehlt (K06).
        var primaryRequested = light ? seed.ToInt() : primary.Tone(80);
        t[ColorRoles.Primary] = ctx.Nearest(ColorRoles.Primary, primary, primaryRequested,
            [(ColorRoles.Surface, TextRatio), (ColorRoles.SurfaceContainer, TextRatio), (ColorRoles.SurfaceContainerHigh, UiRatio)]);
        t[ColorRoles.OnPrimary] = ctx.OnColor(ColorRoles.OnPrimary, primary, ColorRoles.Primary, light ? [100, 10, 0] : [20, 10, 0, 100]);
        t[ColorRoles.PrimaryContainer] = primary.Tone(light ? 90 : 30);
        t[ColorRoles.OnPrimaryContainer] = ctx.OnColor(ColorRoles.OnPrimaryContainer, primary, ColorRoles.PrimaryContainer, light ? [10, 0, 100] : [90, 95, 100, 10, 0]);

        // Fortschritt: warm, mit Abstand zur Marke; Bahn und Zahl brauchen 3:1 gegen alle Flächen und gegen die Bahnfläche.
        t[ColorRoles.ProgressContainer] = progress.Tone(light ? 90 : 30);
        var progressRequested = light ? progress.Tone(RoundTone(progressDefault.Tone)) : progress.Tone(80);
        t[ColorRoles.Progress] = ctx.Nearest(ColorRoles.Progress, progress, progressRequested,
            [(ColorRoles.Surface, UiRatio), (ColorRoles.SurfaceContainer, UiRatio), (ColorRoles.SurfaceContainerHigh, UiRatio), (ColorRoles.ProgressContainer, UiRatio)]);
        t[ColorRoles.OnProgress] = ctx.OnColor(ColorRoles.OnProgress, progress, ColorRoles.Progress, light ? [100, 10, 0] : [20, 10, 0, 100]);
        t[ColorRoles.OnProgressContainer] = ctx.OnColor(ColorRoles.OnProgressContainer, progress, ColorRoles.ProgressContainer, light ? [10, 0, 100] : [90, 95, 100, 10, 0]);

        // Optionaler Akzent (Marke 3.1): als Bedienfarbe 3:1 gegen Flächen, mit eigener Textfarbe.
        if (accent is not null)
        {
            var accentPalette = TonalPalette.FromHct(accent);
            var accentRequested = light ? accent.ToInt() : accentPalette.Tone(80);
            t[ColorRoles.Accent] = ctx.Nearest(ColorRoles.Accent, accentPalette, accentRequested,
                [(ColorRoles.Surface, TextRatio), (ColorRoles.SurfaceContainerHigh, UiRatio)]);
            t[ColorRoles.OnAccent] = ctx.OnColor(ColorRoles.OnAccent, accentPalette, ColorRoles.Accent, light ? [100, 10, 0] : [20, 10, 0, 100]);
        }

        // Plattformweit feste Rollen: geprüft, bei Bedarf durch die nächstliegende Ableitung derselben Farbe ersetzt.
        ctx.Fixed(ColorRoles.Team, Pick(brand.Team, light), [(ColorRoles.Surface, UiRatio), (ColorRoles.SurfaceContainerHigh, UiRatio)]);
        ctx.Fixed(ColorRoles.TeamContainer, Pick(brand.TeamContainer, light), []);
        ctx.Fixed(ColorRoles.OnTeamContainer, Pick(brand.OnTeamContainer, light), [(ColorRoles.TeamContainer, TextRatio)]);

        foreach (var (role, onRole, fixedRole) in new[]
                 {
                     (ColorRoles.Success, ColorRoles.OnSuccess, brand.Success),
                     (ColorRoles.Warning, ColorRoles.OnWarning, brand.Warning),
                     (ColorRoles.Error, ColorRoles.OnError, brand.Error),
                 })
        {
            var palette = TonalPalette.FromInt(ColorUtils.ArgbFromHex(Pick(fixedRole, light)));
            ctx.Fixed(role, Pick(fixedRole, light), [(ColorRoles.Surface, TextRatio), (ColorRoles.SurfaceContainer, TextRatio), (ColorRoles.SurfaceContainerHigh, UiRatio)]);
            t[onRole] = ctx.OnColor(onRole, palette, role, light ? [100, 10, 0] : [20, 10, 0, 100]);
        }

        var errorPalette = TonalPalette.FromInt(ColorUtils.ArgbFromHex(Pick(brand.Error, light)));
        t[ColorRoles.ErrorContainer] = errorPalette.Tone(light ? 90 : 30);
        t[ColorRoles.OnErrorContainer] = ctx.OnColor(ColorRoles.OnErrorContainer, errorPalette, ColorRoles.ErrorContainer, light ? [10, 0, 100] : [90, 95, 100, 10, 0]);

        for (var i = 0; i < brand.Charts.Count; i++)
        {
            t[ColorRoles.Chart(i + 1)] = ColorUtils.ArgbFromHex(Pick(brand.Charts[i], light));
        }

        // Ränder und Sekundärtext gegen die Flächen (K07 Bedienelemente, Marke 2.3 Text).
        ctx.Fixed(ColorRoles.Outline, ColorUtils.HexFromArgb(t[ColorRoles.Outline]), [(ColorRoles.Surface, UiRatio), (ColorRoles.SurfaceContainerHigh, UiRatio)], neutralVariant);
        ctx.Fixed(ColorRoles.OnSurfaceVariant, ColorUtils.HexFromArgb(t[ColorRoles.OnSurfaceVariant]), [(ColorRoles.Surface, TextRatio), (ColorRoles.SurfaceContainerHigh, TextRatio)], neutralVariant);
        ctx.Fixed(ColorRoles.OnSurface, ColorUtils.HexFromArgb(t[ColorRoles.OnSurface]), [(ColorRoles.Surface, TextRatio), (ColorRoles.SurfaceContainerHigh, TextRatio)], neutral);

        var values = t.ToDictionary(p => p.Key, p => ColorUtils.HexFromArgb(p.Value), StringComparer.Ordinal);
        return new ModeTokens(values);
    }

    private static string Pick(FixedRole role, bool light) => light ? role.Hell : role.Dunkel;

    private static int RoundTone(double tone) => (int)Math.Round(tone, MidpointRounding.AwayFromZero);

    /// <summary>Alle Kontrastpaare eines Modus mit gefordertem Verhältnis (Marke 7): Grundlage für den Kontrastbericht und die Tests.</summary>
    public static IReadOnlyList<(string Vordergrund, string Hintergrund, double Verhaeltnis)> RequiredPairs(bool withAccent) =>
    [
        (ColorRoles.OnPrimary, ColorRoles.Primary, TextRatio),
        (ColorRoles.OnPrimaryContainer, ColorRoles.PrimaryContainer, TextRatio),
        (ColorRoles.Primary, ColorRoles.Surface, TextRatio),
        (ColorRoles.Primary, ColorRoles.SurfaceContainer, TextRatio),
        (ColorRoles.Primary, ColorRoles.SurfaceContainerHigh, UiRatio),
        (ColorRoles.OnProgress, ColorRoles.Progress, TextRatio),
        (ColorRoles.OnProgressContainer, ColorRoles.ProgressContainer, TextRatio),
        (ColorRoles.Progress, ColorRoles.Surface, UiRatio),
        (ColorRoles.Progress, ColorRoles.SurfaceContainer, UiRatio),
        (ColorRoles.Progress, ColorRoles.SurfaceContainerHigh, UiRatio),
        (ColorRoles.Progress, ColorRoles.ProgressContainer, UiRatio),
        (ColorRoles.Team, ColorRoles.Surface, UiRatio),
        (ColorRoles.OnTeamContainer, ColorRoles.TeamContainer, TextRatio),
        (ColorRoles.Success, ColorRoles.Surface, TextRatio),
        (ColorRoles.OnSuccess, ColorRoles.Success, TextRatio),
        (ColorRoles.Warning, ColorRoles.Surface, TextRatio),
        (ColorRoles.OnWarning, ColorRoles.Warning, TextRatio),
        (ColorRoles.Error, ColorRoles.Surface, TextRatio),
        (ColorRoles.OnError, ColorRoles.Error, TextRatio),
        (ColorRoles.OnErrorContainer, ColorRoles.ErrorContainer, TextRatio),
        (ColorRoles.OnSurface, ColorRoles.Surface, TextRatio),
        (ColorRoles.OnSurface, ColorRoles.SurfaceContainer, TextRatio),
        (ColorRoles.OnSurface, ColorRoles.SurfaceContainerHigh, TextRatio),
        (ColorRoles.OnSurfaceVariant, ColorRoles.Surface, TextRatio),
        (ColorRoles.OnSurfaceVariant, ColorRoles.SurfaceContainerHigh, TextRatio),
        (ColorRoles.Outline, ColorRoles.Surface, UiRatio),
        .. withAccent ? [(ColorRoles.Accent, ColorRoles.Surface, TextRatio), (ColorRoles.OnAccent, ColorRoles.Accent, TextRatio)] : Array.Empty<(string, string, double)>(),
    ];

    private sealed class ModeContext(ThemeMode mode, Dictionary<string, int> tokens, List<Ersetzung> replacements)
    {
        private bool Light => mode == ThemeMode.Hell;

        /// <summary>Angeforderte Farbe, wenn sie alle Paare besteht; sonst der nächstliegende Ton derselben Palette, der besteht.</summary>
        public int Nearest(string role, TonalPalette palette, int requested, IReadOnlyList<(string Against, double Ratio)> constraints)
        {
            if (Passes(requested, constraints))
            {
                return requested;
            }

            var requestedTone = RoundTone(Hct.FromInt(requested).Tone);
            var best = requested;
            var bestDistance = int.MaxValue;
            for (var tone = 0; tone <= 100; tone++)
            {
                var candidate = palette.Tone(tone);
                if (!Passes(candidate, constraints))
                {
                    continue;
                }

                var distance = Math.Abs(tone - requestedTone);
                // Gleichstand: hell zur dunkleren, dunkel zur helleren Ableitung.
                var preferred = distance < bestDistance || (distance == bestDistance && (Light ? tone < RoundTone(Hct.FromInt(best).Tone) : tone > RoundTone(Hct.FromInt(best).Tone)));
                if (preferred)
                {
                    best = candidate;
                    bestDistance = distance;
                }
            }

            if (bestDistance == int.MaxValue)
            {
                // Kein Ton der Palette besteht alle Paare: Schwarz oder Weiß derselben Palette mit dem höchsten Mindestkontrast.
                best = MinRatio(palette.Tone(0), constraints) >= MinRatio(palette.Tone(100), constraints) ? palette.Tone(0) : palette.Tone(100);
            }

            Record(role, requested, best, constraints);
            return best;
        }

        /// <summary>Textfarbe auf einer Fläche: erster Kandidat (Ton der Palette), der 4,5:1 erreicht; sonst der kontrastreichste.</summary>
        public int OnColor(string role, TonalPalette palette, string background, int[] candidateTones)
        {
            var bg = tokens[background];
            var first = palette.Tone(candidateTones[0]);
            var best = first;
            var bestRatio = 0.0;
            foreach (var tone in candidateTones)
            {
                var candidate = palette.Tone(tone);
                var ratio = Contrast.Ratio(candidate, bg);
                if (ratio >= TextRatio)
                {
                    if (candidate != first)
                    {
                        Record(role, first, candidate, [(background, TextRatio)]);
                    }

                    return candidate;
                }

                if (ratio > bestRatio)
                {
                    best = candidate;
                    bestRatio = ratio;
                }
            }

            Record(role, first, best, [(background, TextRatio)]);
            return best;
        }

        /// <summary>Plattformweit feste Rolle: geprüft; ersetzt nur bei fehlendem Kontrast durch den nächsten Ton derselben Farbe.</summary>
        public void Fixed(string role, string hex, IReadOnlyList<(string Against, double Ratio)> constraints, TonalPalette? palette = null)
        {
            var requested = ColorUtils.ArgbFromHex(hex);
            tokens[role] = Nearest(role, palette ?? TonalPalette.FromInt(requested), requested, constraints);
        }

        private bool Passes(int candidate, IReadOnlyList<(string Against, double Ratio)> constraints) =>
            constraints.All(c => Contrast.Ratio(candidate, tokens[c.Against]) >= c.Ratio);

        private double MinRatio(int candidate, IReadOnlyList<(string Against, double Ratio)> constraints) =>
            constraints.Count == 0 ? double.MaxValue : constraints.Min(c => Contrast.Ratio(candidate, tokens[c.Against]) / c.Ratio);

        private void Record(string role, int requested, int replaced, IReadOnlyList<(string Against, double Ratio)> constraints)
        {
            if (requested == replaced)
            {
                return;
            }

            var failing = constraints
                .Where(c => Contrast.Ratio(requested, tokens[c.Against]) < c.Ratio)
                .Select(c => string.Create(CultureInfo.InvariantCulture, $"{c.Against} {Contrast.Ratio(requested, tokens[c.Against]):0.00}:1 unter {c.Ratio:0.0}:1"));
            replacements.Add(new Ersetzung(mode, role, ColorUtils.HexFromArgb(requested), ColorUtils.HexFromArgb(replaced), "Kontrast gegen " + string.Join(", ", failing)));
        }
    }
}
