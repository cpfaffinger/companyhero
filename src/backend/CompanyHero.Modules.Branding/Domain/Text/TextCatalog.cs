using System.Text.Json;
using System.Text.Json.Serialization;
using CompanyHero.Modules.Branding.Domain.Theme;

namespace CompanyHero.Modules.Branding.Domain.Text;

/// <summary>
/// Textkatalog des Operators (Marke 6.2, A-080): je Textschlüssel drei Tonalitätsvarianten, jede mit ausgearbeiteter
/// Anrede Du und Sie; Platzhalter für Bezeichnungen und Bezüge. Tenants schreiben keine Systemtexte, sie wählen Stufe und
/// Anrede und ersetzen Bezeichnungen. Im Durchstich ist der Katalog eine eingebettete Konfiguration in Deutsch.
/// </summary>
public sealed class TextCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip };

    private readonly IReadOnlyDictionary<string, TextEntry> _entries;

    private TextCatalog(IReadOnlyDictionary<string, TextEntry> entries)
    {
        _entries = entries;
    }

    public IReadOnlyCollection<string> Keys => _entries.Keys.ToList();

    /// <summary>Der eingebettete Katalog (Ressource <c>textkatalog.json</c>).</summary>
    public static TextCatalog Embedded { get; } = LoadEmbedded();

    public static TextCatalog Parse(string json)
    {
        var raw = JsonSerializer.Deserialize<Dictionary<string, TextEntry>>(json, JsonOptions)
            ?? throw new InvalidOperationException("Textkatalog leer.");
        var problems = Validate(raw);
        if (problems.Count > 0)
        {
            throw new InvalidOperationException("Textkatalog ungültig: " + string.Join(" ", problems));
        }

        return new TextCatalog(new Dictionary<string, TextEntry>(raw, StringComparer.Ordinal));
    }

    /// <summary>Prüfliste des Operators (Marke 6.2 und 6.3): Vollständigkeit je Stufe und Anrede, Sperrliste, Ausrufezeichen.</summary>
    public static IReadOnlyList<string> Validate(IReadOnlyDictionary<string, TextEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var problems = new List<string>();
        foreach (var (key, entry) in entries)
        {
            foreach (var (tonalitaet, variant) in new[] { (Tonalitaet.Sachlich, entry.Sachlich), (Tonalitaet.Freundlich, entry.Freundlich), (Tonalitaet.Motivierend, entry.Motivierend) })
            {
                if (variant is null || string.IsNullOrWhiteSpace(variant.Du) || string.IsNullOrWhiteSpace(variant.Sie))
                {
                    problems.Add($"{key}: Variante {tonalitaet} fehlt oder ist ohne Du und Sie.");
                    continue;
                }

                foreach (var text in new[] { variant.Du, variant.Sie })
                {
                    var blocked = Sperrliste.Treffer(text);
                    if (blocked is not null)
                    {
                        problems.Add($"{key}: Sperrbegriff „{blocked}“ in Variante {tonalitaet}.");
                    }

                    var exclamations = text.Count(c => c == '!');
                    if (tonalitaet == Tonalitaet.Sachlich && exclamations > 0)
                    {
                        problems.Add($"{key}: Ausrufezeichen in „sachlich“.");
                    }

                    if (tonalitaet == Tonalitaet.Motivierend && exclamations > 1)
                    {
                        problems.Add($"{key}: mehr als ein Ausrufezeichen in „motivierend“.");
                    }
                }
            }
        }

        return problems;
    }

    /// <summary>Alle Texte in Tonalität und Anrede des Tenants mit ersetzten Bezeichnungen; Platzhalter für Bezüge bleiben für die Oberfläche erhalten.</summary>
    public IReadOnlyDictionary<string, string> Resolve(Tonalitaet tonalitaet, Anrede anrede, string produktname, Bezeichnungen bezeichnungen)
    {
        ArgumentNullException.ThrowIfNull(bezeichnungen);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, entry) in _entries)
        {
            var variant = tonalitaet switch
            {
                Tonalitaet.Sachlich => entry.Sachlich,
                Tonalitaet.Motivierend => entry.Motivierend,
                _ => entry.Freundlich,
            };
            var text = anrede == Anrede.Sie ? variant.Sie : variant.Du;
            result[key] = text
                .Replace("{produktname}", produktname, StringComparison.Ordinal)
                .Replace("{punkte}", bezeichnungen.Punkte, StringComparison.Ordinal)
                .Replace("{serie}", bezeichnungen.Serie, StringComparison.Ordinal);
        }

        return result;
    }

    private static TextCatalog LoadEmbedded()
    {
        using var stream = typeof(TextCatalog).Assembly.GetManifestResourceStream("CompanyHero.Modules.Branding.Resources.textkatalog.json")
            ?? throw new InvalidOperationException("Eingebetteter Textkatalog fehlt.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }
}

public sealed record TextVariant(string Du, string Sie);

public sealed record TextEntry(TextVariant Sachlich, TextVariant Freundlich, TextVariant Motivierend);

/// <summary>Sperrliste der Oberflächenbegriffe (Marke 6.3, A-080); der Operator pflegt sie, sie gilt für Textkatalog und Moderation.</summary>
public static class Sperrliste
{
    public static IReadOnlyList<string> Begriffe { get; } =
    [
        "Gesundheitsdaten", "Tracking", "Monitoring", "Auswertung", "Überwachung", "Krankenstand", "Fehlzeiten", "Leistung", "Ranking",
    ];

    /// <summary>Erster getroffener Sperrbegriff als ganzes Wort (auch als Wortanfang, etwa „Leistungen“), sonst <c>null</c>.</summary>
    public static string? Treffer(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        foreach (var begriff in Begriffe)
        {
            var index = 0;
            while ((index = text.IndexOf(begriff, index, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                var precededByLetter = index > 0 && char.IsLetter(text[index - 1]);
                if (!precededByLetter)
                {
                    return begriff;
                }

                index += begriff.Length;
            }
        }

        return null;
    }
}
