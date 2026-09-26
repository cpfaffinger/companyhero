using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CompanyHero.Modules.Branding.Api;
using CompanyHero.Modules.Branding.Application;
using CompanyHero.Modules.Branding.Domain.Color;
using CompanyHero.Modules.Branding.Domain.Theme;
using CompanyHero.Platform.Tenancy;

namespace CompanyHero.Integration.Tests;

/// <summary>
/// Marke 3.3 bis 3.5, 5, 9.1 bis 9.4; A-013, A-077, A-079, A-080; Datenschutz 7: versionierter Tokensatz je Tenant und Modus über
/// den Endpunkt der Marke, Vorschau ohne Persistierung mit identischen Werten, Standard-Theme bei ungültiger Eingabe mit Grund,
/// Texte in Tonalität und Anrede, Manifest und Icons je Tenant nur für die eigene Sitzung, Plattformtheme ohne Sitzung.
/// </summary>
[Collection(PostgresTests.Name)]
public sealed class BrandingThemeTests(PostgresFixture pg) : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ScratchTenant _w = null!;
    private ScratchTenant _h = null!;

    public async ValueTask InitializeAsync()
    {
        _w = await pg.CreateScratchTenantWithMembersAsync("Wiesner Marke");
        _h = await pg.CreateScratchTenantWithMembersAsync("Hödl Marke");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public static ThemeDocumentRequest Wiesner { get; } = new(1, "Wiesner Aktiv", "#9B1B3A", null, AnredeDto.Du, TonalitaetDto.Freundlich, new BezeichnungenDto("Punkte", "Serie", ["Neu dabei", "Dabei", "Dranbleiber", "Vorbild", "Urgestein"]), "Willkommen im Wiesner-Programm.");

    public static ThemeDocumentRequest Hoedl { get; } = new(1, "Hödl Fit", "#0F7A45", null, AnredeDto.Sie, TonalitaetDto.Sachlich, new BezeichnungenDto("Punkte", "Serie", ["Neu dabei", "Dabei", "Dranbleiber", "Vorbild", "Urgestein"]), null);

    [Fact]
    public async Task Ohne_Veroeffentlichung_gilt_das_Standard_Theme_mit_dem_Anzeigenamen_des_Tenants()
    {
        using var client = Client(_w.Id, _w.MemberA);
        var theme = await client.GetFromJsonAsync<ThemeResponse>("/api/branding/theme", Ct);

        Assert.NotNull(theme);
        Assert.Equal(0, theme.Version);
        Assert.Equal("Wiesner Marke", theme.Produktname);
        Assert.Equal("#2A45C9", theme.Hell["primary"]);
        Assert.Equal(AnredeDto.Du, theme.Anrede);
        Assert.Equal(ThemeDerivation.Verfahren, theme.Verfahren);
        Assert.All(ColorRoles.Required, role => Assert.True(ColorUtils.TryParseHex(theme.Hell[role], out _) && ColorUtils.TryParseHex(theme.Dunkel[role], out _), role));
        Assert.Equal("Dein Beitrag heute", theme.Texte["challenge.beitragHeute"]);
        Assert.Equal($"/api/branding/tenants/{_w.Id}/manifest.webmanifest", theme.ManifestPfad);
    }

    [Fact]
    public async Task Tenant_Admin_veroeffentlicht_Theme_Saatfarbe_wird_angenommen_Version_und_Texte_folgen()
    {
        using var admin = Client(_w.Id, _w.Admin);
        using var member = Client(_w.Id, _w.MemberA);

        using var forbidden = await member.PutAsJsonAsync("/api/branding/theme", Wiesner, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        using var published = await admin.PutAsJsonAsync("/api/branding/theme", Wiesner, Ct);
        Assert.Equal(HttpStatusCode.OK, published.StatusCode);
        var theme = (await published.Content.ReadFromJsonAsync<ThemeResponse>(Ct))!;
        Assert.Equal(1, theme.Version);
        Assert.Equal("#9B1B3A", theme.Hell["primary"]);
        Assert.Equal("Wiesner Aktiv", theme.Produktname);
        Assert.NotNull(theme.VeroeffentlichtAm);
        Assert.DoesNotContain(theme.Ersetzungen, e => e.Rolle == "primary");
        Assert.Contains(theme.Ersetzungen, e => e.Rolle == "progress" && e.Grund.Contains("Kontrast", StringComparison.Ordinal));

        // Mitglieder sehen die neue Version; Hödl bleibt unberührt (Isolation).
        var seen = (await member.GetFromJsonAsync<ThemeResponse>("/api/branding/theme", Ct))!;
        Assert.Equal(1, seen.Version);
        Assert.Equal("#9B1B3A", seen.Hell["primary"]);
        using var hoedl = Client(_h.Id, _h.MemberA);
        var other = (await hoedl.GetFromJsonAsync<ThemeResponse>("/api/branding/theme", Ct))!;
        Assert.Equal(0, other.Version);
        Assert.Equal("Hödl Marke", other.Produktname);

        // Zweite Version mit Sie und sachlich: Texte wechseln sofort, frühere Version bleibt abrufbar (Marke 3.4).
        using var second = await admin.PutAsJsonAsync("/api/branding/theme", Wiesner with { Anrede = AnredeDto.Sie, Tonalitaet = TonalitaetDto.Sachlich }, Ct);
        var v2 = (await second.Content.ReadFromJsonAsync<ThemeResponse>(Ct))!;
        Assert.Equal(2, v2.Version);
        Assert.Equal("Ihr Beitrag heute", v2.Texte["challenge.beitragHeute"]);
        Assert.All(v2.Texte.Values, t => Assert.DoesNotContain('!', t));
        var v1 = await member.GetFromJsonAsync<ThemeResponse>("/api/branding/theme/versions/1", Ct);
        Assert.Equal("Dein Beitrag heute", v1!.Texte["challenge.beitragHeute"]);
        using var missing = await member.GetAsync("/api/branding/theme/versions/9", Ct);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Ungueltiges_Theme_wird_mit_Grund_abgelehnt_und_das_gueltige_Theme_bleibt()
    {
        using var admin = Client(_h.Id, _h.Admin);
        using var ok = await admin.PutAsJsonAsync("/api/branding/theme", Hoedl, Ct);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        using var invalid = await admin.PutAsJsonAsync("/api/branding/theme", Hoedl with { Saatfarbe = "grün", Produktname = "" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var problem = await invalid.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var reasons = problem.GetProperty("errors").GetProperty("theme").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains(reasons, r => r!.Contains("Saatfarbe", StringComparison.Ordinal));
        Assert.Contains(reasons, r => r!.Contains("Produktname", StringComparison.Ordinal));

        var current = await admin.GetFromJsonAsync<ThemeResponse>("/api/branding/theme", Ct);
        Assert.Equal(1, current!.Version);
        Assert.Equal("#0F7A45", current.Hell["primary"]);
        Assert.Equal(AnredeDto.Sie, current.Anrede);
    }

    [Fact]
    public async Task Vorschau_liefert_dieselben_Werte_wie_die_Veroeffentlichung_ohne_zu_persistieren()
    {
        using var admin = Client(_w.Id, _w.Admin);
        var orange = Wiesner with { Saatfarbe = "#E4670A", Produktname = "Orange" };

        using var previewResponse = await admin.PostAsJsonAsync("/api/branding/theme/preview", orange, Ct);
        Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
        var preview = (await previewResponse.Content.ReadFromJsonAsync<ThemePreviewResponse>(Ct))!;
        Assert.NotEqual("#E4670A", preview.Hell["primary"]);
        Assert.Contains(preview.Ersetzungen, e => e.Rolle == "primary" && e.Angefordert == "#E4670A");
        Assert.All(preview.Kontrast, k => Assert.True(decimal.Parse(k.Erreicht, System.Globalization.CultureInfo.InvariantCulture) >= decimal.Parse(k.Gefordert, System.Globalization.CultureInfo.InvariantCulture), $"{k.Vordergrund} auf {k.Hintergrund}"));

        var before = await admin.GetFromJsonAsync<ThemeResponse>("/api/branding/theme", Ct);
        using var published = await admin.PutAsJsonAsync("/api/branding/theme", orange, Ct);
        var theme = (await published.Content.ReadFromJsonAsync<ThemeResponse>(Ct))!;
        Assert.Equal(before!.Version + 1, theme.Version);
        Assert.Equal(preview.Hell, theme.Hell);
        Assert.Equal(preview.Dunkel, theme.Dunkel);

        using var member = Client(_w.Id, _w.MemberA);
        using var previewForbidden = await member.PostAsJsonAsync("/api/branding/theme/preview", orange, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, previewForbidden.StatusCode);
    }

    [Fact]
    public async Task Manifest_und_Icons_je_Tenant_nur_fuer_die_eigene_Sitzung()
    {
        using var admin = Client(_h.Id, _h.Admin);
        using var published = await admin.PutAsJsonAsync("/api/branding/theme", Hoedl, Ct);
        Assert.Equal(HttpStatusCode.OK, published.StatusCode);
        using var member = Client(_h.Id, _h.MemberA);

        using var manifestResponse = await member.GetAsync($"/api/branding/tenants/{_h.Id}/manifest.webmanifest", Ct);
        Assert.Equal(HttpStatusCode.OK, manifestResponse.StatusCode);
        Assert.Equal("application/manifest+json", manifestResponse.Content.Headers.ContentType?.MediaType);
        var manifest = await manifestResponse.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("Hödl Fit", manifest.GetProperty("name").GetString());
        Assert.Equal($"/t/{_h.Id}/", manifest.GetProperty("id").GetString());
        Assert.Equal($"/t/{_h.Id}/start", manifest.GetProperty("start_url").GetString());
        Assert.Equal($"/t/{_h.Id}/", manifest.GetProperty("scope").GetString());
        Assert.Equal("#0F7A45", manifest.GetProperty("theme_color").GetString());
        Assert.Equal("standalone", manifest.GetProperty("display").GetString());
        var icons = manifest.GetProperty("icons").EnumerateArray().ToList();
        Assert.Contains(icons, i => i.GetProperty("purpose").GetString() == "maskable");
        Assert.Contains(icons, i => i.GetProperty("sizes").GetString() == "192x192");

        foreach (var icon in icons)
        {
            using var iconResponse = await member.GetAsync(icon.GetProperty("src").GetString(), Ct);
            Assert.Equal(HttpStatusCode.OK, iconResponse.StatusCode);
            var bytes = await iconResponse.Content.ReadAsByteArrayAsync(Ct);
            if (icon.GetProperty("type").GetString() == "image/png")
            {
                Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, bytes[..4]);
                var size = int.Parse(icon.GetProperty("sizes").GetString()!.Split('x')[0], System.Globalization.CultureInfo.InvariantCulture);
                Assert.Equal(size, (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19]);
                Assert.Equal(size, (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23]);
            }
            else
            {
                Assert.Contains("<svg", System.Text.Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
            }
        }

        // Kiosk-Manifest im Vollbild mit Kiosk-Einstieg.
        var kiosk = await member.GetFromJsonAsync<JsonElement>("/api/branding/kiosk/manifest.webmanifest", Ct);
        Assert.Equal("fullscreen", kiosk.GetProperty("display").GetString());
        Assert.Equal("/kiosk", kiosk.GetProperty("start_url").GetString());

        // Fremder Tenant: nicht gefunden, auch für den Tenant-Admin des anderen Tenants (Datenschutz 7, A-026).
        using var foreignAdmin = Client(_w.Id, _w.Admin);
        using var foreignManifest = await foreignAdmin.GetAsync($"/api/branding/tenants/{_h.Id}/manifest.webmanifest", Ct);
        Assert.Equal(HttpStatusCode.NotFound, foreignManifest.StatusCode);
        using var foreignIcon = await foreignAdmin.GetAsync($"/api/branding/tenants/{_h.Id}/icons/icon-192.png", Ct);
        Assert.Equal(HttpStatusCode.NotFound, foreignIcon.StatusCode);
        using var unknownIcon = await member.GetAsync($"/api/branding/tenants/{_h.Id}/icons/logo.png", Ct);
        Assert.Equal(HttpStatusCode.NotFound, unknownIcon.StatusCode);

        // Ohne Sitzung: kein Theme, kein Manifest; das Plattformtheme ist öffentlich (Anmeldeseite vor Zuordnung).
        using var anonymous = pg.Api.CreateClient();
        using var noTheme = await anonymous.GetAsync("/api/branding/theme", Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, noTheme.StatusCode);
        using var noManifest = await anonymous.GetAsync($"/api/branding/tenants/{_h.Id}/manifest.webmanifest", Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, noManifest.StatusCode);
        var platform = await anonymous.GetFromJsonAsync<PlatformThemeResponse>("/api/branding/platform", Ct);
        Assert.Equal("CompanyHero", platform!.Plattformname);
        Assert.Equal("#2A45C9", platform.Hell["primary"]);
        Assert.Equal("#E4670A", PlatformBrand.Default.Fortschritt);
    }

    [Fact]
    public async Task Theme_Endpunkt_und_Ableitung_liefern_identische_Werte_wie_die_Fachregel()
    {
        using var admin = Client(_w.Id, _w.Admin);
        using var published = await admin.PutAsJsonAsync("/api/branding/theme", Wiesner, Ct);
        var theme = (await published.Content.ReadFromJsonAsync<ThemeResponse>(Ct))!;

        var expected = ThemeDerivation.Derive(new ThemeDocument(1, "Wiesner Aktiv", "#9B1B3A", null, Anrede.Du, Tonalitaet.Freundlich, Bezeichnungen.Standard, "Willkommen im Wiesner-Programm."), PlatformBrand.Default);
        Assert.Equal(expected.Hell.Werte, theme.Hell);
        Assert.Equal(expected.Dunkel.Werte, theme.Dunkel);
    }

    private HttpClient Client(TenantId tenant, PersonId person)
    {
        var client = pg.Api.CreateClient();
        client.DefaultRequestHeaders.Add(TestSessionHandler.Header, TestSession.For(tenant, person));
        return client;
    }
}
