using CompanyHero.Modules.Branding.Domain.Text;
using CompanyHero.Modules.Branding.Domain.Theme;

namespace CompanyHero.Domain.Tests;

/// <summary>Marke 6.2 und 6.3, A-080: drei Tonalitätsvarianten je Schlüssel mit Du und Sie, Sperrliste, Ausrufezeichen, Platzhalter.</summary>
public sealed class TextCatalogTests
{
    [Fact]
    public void Eingebetteter_Katalog_ist_vollstaendig_und_besteht_die_Prueflisten()
    {
        var catalog = TextCatalog.Embedded;
        Assert.Contains("challenge.beitragHeute", catalog.Keys);
        Assert.Contains("kiosk.hinweis", catalog.Keys);
        Assert.Contains("fehler.GracePeriodOver", catalog.Keys);
    }

    [Fact]
    public void Anrede_ist_je_Variante_ausgearbeitet_und_Bezeichnungen_werden_ersetzt()
    {
        var du = TextCatalog.Embedded.Resolve(Tonalitaet.Freundlich, Anrede.Du, "Wiesner Aktiv", Bezeichnungen.Standard);
        var sie = TextCatalog.Embedded.Resolve(Tonalitaet.Freundlich, Anrede.Sie, "Hödl Fit", new Bezeichnungen("Sterne", "Strähne", Bezeichnungen.Standard.Stufen));

        Assert.Equal("Dein Beitrag heute", du["challenge.beitragHeute"]);
        Assert.Equal("Ihr Beitrag heute", sie["challenge.beitragHeute"]);
        Assert.Equal("Notiz (nur für dich)", du["form.notiz"]);
        Assert.Equal("Notiz (nur für Sie)", sie["form.notiz"]);
        Assert.Equal("{tage} Tage Serie", du["kontext.serie"]);
        Assert.Equal("{tage} Tage Strähne", sie["kontext.serie"]);
        Assert.Equal("Über Hödl Fit", sie["ich.ueber"]);
    }

    [Fact]
    public void Tonalitaet_wirkt_auf_Texte_sachlich_ohne_Ausrufezeichen_motivierend_mit_hoechstens_einem()
    {
        var sachlich = TextCatalog.Embedded.Resolve(Tonalitaet.Sachlich, Anrede.Du, "X", Bezeichnungen.Standard);
        var motivierend = TextCatalog.Embedded.Resolve(Tonalitaet.Motivierend, Anrede.Du, "X", Bezeichnungen.Standard);
        Assert.NotEqual(sachlich["challenge.erfolgsmoment"], motivierend["challenge.erfolgsmoment"]);
        Assert.All(sachlich.Values, t => Assert.DoesNotContain('!', t));
        Assert.All(motivierend.Values, t => Assert.True(t.Count(c => c == '!') <= 1, t));
    }

    [Fact]
    public void Sperrbegriff_im_Katalog_wird_abgelehnt()
    {
        var entry = new TextEntry(new("Deine Leistung zählt.", "Ihre Leistung zählt."), new("a", "b"), new("c", "d"));
        var problems = TextCatalog.Validate(new Dictionary<string, TextEntry> { ["x"] = entry });
        Assert.Contains(problems, p => p.Contains("Sperrbegriff „Leistung“", StringComparison.Ordinal));

        Assert.Equal("Ranking", Sperrliste.Treffer("Das Ranking der Woche"));
        Assert.Equal("Leistung", Sperrliste.Treffer("Leistungen anzeigen"));
        Assert.Null(Sperrliste.Treffer("Dienstleistung anzeigen"));
        Assert.Null(Sperrliste.Treffer("Firmenziel erreicht"));
        Assert.Throws<InvalidOperationException>(() => TextCatalog.Parse("""{"x":{"sachlich":{"du":"Tracking","sie":"Tracking"},"freundlich":{"du":"a","sie":"b"},"motivierend":{"du":"c","sie":"d"}}}"""));
    }

    [Fact]
    public void Unvollstaendige_Variante_wird_abgelehnt()
    {
        var problems = TextCatalog.Validate(new Dictionary<string, TextEntry> { ["x"] = new(new("a", ""), new("a", "b"), new("c", "d")) });
        Assert.Contains(problems, p => p.Contains("Sachlich", StringComparison.Ordinal));
    }
}
