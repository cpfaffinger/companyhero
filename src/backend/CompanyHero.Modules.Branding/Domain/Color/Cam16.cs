namespace CompanyHero.Modules.Branding.Domain.Color;

/// <summary>Betrachtungsbedingungen des CAM16-Modells; portiert aus <c>hct/viewing_conditions.ts</c> (Apache-2.0).</summary>
public sealed class ViewingConditions
{
    public static ViewingConditions Default { get; } = Make();

    private ViewingConditions(double n, double aw, double nbb, double ncb, double c, double nc, double[] rgbD, double fl, double fLRoot, double z)
    {
        N = n;
        Aw = aw;
        Nbb = nbb;
        Ncb = ncb;
        C = c;
        Nc = nc;
        RgbD = rgbD;
        Fl = fl;
        FLRoot = fLRoot;
        Z = z;
    }

    public double N { get; }

    public double Aw { get; }

    public double Nbb { get; }

    public double Ncb { get; }

    public double C { get; }

    public double Nc { get; }

    public IReadOnlyList<double> RgbD { get; }

    public double Fl { get; }

    public double FLRoot { get; }

    public double Z { get; }

    public static ViewingConditions Make(double[]? whitePoint = null, double? adaptingLuminance = null, double backgroundLstar = 50.0, double surround = 2.0, bool discountingIlluminant = false)
    {
        var xyz = whitePoint ?? ColorUtils.WhitePointD65();
        var adapting = adaptingLuminance ?? (200.0 / Math.PI) * ColorUtils.YFromLstar(50.0) / 100.0;
        var rW = xyz[0] * 0.401288 + xyz[1] * 0.650173 + xyz[2] * -0.051461;
        var gW = xyz[0] * -0.250268 + xyz[1] * 1.204414 + xyz[2] * 0.045854;
        var bW = xyz[0] * -0.002079 + xyz[1] * 0.048952 + xyz[2] * 0.953127;
        var f = 0.8 + surround / 10.0;
        var c = f >= 0.9 ? MathUtils.Lerp(0.59, 0.69, (f - 0.9) * 10.0) : MathUtils.Lerp(0.525, 0.59, (f - 0.8) * 10.0);
        var d = discountingIlluminant ? 1.0 : f * (1.0 - (1.0 / 3.6) * Math.Exp((-adapting - 42.0) / 92.0));
        d = d > 1.0 ? 1.0 : d < 0.0 ? 0.0 : d;
        var nc = f;
        double[] rgbD = [d * (100.0 / rW) + 1.0 - d, d * (100.0 / gW) + 1.0 - d, d * (100.0 / bW) + 1.0 - d];
        var k = 1.0 / (5.0 * adapting + 1.0);
        var k4 = k * k * k * k;
        var k4F = 1.0 - k4;
        var fl = k4 * adapting + 0.1 * k4F * k4F * Math.Cbrt(5.0 * adapting);
        var n = ColorUtils.YFromLstar(backgroundLstar) / xyz[1];
        var z = 1.48 + Math.Sqrt(n);
        var nbb = 0.725 / Math.Pow(n, 0.2);
        var ncb = nbb;
        double[] rgbAFactors =
        [
            Math.Pow(fl * rgbD[0] * rW / 100.0, 0.42),
            Math.Pow(fl * rgbD[1] * gW / 100.0, 0.42),
            Math.Pow(fl * rgbD[2] * bW / 100.0, 0.42),
        ];
        double[] rgbA =
        [
            400.0 * rgbAFactors[0] / (rgbAFactors[0] + 27.13),
            400.0 * rgbAFactors[1] / (rgbAFactors[1] + 27.13),
            400.0 * rgbAFactors[2] / (rgbAFactors[2] + 27.13),
        ];
        var aw = (2.0 * rgbA[0] + rgbA[1] + 0.05 * rgbA[2]) * nbb;
        return new ViewingConditions(n, aw, nbb, ncb, c, nc, rgbD, fl, Math.Pow(fl, 0.25), z);
    }
}

/// <summary>CAM16-Erscheinung einer Farbe; portiert aus <c>hct/cam16.ts</c> (Apache-2.0).</summary>
public sealed record Cam16(double Hue, double Chroma, double J, double Q, double M, double S, double Jstar, double Astar, double Bstar)
{
    public static Cam16 FromInt(int argb) => FromIntInViewingConditions(argb, ViewingConditions.Default);

    public static Cam16 FromIntInViewingConditions(int argb, ViewingConditions vc)
    {
        ArgumentNullException.ThrowIfNull(vc);
        var redL = ColorUtils.Linearized(ColorUtils.RedFromArgb(argb));
        var greenL = ColorUtils.Linearized(ColorUtils.GreenFromArgb(argb));
        var blueL = ColorUtils.Linearized(ColorUtils.BlueFromArgb(argb));
        var x = 0.41233895 * redL + 0.35762064 * greenL + 0.18051042 * blueL;
        var y = 0.2126 * redL + 0.7152 * greenL + 0.0722 * blueL;
        var z = 0.01932141 * redL + 0.11916382 * greenL + 0.95034478 * blueL;
        return FromXyzInViewingConditions(x, y, z, vc);
    }

    public static Cam16 FromXyzInViewingConditions(double x, double y, double z, ViewingConditions vc)
    {
        ArgumentNullException.ThrowIfNull(vc);
        var rC = 0.401288 * x + 0.650173 * y - 0.051461 * z;
        var gC = -0.250268 * x + 1.204414 * y + 0.045854 * z;
        var bC = -0.002079 * x + 0.048952 * y + 0.953127 * z;

        var rD = vc.RgbD[0] * rC;
        var gD = vc.RgbD[1] * gC;
        var bD = vc.RgbD[2] * bC;

        var rAF = Math.Pow(vc.Fl * Math.Abs(rD) / 100.0, 0.42);
        var gAF = Math.Pow(vc.Fl * Math.Abs(gD) / 100.0, 0.42);
        var bAF = Math.Pow(vc.Fl * Math.Abs(bD) / 100.0, 0.42);
        var rA = MathUtils.Signum(rD) * 400.0 * rAF / (rAF + 27.13);
        var gA = MathUtils.Signum(gD) * 400.0 * gAF / (gAF + 27.13);
        var bA = MathUtils.Signum(bD) * 400.0 * bAF / (bAF + 27.13);

        var a = (11.0 * rA + -12.0 * gA + bA) / 11.0;
        var b = (rA + gA - 2.0 * bA) / 9.0;
        var u = (20.0 * rA + 20.0 * gA + 21.0 * bA) / 20.0;
        var p2 = (40.0 * rA + 20.0 * gA + bA) / 20.0;

        var atanDegrees = Math.Atan2(b, a) * 180.0 / Math.PI;
        var hue = atanDegrees < 0 ? atanDegrees + 360.0 : atanDegrees >= 360 ? atanDegrees - 360 : atanDegrees;
        var hueRadians = hue * Math.PI / 180.0;

        var ac = p2 * vc.Nbb;
        var j = 100.0 * Math.Pow(ac / vc.Aw, vc.C * vc.Z);
        var q = 4.0 / vc.C * Math.Sqrt(j / 100.0) * (vc.Aw + 4.0) * vc.FLRoot;

        var huePrime = hue < 20.14 ? hue + 360 : hue;
        var eHue = 0.25 * (Math.Cos(huePrime * Math.PI / 180.0 + 2.0) + 3.8);
        var p1 = 50000.0 / 13.0 * eHue * vc.Nc * vc.Ncb;
        var t = p1 * Math.Sqrt(a * a + b * b) / (u + 0.305);
        var alpha = Math.Pow(t, 0.9) * Math.Pow(1.64 - Math.Pow(0.29, vc.N), 0.73);
        var c = alpha * Math.Sqrt(j / 100.0);
        var m = c * vc.FLRoot;
        var s = 50.0 * Math.Sqrt(alpha * vc.C / (vc.Aw + 4.0));

        var jstar = (1.0 + 100.0 * 0.007) * j / (1.0 + 0.007 * j);
        var mstar = Math.Log(1.0 + 0.0228 * m) / 0.0228;
        var astar = mstar * Math.Cos(hueRadians);
        var bstar = mstar * Math.Sin(hueRadians);
        return new Cam16(hue, c, j, q, m, s, jstar, astar, bstar);
    }

    public int ToInt() => Viewed(ViewingConditions.Default);

    public int Viewed(ViewingConditions vc)
    {
        var xyz = XyzInViewingConditions(vc);
        return ColorUtils.ArgbFromXyz(xyz[0], xyz[1], xyz[2]);
    }

    public double[] XyzInViewingConditions(ViewingConditions vc)
    {
        ArgumentNullException.ThrowIfNull(vc);
        var alpha = Chroma == 0.0 || J == 0.0 ? 0.0 : Chroma / Math.Sqrt(J / 100.0);
        var t = Math.Pow(alpha / Math.Pow(1.64 - Math.Pow(0.29, vc.N), 0.73), 1.0 / 0.9);
        var hRad = Hue * Math.PI / 180.0;
        var eHue = 0.25 * (Math.Cos(hRad + 2.0) + 3.8);
        var ac = vc.Aw * Math.Pow(J / 100.0, 1.0 / vc.C / vc.Z);
        var p1 = eHue * (50000.0 / 13.0) * vc.Nc * vc.Ncb;
        var p2 = ac / vc.Nbb;
        var hSin = Math.Sin(hRad);
        var hCos = Math.Cos(hRad);
        var gamma = 23.0 * (p2 + 0.305) * t / (23.0 * p1 + 11.0 * t * hCos + 108.0 * t * hSin);
        var a = gamma * hCos;
        var b = gamma * hSin;
        var rA = (460.0 * p2 + 451.0 * a + 288.0 * b) / 1403.0;
        var gA = (460.0 * p2 - 891.0 * a - 261.0 * b) / 1403.0;
        var bA = (460.0 * p2 - 220.0 * a - 6300.0 * b) / 1403.0;

        var rCBase = Math.Max(0, 27.13 * Math.Abs(rA) / (400.0 - Math.Abs(rA)));
        var rC = MathUtils.Signum(rA) * (100.0 / vc.Fl) * Math.Pow(rCBase, 1.0 / 0.42);
        var gCBase = Math.Max(0, 27.13 * Math.Abs(gA) / (400.0 - Math.Abs(gA)));
        var gC = MathUtils.Signum(gA) * (100.0 / vc.Fl) * Math.Pow(gCBase, 1.0 / 0.42);
        var bCBase = Math.Max(0, 27.13 * Math.Abs(bA) / (400.0 - Math.Abs(bA)));
        var bC = MathUtils.Signum(bA) * (100.0 / vc.Fl) * Math.Pow(bCBase, 1.0 / 0.42);
        var rF = rC / vc.RgbD[0];
        var gF = gC / vc.RgbD[1];
        var bF = bC / vc.RgbD[2];

        var x = 1.86206786 * rF - 1.01125463 * gF + 0.14918677 * bF;
        var y = 0.38752654 * rF + 0.62144744 * gF - 0.00897398 * bF;
        var z = -0.01584150 * rF - 0.03412294 * gF + 1.04996444 * bF;
        return [x, y, z];
    }
}
