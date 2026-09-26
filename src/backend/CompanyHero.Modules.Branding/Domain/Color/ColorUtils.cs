using System.Globalization;

namespace CompanyHero.Modules.Branding.Domain.Color;

/// <summary>
/// Farbraumhilfen des Material-Verfahrens (sRGB, XYZ, L*), portiert aus material-color-utilities
/// (Google, Apache-2.0, <c>utils/color_utils.ts</c>). Farben sind ARGB-Ganzzahlen wie im Original.
/// </summary>
public static class ColorUtils
{
    private static readonly double[][] SrgbToXyz =
    [
        [0.41233895, 0.35762064, 0.18051042],
        [0.2126, 0.7152, 0.0722],
        [0.01932141, 0.11916382, 0.95034478],
    ];

    private static readonly double[][] XyzToSrgb =
    [
        [3.2413774792388685, -1.5376652402851851, -0.49885366846268053],
        [-0.9691452513005321, 1.8758853451067872, 0.04156585616912061],
        [0.05562093689691305, -0.20395524564742123, 1.0571799111220335],
    ];

    private static readonly double[] WhitePointD65Values = [95.047, 100.0, 108.883];

    public static double[] WhitePointD65() => [WhitePointD65Values[0], WhitePointD65Values[1], WhitePointD65Values[2]];

    public static int ArgbFromRgb(int red, int green, int blue) =>
        unchecked((int)(0xFF000000u | (uint)((red & 255) << 16) | (uint)((green & 255) << 8) | (uint)(blue & 255)));

    public static int ArgbFromLinrgb(double[] linrgb) =>
        ArgbFromRgb(Delinearized(linrgb[0]), Delinearized(linrgb[1]), Delinearized(linrgb[2]));

    public static int RedFromArgb(int argb) => (argb >> 16) & 255;

    public static int GreenFromArgb(int argb) => (argb >> 8) & 255;

    public static int BlueFromArgb(int argb) => argb & 255;

    public static int ArgbFromXyz(double x, double y, double z)
    {
        var linearR = XyzToSrgb[0][0] * x + XyzToSrgb[0][1] * y + XyzToSrgb[0][2] * z;
        var linearG = XyzToSrgb[1][0] * x + XyzToSrgb[1][1] * y + XyzToSrgb[1][2] * z;
        var linearB = XyzToSrgb[2][0] * x + XyzToSrgb[2][1] * y + XyzToSrgb[2][2] * z;
        return ArgbFromRgb(Delinearized(linearR), Delinearized(linearG), Delinearized(linearB));
    }

    public static double[] XyzFromArgb(int argb)
    {
        var r = Linearized(RedFromArgb(argb));
        var g = Linearized(GreenFromArgb(argb));
        var b = Linearized(BlueFromArgb(argb));
        return MathUtils.MatrixMultiply([r, g, b], SrgbToXyz);
    }

    public static int ArgbFromLstar(double lstar)
    {
        var y = YFromLstar(lstar);
        var component = Delinearized(y);
        return ArgbFromRgb(component, component, component);
    }

    public static double LstarFromArgb(int argb)
    {
        var y = XyzFromArgb(argb)[1];
        return 116.0 * LabF(y / 100.0) - 16.0;
    }

    public static double YFromLstar(double lstar) => 100.0 * LabInvf((lstar + 16.0) / 116.0);

    public static double LstarFromY(double y) => LabF(y / 100.0) * 116.0 - 16.0;

    /// <summary>sRGB-Kanal (0 bis 255) in linearen Wert (0 bis 100).</summary>
    public static double Linearized(int rgbComponent)
    {
        var normalized = rgbComponent / 255.0;
        return normalized <= 0.040449936
            ? normalized / 12.92 * 100.0
            : Math.Pow((normalized + 0.055) / 1.055, 2.4) * 100.0;
    }

    /// <summary>Linearer Wert (0 bis 100) in sRGB-Kanal (0 bis 255).</summary>
    public static int Delinearized(double rgbComponent)
    {
        var normalized = rgbComponent / 100.0;
        var delinearized = normalized <= 0.0031308
            ? normalized * 12.92
            : 1.055 * Math.Pow(normalized, 1.0 / 2.4) - 0.055;
        return MathUtils.ClampInt(0, 255, (int)Math.Round(delinearized * 255.0, MidpointRounding.AwayFromZero));
    }

    /// <summary>Relative Leuchtdichte nach WCAG 2.2 (0 bis 1) aus sRGB.</summary>
    public static double RelativeLuminance(int argb) => XyzFromArgb(argb)[1] / 100.0;

    public static string HexFromArgb(int argb) =>
        string.Create(CultureInfo.InvariantCulture, $"#{RedFromArgb(argb):X2}{GreenFromArgb(argb):X2}{BlueFromArgb(argb):X2}");

    /// <summary>Genau die Form <c>#RRGGBB</c> (Marke 3.2: Farbformat wird im Backend validiert).</summary>
    public static bool TryParseHex(string? hex, out int argb)
    {
        argb = 0;
        if (hex is null || hex.Length != 7 || hex[0] != '#')
        {
            return false;
        }

        if (!int.TryParse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
        {
            return false;
        }

        argb = unchecked((int)(0xFF000000u | (uint)rgb));
        return true;
    }

    public static int ArgbFromHex(string hex) =>
        TryParseHex(hex, out var argb) ? argb : throw new ArgumentException("Farbe im Format #RRGGBB erwartet.", nameof(hex));

    private static double LabF(double t)
    {
        const double e = 216.0 / 24389.0;
        const double kappa = 24389.0 / 27.0;
        return t > e ? Math.Cbrt(t) : (kappa * t + 16) / 116;
    }

    private static double LabInvf(double ft)
    {
        const double e = 216.0 / 24389.0;
        const double kappa = 24389.0 / 27.0;
        var ft3 = ft * ft * ft;
        return ft3 > e ? ft3 : (116 * ft - 16) / kappa;
    }
}

internal static class MathUtils
{
    public static double Signum(double num) => num < 0 ? -1 : num == 0 ? 0 : 1;

    public static double Lerp(double start, double stop, double amount) => (1.0 - amount) * start + amount * stop;

    public static int ClampInt(int min, int max, int input) => input < min ? min : input > max ? max : input;

    public static double ClampDouble(double min, double max, double input) => input < min ? min : input > max ? max : input;

    public static double SanitizeDegreesDouble(double degrees)
    {
        degrees %= 360.0;
        if (degrees < 0)
        {
            degrees += 360.0;
        }

        return degrees;
    }

    public static double DifferenceDegrees(double a, double b) => 180.0 - Math.Abs(Math.Abs(a - b) - 180.0);

    public static double[] MatrixMultiply(double[] row, double[][] matrix)
    {
        var a = row[0] * matrix[0][0] + row[1] * matrix[0][1] + row[2] * matrix[0][2];
        var b = row[0] * matrix[1][0] + row[1] * matrix[1][1] + row[2] * matrix[1][2];
        var c = row[0] * matrix[2][0] + row[1] * matrix[2][1] + row[2] * matrix[2][2];
        return [a, b, c];
    }
}
