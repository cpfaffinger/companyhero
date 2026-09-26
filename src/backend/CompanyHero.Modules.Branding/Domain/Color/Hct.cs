namespace CompanyHero.Modules.Branding.Domain.Color;

/// <summary>
/// Farbe im HCT-Farbraum (Farbton, Chroma, Ton = L*); portiert aus <c>hct/hct.ts</c> (Apache-2.0). Unveränderlich;
/// Änderungen erzeugen über den Solver eine neue, im sRGB-Gamut liegende Farbe.
/// </summary>
public sealed class Hct
{
    private Hct(int argb)
    {
        var cam = Cam16.FromInt(argb);
        Hue = cam.Hue;
        Chroma = cam.Chroma;
        Tone = ColorUtils.LstarFromArgb(argb);
        Argb = argb;
    }

    public double Hue { get; }

    public double Chroma { get; }

    public double Tone { get; }

    private int Argb { get; }

    public static Hct From(double hue, double chroma, double tone) => new(HctSolver.SolveToInt(hue, chroma, tone));

    public static Hct FromInt(int argb) => new(argb);

    public int ToInt() => Argb;

    public string ToHex() => ColorUtils.HexFromArgb(Argb);

    public Hct WithTone(double tone) => From(Hue, Chroma, tone);

    public Hct WithChroma(double chroma) => From(Hue, chroma, Tone);

    public Hct WithHue(double hue) => From(hue, Chroma, Tone);

    /// <summary>Blau nach dem veröffentlichten Verfahren (Farbtonband 250 bis 270).</summary>
    public static bool IsBlue(double hue) => hue is >= 250 and < 270;

    public static bool IsYellow(double hue) => hue is >= 105 and < 125;

    public static bool IsCyan(double hue) => hue is >= 170 and < 207;

    public override string ToString() => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"HCT({Hue:0}, {Chroma:0}, {Tone:0})");
}

/// <summary>
/// Farben mit konstantem Farbton und Chroma bei veränderlichem Ton; portiert aus <c>palettes/tonal_palette.ts</c>
/// (Apache-2.0) einschließlich der Schlüsselfarbe.
/// </summary>
public sealed class TonalPalette
{
    private readonly Dictionary<int, int> _cache = [];

    private TonalPalette(double hue, double chroma, Hct keyColor)
    {
        Hue = hue;
        Chroma = chroma;
        KeyColor = keyColor;
    }

    public double Hue { get; }

    public double Chroma { get; }

    public Hct KeyColor { get; }

    public static TonalPalette FromInt(int argb) => FromHct(Hct.FromInt(argb));

    public static TonalPalette FromHct(Hct hct)
    {
        ArgumentNullException.ThrowIfNull(hct);
        return new TonalPalette(hct.Hue, hct.Chroma, hct);
    }

    public static TonalPalette FromHueAndChroma(double hue, double chroma) => new(hue, chroma, KeyColorFinder.Create(hue, chroma));

    public int Tone(int tone)
    {
        if (_cache.TryGetValue(tone, out var cached))
        {
            return cached;
        }

        int argb;
        if (tone == 99 && Hct.IsYellow(Hue))
        {
            argb = AverageArgb(Tone(98), Tone(100));
        }
        else
        {
            argb = Hct.From(Hue, Chroma, tone).ToInt();
        }

        _cache[tone] = argb;
        return argb;
    }

    public Hct GetHct(int tone) => Hct.FromInt(Tone(tone));

    private static int AverageArgb(int argb1, int argb2)
    {
        var red = (int)Math.Round((ColorUtils.RedFromArgb(argb1) + ColorUtils.RedFromArgb(argb2)) / 2.0, MidpointRounding.AwayFromZero);
        var green = (int)Math.Round((ColorUtils.GreenFromArgb(argb1) + ColorUtils.GreenFromArgb(argb2)) / 2.0, MidpointRounding.AwayFromZero);
        var blue = (int)Math.Round((ColorUtils.BlueFromArgb(argb1) + ColorUtils.BlueFromArgb(argb2)) / 2.0, MidpointRounding.AwayFromZero);
        return ColorUtils.ArgbFromRgb(red, green, blue);
    }

    private static class KeyColorFinder
    {
        private const double MaxChromaValue = 200.0;

        public static Hct Create(double hue, double requestedChroma)
        {
            var chromaCache = new Dictionary<int, double>();
            double MaxChroma(int tone)
            {
                if (!chromaCache.TryGetValue(tone, out var chroma))
                {
                    chroma = Hct.From(hue, MaxChromaValue, tone).Chroma;
                    chromaCache[tone] = chroma;
                }

                return chroma;
            }

            const int pivotTone = 50;
            const int toneStepSize = 1;
            const double epsilon = 0.01;
            var lowerTone = 0;
            var upperTone = 100;
            while (lowerTone < upperTone)
            {
                var midTone = (int)Math.Floor((lowerTone + upperTone) / 2.0);
                var isAscending = MaxChroma(midTone) < MaxChroma(midTone + toneStepSize);
                var sufficientChroma = MaxChroma(midTone) >= requestedChroma - epsilon;
                if (sufficientChroma)
                {
                    if (Math.Abs(lowerTone - pivotTone) < Math.Abs(upperTone - pivotTone))
                    {
                        upperTone = midTone;
                    }
                    else
                    {
                        if (lowerTone == midTone)
                        {
                            return Hct.From(hue, requestedChroma, lowerTone);
                        }

                        lowerTone = midTone;
                    }
                }
                else if (isAscending)
                {
                    lowerTone = midTone + toneStepSize;
                }
                else
                {
                    upperTone = midTone;
                }
            }

            return Hct.From(hue, requestedChroma, lowerTone);
        }
    }
}

/// <summary>Kontrast nach WCAG 2.2 (relative Leuchtdichte); Tonfunktionen portiert aus <c>contrast/contrast.ts</c> (Apache-2.0).</summary>
public static class Contrast
{
    /// <summary>Kontrastverhältnis zweier Farben (1 bis 21), symmetrisch.</summary>
    public static double Ratio(int argbA, int argbB) =>
        RatioOfYs(ColorUtils.XyzFromArgb(argbA)[1], ColorUtils.XyzFromArgb(argbB)[1]);

    public static double RatioOfTones(double toneA, double toneB)
    {
        toneA = MathUtils.ClampDouble(0.0, 100.0, toneA);
        toneB = MathUtils.ClampDouble(0.0, 100.0, toneB);
        return RatioOfYs(ColorUtils.YFromLstar(toneA), ColorUtils.YFromLstar(toneB));
    }

    public static double RatioOfYs(double y1, double y2)
    {
        var lighter = y1 > y2 ? y1 : y2;
        var darker = lighter == y2 ? y1 : y2;
        return (lighter + 5.0) / (darker + 5.0);
    }

    public static double Lighter(double tone, double ratio)
    {
        if (tone is < 0.0 or > 100.0)
        {
            return -1.0;
        }

        var darkY = ColorUtils.YFromLstar(tone);
        var lightY = ratio * (darkY + 5.0) - 5.0;
        var realContrast = RatioOfYs(lightY, darkY);
        var delta = Math.Abs(realContrast - ratio);
        if (realContrast < ratio && delta > 0.04)
        {
            return -1;
        }

        var returnValue = ColorUtils.LstarFromY(lightY) + 0.4;
        return returnValue is < 0 or > 100 ? -1 : returnValue;
    }

    public static double Darker(double tone, double ratio)
    {
        if (tone is < 0.0 or > 100.0)
        {
            return -1.0;
        }

        var lightY = ColorUtils.YFromLstar(tone);
        var darkY = (lightY + 5.0) / ratio - 5.0;
        var realContrast = RatioOfYs(lightY, darkY);
        var delta = Math.Abs(realContrast - ratio);
        if (realContrast < ratio && delta > 0.04)
        {
            return -1;
        }

        var returnValue = ColorUtils.LstarFromY(darkY) - 0.4;
        return returnValue is < 0 or > 100 ? -1 : returnValue;
    }

    public static double LighterUnsafe(double tone, double ratio)
    {
        var lighterSafe = Lighter(tone, ratio);
        return lighterSafe < 0.0 ? 100.0 : lighterSafe;
    }

    public static double DarkerUnsafe(double tone, double ratio)
    {
        var darkerSafe = Darker(tone, ratio);
        return darkerSafe < 0.0 ? 0.0 : darkerSafe;
    }
}
