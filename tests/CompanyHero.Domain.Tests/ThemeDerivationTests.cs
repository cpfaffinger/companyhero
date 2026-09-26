using CompanyHero.Modules.Branding.Domain.Color;
using CompanyHero.Modules.Branding.Domain.Theme;

namespace CompanyHero.Domain.Tests;

/// <summary>
/// Marke 3.3, 9.1 bis 9.3, A-013, A-077, K06: Ableitung aus der Saatfarbe, Saatfarbe immer angenommen, Fortschrittsfarbe warm
/// mit Farbtonabstand und nie Blau, alle Rollenpaare bestehen 4,5:1 beziehungsweise 3:1 in beiden Modi, Ersetzungen mit Grund,
/// ungültige Eingabe fällt auf das Standard-Theme zurück. Reine Fachregeln ohne Infrastruktur.
/// </summary>
public sealed class ThemeDerivationTests
{
    private static ThemeDocument Doc(string seed, string? accent = null) =>
        new(ThemeDocument.CurrentSchema, "Test", seed, accent, Anrede.Du, Tonalitaet.Freundlich, Bezeichnungen.Standard, null);

    public static TheoryData<string> Seeds() => ["#9B1B3A", "#0F7A45", "#2A45C9", "#E4670A", "#FFD700", "#808080", "#000000", "#FFFFFF", "#00FFFF", "#FF00FF"];

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Alle_Rollenpaare_bestehen_in_beiden_Modi_und_alle_Rollen_sind_belegt(string seed)
    {
        var tokens = ThemeDerivation.Derive(Doc(seed), PlatformBrand.Default);

        foreach (var mode in new[] { tokens.Hell, tokens.Dunkel })
        {
            foreach (var role in ColorRoles.Required)
            {
                Assert.True(ColorUtils.TryParseHex(mode[role], out _), $"{seed}: Rolle {role} fehlt oder ist kein #RRGGBB");
            }

            foreach (var (fg, bg, ratio) in ThemeDerivation.RequiredPairs(withAccent: false))
            {
                var actual = Contrast.Ratio(ColorUtils.ArgbFromHex(mode[fg]), ColorUtils.ArgbFromHex(mode[bg]));
                Assert.True(actual >= ratio, $"{seed}: {fg} auf {bg} hat {actual:0.00}:1, gefordert {ratio}:1");
            }
        }
    }

    [Theory]
    [InlineData("#9B1B3A")]
    [InlineData("#0F7A45")]
    [InlineData("#2A45C9")]
    public void Saatfarbe_mit_ausreichendem_Kontrast_wird_unveraendert_als_primary_hell_uebernommen(string seed)
    {
        var tokens = ThemeDerivation.Derive(Doc(seed), PlatformBrand.Default);
        Assert.Equal(seed, tokens.Hell[ColorRoles.Primary]);
        Assert.DoesNotContain(tokens.Ersetzungen, e => e.Rolle == ColorRoles.Primary);
    }

    [Fact]
    public void Orange_Saatfarbe_wird_angenommen_und_nur_dort_ersetzt_wo_sie_unlesbar_waere_mit_Grund()
    {
        var tokens = ThemeDerivation.Derive(Doc("#E4670A"), PlatformBrand.Default);

        var replacement = Assert.Single(tokens.Ersetzungen, e => e.Rolle == ColorRoles.Primary && e.Modus == ThemeMode.Hell);
        Assert.Equal("#E4670A", replacement.Angefordert);
        Assert.Contains("surface", replacement.Grund, StringComparison.Ordinal);
        Assert.Contains("4.5:1", replacement.Grund, StringComparison.Ordinal);

        // Dieselbe Farbe, nur dunklerer Ton: Farbton bleibt, Kontrast reicht.
        var seed = Hct.FromInt(ColorUtils.ArgbFromHex("#E4670A"));
        var primary = Hct.FromInt(ColorUtils.ArgbFromHex(tokens.Hell[ColorRoles.Primary]));
        Assert.True(MathUtils_DifferenceDegrees(seed.Hue, primary.Hue) < 8, "Farbton der Ersetzung weicht von der Saatfarbe ab");
        Assert.True(primary.Tone < seed.Tone);
        Assert.True(Contrast.Ratio(ColorUtils.ArgbFromHex(tokens.Hell[ColorRoles.Primary]), ColorUtils.ArgbFromHex(tokens.Hell[ColorRoles.Surface])) >= 4.5);
    }

    [Theory]
    [InlineData("#9B1B3A")]
    [InlineData("#E4670A")]
    [InlineData("#FF0000")]
    [InlineData("#2A45C9")]
    [InlineData("#0F7A45")]
    public void Fortschrittsfarbe_ist_warm_haelt_Abstand_zur_Saatfarbe_und_ist_nie_Blau(string seed)
    {
        var tokens = ThemeDerivation.Derive(Doc(seed), PlatformBrand.Default);
        var seedHue = Hct.FromInt(ColorUtils.ArgbFromHex(seed)).Hue;

        foreach (var mode in new[] { tokens.Hell, tokens.Dunkel })
        {
            var progress = Hct.FromInt(ColorUtils.ArgbFromHex(mode[ColorRoles.Progress]));
            Assert.InRange(progress.Hue, ThemeDerivation.WarmBandStart - 5, ThemeDerivation.WarmBandEnd + 5);
            Assert.False(Hct.IsBlue(progress.Hue));
            Assert.True(MathUtils_DifferenceDegrees(seedHue, progress.Hue) >= ThemeDerivation.MinHueDistance - 5, $"{seed}: Abstand {MathUtils_DifferenceDegrees(seedHue, progress.Hue):0.0}");
            Assert.True(progress.Chroma > 30, "Fortschrittsfarbe ist kein Grau");
        }
    }

    [Fact]
    public void Warme_Saatfarbe_erhaelt_einen_anderen_warmen_Fortschrittston_als_die_Plattform()
    {
        var wiesner = ThemeDerivation.Derive(Doc("#9B1B3A"), PlatformBrand.Default);
        var plattform = ThemeDerivation.Derive(Doc("#2A45C9"), PlatformBrand.Default);
        Assert.NotEqual(plattform.Hell[ColorRoles.Progress], wiesner.Hell[ColorRoles.Progress]);
        Assert.Equal(Hct.FromInt(ColorUtils.ArgbFromHex("#E4670A")).Hue, ThemeDerivation.ProgressHue(Hct.FromInt(ColorUtils.ArgbFromHex("#2A45C9")).Hue, Hct.FromInt(ColorUtils.ArgbFromHex("#E4670A")).Hue));
    }

    [Fact]
    public void Ableitung_ist_deterministisch()
    {
        var a = ThemeDerivation.Derive(Doc("#9B1B3A"), PlatformBrand.Default);
        var b = ThemeDerivation.Derive(Doc("#9B1B3A"), PlatformBrand.Default);
        Assert.Equal(a.Hell.Werte, b.Hell.Werte);
        Assert.Equal(a.Dunkel.Werte, b.Dunkel.Werte);
        Assert.Equal(a.Ersetzungen, b.Ersetzungen);
        Assert.Equal(ThemeDerivation.Verfahren, a.Verfahren);
    }

    [Fact]
    public void Plattformweite_Rollen_sind_fuer_alle_Tenants_gleich()
    {
        var wiesner = ThemeDerivation.Derive(Doc("#9B1B3A"), PlatformBrand.Default);
        var hoedl = ThemeDerivation.Derive(Doc("#0F7A45"), PlatformBrand.Default);
        foreach (var role in new[] { ColorRoles.Team, ColorRoles.TeamContainer, ColorRoles.OnTeamContainer, ColorRoles.Success, ColorRoles.Warning, ColorRoles.Error, ColorRoles.Surface, ColorRoles.OnSurface, ColorRoles.Outline, ColorRoles.Chart(1), ColorRoles.Chart(6) })
        {
            Assert.Equal(wiesner.Hell[role], hoedl.Hell[role]);
            Assert.Equal(wiesner.Dunkel[role], hoedl.Dunkel[role]);
        }
    }

    [Fact]
    public void Akzent_wird_nur_mit_Mindestabstand_uebernommen()
    {
        var accepted = ThemeDerivation.Derive(Doc("#2A45C9", "#B3266E"), PlatformBrand.Default);
        Assert.True(accepted.Hell.Werte.ContainsKey(ColorRoles.Accent));
        Assert.True(Contrast.Ratio(ColorUtils.ArgbFromHex(accepted.Hell[ColorRoles.OnAccent]), ColorUtils.ArgbFromHex(accepted.Hell[ColorRoles.Accent])) >= 4.5);

        // Zu nah an der Saatfarbe: nicht übernommen (Marke 3.3 Nr. 3).
        var rejected = ThemeDerivation.Derive(Doc("#2A45C9", "#3A55D9"), PlatformBrand.Default);
        Assert.False(rejected.Hell.Werte.ContainsKey(ColorRoles.Accent));
    }

    [Fact]
    public void Ungueltiges_Dokument_wird_mit_lesbaren_Gruenden_abgelehnt_und_das_Standard_Theme_ist_gueltig()
    {
        var invalid = new ThemeDocument(1, "", "9B1B3A", "#ZZZZZZ", (Anrede)9, Tonalitaet.Sachlich, new Bezeichnungen("", "Serie", ["a"]), new string('x', 500));
        var errors = ThemeDocumentValidator.Validate(invalid);
        Assert.Contains(errors, e => e.Contains("Produktname", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("Saatfarbe", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("Akzentfarbe", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("Anrede", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("Stufen", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("Willkommenstext", StringComparison.Ordinal));
        Assert.Throws<ArgumentException>(() => ThemeDerivation.Derive(invalid, PlatformBrand.Default));

        var standard = ThemeDocument.Standard("CompanyHero", PlatformBrand.Default);
        Assert.Empty(ThemeDocumentValidator.Validate(standard));
        var tokens = ThemeDerivation.Derive(standard, PlatformBrand.Default);
        Assert.Equal("#2A45C9", tokens.Hell[ColorRoles.Primary]);
    }

    private static double MathUtils_DifferenceDegrees(double a, double b) => 180.0 - Math.Abs(Math.Abs(a - b) - 180.0);
}
