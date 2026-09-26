using System.Net;
using System.Web;

namespace CompanyHero.Integration.Tests;

/// <summary>
/// Der Anbieter-Login aus Sicht des Browsers: Start an der API (Weiterleitung zum Anbieter), Anmeldung am In-Process-Anbieter
/// (Weiterleitung mit Code zurück), Callback an der API (Weiterleitung in die App mit Sitzung oder in den Beitritt).
/// Der Callback kann auf einer anderen API-Instanz landen als der Start (A-007 Betrieb).
/// </summary>
public static class OidcFlow
{
    public sealed record Outcome(string FinalLocation, HttpStatusCode CallbackStatus);

    public static async Task<Outcome> LoginAsync(PostgresFixture pg, TestBrowser browser, string providerKey, string subject, CancellationToken ct, string intent = "login", string? returnUrl = null, TestBrowser? callbackBrowser = null)
    {
        var query = $"?intent={intent}" + (returnUrl is null ? string.Empty : "&returnUrl=" + Uri.EscapeDataString(returnUrl));
        using var start = await browser.GetAsync($"/api/auth/oidc/{providerKey}/start{query}", ct);
        Assert.True(start.StatusCode == HttpStatusCode.Found, $"Start: {start.StatusCode} {await start.Content.ReadAsStringAsync(ct)}");
        var authorize = start.Headers.Location!;
        Assert.StartsWith(FakeIdentityProvider.Issuer(providerKey == "microsoft" || providerKey == "google" ? providerKey : IssuerNameOf(authorize)), authorize.ToString(), StringComparison.Ordinal);
        var parameters = HttpUtility.ParseQueryString(authorize.Query);
        Assert.Equal("code", parameters["response_type"]);
        Assert.Equal("S256", parameters["code_challenge_method"]);
        Assert.Equal("openid", parameters["scope"]);

        // Anmeldung am Anbieter: der Test benennt das Subjekt; Name und E-Mail liefert der Anbieter im Token.
        using var idp = new HttpClient(pg.Idp.Handler, disposeHandler: false);
        using var consent = await idp.GetAsync(new Uri(authorize + "&sub=" + Uri.EscapeDataString(subject)), ct);
        Assert.Equal(HttpStatusCode.Found, consent.StatusCode);
        var callback = consent.Headers.Location!;
        Assert.StartsWith(PostgresFixture.Origin + $"/api/auth/oidc/{providerKey}/callback", callback.ToString(), StringComparison.Ordinal);

        var target = callbackBrowser ?? browser;
        if (callbackBrowser is not null)
        {
            // Instanzwechsel: die Korrelations- und Nonce-Cookies des Starts wandern mit dem Browser.
            foreach (var cookie in browser.Cookies.All)
            {
                callbackBrowser.Cookies.Absorb(new HttpResponseMessage { Headers = { { "Set-Cookie", $"{cookie.Key}={cookie.Value}" } } });
            }
        }

        using var response = await target.GetAsync(callback.PathAndQuery, ct);
        var location = response.Headers.Location?.ToString() ?? string.Empty;
        return new Outcome(location, response.StatusCode);
    }

    private static string IssuerNameOf(Uri authorize) => authorize.AbsolutePath.Trim('/').Split('/')[0];
}
