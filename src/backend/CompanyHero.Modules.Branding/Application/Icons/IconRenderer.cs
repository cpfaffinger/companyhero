using System.Globalization;
using System.IO.Compression;
using System.Text;
using CompanyHero.Modules.Branding.Domain.Color;

namespace CompanyHero.Modules.Branding.Application.Icons;

/// <summary>
/// Bildzeichen der Plattform in Tenant-Farbe (Marke 4.1, 5): ein aufsteigender Bogen aus drei Segmenten in <c>primary</c> und
/// <c>progress</c> auf <c>primary-container</c>. Erzeugt PNG (ohne Bibliothek, deterministisch) in allen benötigten Größen
/// einschließlich maskierbarer Variante sowie SVG. Ein hochgeladenes Tenant-Logo ersetzt das Bildzeichen mit der Medienverarbeitung.
/// </summary>
public static class IconRenderer
{
    // Geometrie in Einheitskoordinaten (y nach unten): Bogenmitte, Radius, Strichstärke, drei Segmente in Grad.
    private const double CenterX = 0.5;
    private const double CenterY = 0.72;
    private const double Radius = 0.36;
    private const double Stroke = 0.105;
    private static readonly (double From, double To, bool Progress)[] Segments = [(200, 242, false), (250, 290, false), (298, 340, true)];

    /// <summary>Icon als PNG: abgerundetes Quadrat (Standard) oder randlos mit Sicherheitszone (maskierbar).</summary>
    public static byte[] Png(int size, string primary, string progress, string background, bool maskable)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 16);
        var p = Rgb(primary);
        var g = Rgb(progress);
        var bg = Rgb(background);
        var pixels = new byte[size * size * 4];
        var pixel = 1.0 / size;
        // Maskierbar: Inhalt in die 80-%-Sicherheitszone gestaucht; Standard: Ecken mit Radius 20 % transparent.
        var scale = maskable ? 0.8 : 1.0;
        var offset = (1.0 - scale) / 2.0;
        var corner = 0.2;

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var ux = (x + 0.5) * pixel;
                var uy = (y + 0.5) * pixel;
                var alpha = maskable ? 1.0 : RoundedSquareCoverage(ux, uy, corner, pixel);
                var (r, gr, b) = bg;
                var lx = (ux - offset) / scale;
                var ly = (uy - offset) / scale;
                foreach (var (from, to, isProgress) in Segments)
                {
                    var coverage = ArcCoverage(lx, ly, from, to, pixel / scale);
                    if (coverage <= 0)
                    {
                        continue;
                    }

                    var (cr, cg, cb) = isProgress ? g : p;
                    r = Blend(r, cr, coverage);
                    gr = Blend(gr, cg, coverage);
                    b = Blend(b, cb, coverage);
                }

                var i = (y * size + x) * 4;
                pixels[i] = r;
                pixels[i + 1] = gr;
                pixels[i + 2] = b;
                pixels[i + 3] = (byte)Math.Round(alpha * 255, MidpointRounding.AwayFromZero);
            }
        }

        return PngEncoder.Encode(size, size, pixels);
    }

    /// <summary>Dasselbe Bildzeichen als SVG (<c>sizes="any"</c>).</summary>
    public static string Svg(string primary, string progress, string background)
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 100 100\" role=\"img\" aria-label=\"Bildzeichen\">");
        sb.Append(CultureInfo.InvariantCulture, $"<rect width=\"100\" height=\"100\" rx=\"20\" fill=\"{background}\"/>");
        foreach (var (from, to, isProgress) in Segments)
        {
            var (x1, y1) = Point(from);
            var (x2, y2) = Point(to);
            sb.Append(CultureInfo.InvariantCulture, $"<path d=\"M{x1:0.##} {y1:0.##} A{Radius * 100:0.##} {Radius * 100:0.##} 0 0 1 {x2:0.##} {y2:0.##}\" fill=\"none\" stroke=\"{(isProgress ? progress : primary)}\" stroke-width=\"{Stroke * 100:0.##}\" stroke-linecap=\"round\"/>");
        }

        sb.Append("</svg>");
        return sb.ToString();
    }

    private static (double X, double Y) Point(double degrees)
    {
        var rad = degrees * Math.PI / 180.0;
        return ((CenterX + Radius * Math.Cos(rad)) * 100, (CenterY + Radius * Math.Sin(rad)) * 100);
    }

    private static double ArcCoverage(double x, double y, double from, double to, double pixel)
    {
        var dx = x - CenterX;
        var dy = y - CenterY;
        var angle = Math.Atan2(dy, dx) * 180.0 / Math.PI;
        if (angle < 0)
        {
            angle += 360;
        }

        double distance;
        if (angle >= from && angle <= to)
        {
            distance = Math.Abs(Math.Sqrt(dx * dx + dy * dy) - Radius);
        }
        else
        {
            var (ax, ay) = EndPoint(from);
            var (bx, by) = EndPoint(to);
            distance = Math.Min(Math.Sqrt((x - ax) * (x - ax) + (y - ay) * (y - ay)), Math.Sqrt((x - bx) * (x - bx) + (y - by) * (y - by)));
        }

        return Math.Clamp(0.5 + (Stroke / 2 - distance) / pixel, 0.0, 1.0);
    }

    private static (double X, double Y) EndPoint(double degrees)
    {
        var rad = degrees * Math.PI / 180.0;
        return (CenterX + Radius * Math.Cos(rad), CenterY + Radius * Math.Sin(rad));
    }

    private static double RoundedSquareCoverage(double x, double y, double radius, double pixel)
    {
        var cx = Math.Clamp(x, radius, 1 - radius);
        var cy = Math.Clamp(y, radius, 1 - radius);
        var distance = Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) - radius;
        return Math.Clamp(0.5 - distance / pixel, 0.0, 1.0);
    }

    private static byte Blend(byte under, byte over, double coverage) => (byte)Math.Round(under + (over - under) * coverage, MidpointRounding.AwayFromZero);

    private static (byte R, byte G, byte B) Rgb(string hex)
    {
        var argb = ColorUtils.ArgbFromHex(hex);
        return ((byte)ColorUtils.RedFromArgb(argb), (byte)ColorUtils.GreenFromArgb(argb), (byte)ColorUtils.BlueFromArgb(argb));
    }
}

/// <summary>Minimaler PNG-Kodierer (RGBA 8 Bit, Filter 0, zlib), ohne zusätzliche Abhängigkeit.</summary>
internal static class PngEncoder
{
    private static readonly uint[] CrcTable = BuildCrcTable();

    public static byte[] Encode(int width, int height, byte[] rgba)
    {
        var raw = new byte[height * (width * 4 + 1)];
        for (var y = 0; y < height; y++)
        {
            var row = y * (width * 4 + 1);
            raw[row] = 0;
            Buffer.BlockCopy(rgba, y * width * 4, raw, row + 1, width * 4);
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(raw, 0, raw.Length);
        }

        using var output = new MemoryStream();
        output.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var ihdr = new byte[13];
        WriteBigEndian(ihdr, 0, width);
        WriteBigEndian(ihdr, 4, height);
        ihdr[8] = 8;
        ihdr[9] = 6;
        WriteChunk(output, "IHDR", ihdr);
        WriteChunk(output, "IDAT", compressed.ToArray());
        WriteChunk(output, "IEND", []);
        return output.ToArray();
    }

    private static void WriteChunk(Stream output, string type, byte[] data)
    {
        var length = new byte[4];
        WriteBigEndian(length, 0, data.Length);
        output.Write(length);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes);
        output.Write(data);
        var crc = Crc32(typeBytes, data);
        var crcBytes = new byte[4];
        WriteBigEndian(crcBytes, 0, unchecked((int)crc));
        output.Write(crcBytes);
    }

    private static void WriteBigEndian(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    private static uint Crc32(byte[] type, byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in type)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        foreach (var b in data)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return crc ^ 0xFFFFFFFFu;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }
}
