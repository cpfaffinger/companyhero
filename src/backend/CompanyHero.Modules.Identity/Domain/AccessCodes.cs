using System.Security.Cryptography;
using System.Text;

namespace CompanyHero.Modules.Identity.Domain;

/// <summary>
/// Codes, Kennungen und Geheimnisse des Zugangs (Zugang 1): zufällig, nicht sequenziell, Alphabet ohne verwechselbare
/// Zeichen (kein I, O, 0, 1). Gespeichert wird nie der Code, sondern sein Hash; Sitzungs- und Gerätegeheimnisse ebenso.
/// </summary>
public static class AccessCodes
{
    public const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public const int JoinCodeLength = 8;
    public const int RecoveryCodeLength = 12;
    public const int RoleCodeLength = 12;
    public const int KioskRegistrationCodeLength = 8;

    /// <summary>Beitrittscode: acht Zeichen (Zugang 2.1).</summary>
    public static string NewJoinCode() => Random(JoinCodeLength);

    /// <summary>Wiederherstellungscode: zwölf Zeichen, angezeigt in Dreiergruppen (Zugang 3.1).</summary>
    public static string NewRecoveryCode() => Random(RecoveryCodeLength);

    public static string NewRoleCode() => Random(RoleCodeLength);

    public static string NewKioskRegistrationCode() => Random(KioskRegistrationCodeLength);

    /// <summary>Kiosk-Kennung: sechs Ziffern, zufällig (Zugang 6.2); Eindeutigkeit je Tenant prüft die Anwendung.</summary>
    public static string NewKioskId() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("000000", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Geheimnis für Sitzungen, Geräte, Magic-Links und Übertragungen: 256 Bit, Base64url.</summary>
    public static string NewSecret() => Base64Url(RandomNumberGenerator.GetBytes(32));

    /// <summary>Anzeige in Dreiergruppen: ABC-DEF-GHJ-KLM.</summary>
    public static string Grouped(string code, int groupSize = 3)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        var builder = new StringBuilder(code.Length + code.Length / groupSize);
        for (var i = 0; i < code.Length; i++)
        {
            if (i > 0 && i % groupSize == 0)
            {
                builder.Append('-');
            }

            builder.Append(code[i]);
        }

        return builder.ToString();
    }

    /// <summary>Eingabe normalisieren: Großschreibung, Trennzeichen und Leerraum entfernt.</summary>
    public static string Normalize(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var builder = new StringBuilder(input.Length);
        foreach (var c in input.ToUpperInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    public static bool IsWellFormed(string normalizedCode, int length) =>
        normalizedCode.Length == length && normalizedCode.All(c => Alphabet.Contains(c, StringComparison.Ordinal));

    /// <summary>SHA-256 als Base64url; für Codes, Geheimnisse und Kennungen (Zugang 3.1: gespeichert wird der Hash).</summary>
    public static string Hash(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    /// <summary>Konstantzeitvergleich zweier Hashes.</summary>
    public static bool HashEquals(string? storedHash, string value) =>
        storedHash is not null && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(storedHash), Encoding.UTF8.GetBytes(Hash(value)));

    public static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Random(int length)
    {
        var chars = new char[length];
        for (var i = 0; i < length; i++)
        {
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return new string(chars);
    }
}
