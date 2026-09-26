using CompanyHero.Modules.Branding.Domain.Color;

namespace CompanyHero.Domain.Tests;

/// <summary>
/// A-013, A-077: Die Ableitung ist deterministisch und mit Referenzwerten getestet. Die Erwartungswerte stammen aus den
/// veröffentlichten Tests des Material-Verfahrens (material-color-utilities, Apache-2.0: hct_test, palettes_test,
/// contrast_test); die Portierung muss sie exakt reproduzieren.
/// </summary>
public sealed class HctReferenceTests
{
    private const int Red = unchecked((int)0xFFFF0000);
    private const int Green = unchecked((int)0xFF00FF00);
    private const int Blue = unchecked((int)0xFF0000FF);
    private const int White = unchecked((int)0xFFFFFFFF);
    private const int Black = unchecked((int)0xFF000000);

    [Theory]
    [InlineData(Red, 27.408, 113.357, 46.445, 89.494, 91.889, 105.988)]
    [InlineData(Green, 142.139, 108.410, 79.331, 85.587, 78.604, 138.520)]
    [InlineData(Blue, 282.788, 87.230, 25.465, 68.867, 93.674, 78.481)]
    [InlineData(White, 209.492, 2.869, 100.0, 2.265, 12.068, 155.521)]
    [InlineData(Black, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0)]
    public void Cam16_liefert_die_veroeffentlichten_Erscheinungswerte(int argb, double hue, double chroma, double j, double m, double s, double q)
    {
        // Veröffentlichte Werte sind auf drei Nachkommastellen gerundet; Toleranz 0,001.
        var cam = Cam16.FromInt(argb);
        Assert.Equal(hue, cam.Hue, 0.001);
        Assert.Equal(chroma, cam.Chroma, 0.001);
        Assert.Equal(j, cam.J, 0.001);
        Assert.Equal(m, cam.M, 0.001);
        Assert.Equal(s, cam.S, 0.001);
        Assert.Equal(q, cam.Q, 0.001);
    }

    [Theory]
    [InlineData(Green, 142.139, 108.410, 87.737)]
    [InlineData(Blue, 282.788, 87.230, 32.302)]
    public void Hct_zerlegt_Farben_in_Farbton_Chroma_und_Ton(int argb, double hue, double chroma, double tone)
    {
        var hct = Hct.FromInt(argb);
        Assert.Equal(hue, hct.Hue, 0.001);
        Assert.Equal(chroma, hct.Chroma, 0.001);
        Assert.Equal(tone, hct.Tone, 0.001);
    }

    [Fact]
    public void Blau_auf_Ton_90_gesetzt_verliert_Chroma_und_behaelt_den_Farbton()
    {
        var hct = Hct.FromInt(Blue).WithTone(90.0);
        Assert.Equal(282.239, hct.Hue, 0.001);
        Assert.Equal(19.144, hct.Chroma, 0.001);
        Assert.Equal(90.035, hct.Tone, 0.001);
    }

    [Theory]
    [InlineData(100, 0xFFFFFFFF)]
    [InlineData(95, 0xFFF1EFFF)]
    [InlineData(90, 0xFFE0E0FF)]
    [InlineData(80, 0xFFBEC2FF)]
    [InlineData(70, 0xFF9DA3FF)]
    [InlineData(60, 0xFF7C84FF)]
    [InlineData(50, 0xFF5A64FF)]
    [InlineData(40, 0xFF343DFF)]
    [InlineData(30, 0xFF0000EF)]
    [InlineData(20, 0xFF0001AC)]
    [InlineData(10, 0xFF00006E)]
    [InlineData(0, 0xFF000000)]
    public void Tonpalette_von_Blau_liefert_die_veroeffentlichten_Toene(int tone, uint expected)
    {
        var palette = TonalPalette.FromInt(Blue);
        Assert.Equal(unchecked((int)expected), palette.Tone(tone));
    }

    [Fact]
    public void Hct_Rundreise_erhaelt_die_Farbe_und_Toene_bleiben_innerhalb_der_Toleranz()
    {
        // Jede zweite Farbe des sRGB-Würfels in groben Schritten: Hue/Chroma/Tone -> ARGB -> HCT trifft wieder.
        for (var r = 0; r <= 255; r += 51)
        {
            for (var g = 0; g <= 255; g += 51)
            {
                for (var b = 0; b <= 255; b += 51)
                {
                    var argb = ColorUtils.ArgbFromRgb(r, g, b);
                    var hct = Hct.FromInt(argb);
                    var roundtrip = Hct.From(hct.Hue, hct.Chroma, hct.Tone);
                    Assert.Equal(argb, roundtrip.ToInt());
                }
            }
        }
    }

    [Fact]
    public void Kontrastverhaeltnis_folgt_WCAG_und_ist_symmetrisch()
    {
        Assert.Equal(21.0, Contrast.RatioOfTones(-10.0, 110.0), 3);
        Assert.Equal(21.0, Contrast.Ratio(White, Black), 3);
        Assert.Equal(Contrast.Ratio(Red, White), Contrast.Ratio(White, Red), 6);
        // Bekannter Grenzfall aus den WCAG-Beispielen: #767676 auf Weiß liegt knapp über 4,5:1.
        Assert.InRange(Contrast.Ratio(unchecked((int)0xFF767676), White), 4.5, 4.6);
        Assert.Equal(-1.0, Contrast.Lighter(90.0, 10.0), 3);
        Assert.Equal(-1.0, Contrast.Darker(10.0, 20.0), 3);
        Assert.Equal(100.0, Contrast.LighterUnsafe(100.0, 2.0), 3);
        Assert.Equal(0.0, Contrast.DarkerUnsafe(0.0, 2.0), 3);
    }

    [Fact]
    public void Hexdarstellung_ist_kulturunabhaengig_und_grossgeschrieben()
    {
        Assert.Equal("#9B1B3A", ColorUtils.HexFromArgb(ColorUtils.ArgbFromHex("#9b1b3a")));
        Assert.True(ColorUtils.TryParseHex("#0F7A45", out var argb));
        Assert.Equal(unchecked((int)0xFF0F7A45), argb);
        Assert.False(ColorUtils.TryParseHex("0F7A45", out _));
        Assert.False(ColorUtils.TryParseHex("#GGGGGG", out _));
        Assert.False(ColorUtils.TryParseHex("#FFF", out _));
    }
}
