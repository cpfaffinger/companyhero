using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CompanyHero.Modules.Identity.Api;
using CompanyHero.Modules.Identity.Domain;
using CompanyHero.Modules.Identity.Infrastructure;
using CompanyHero.Modules.Identity.Infrastructure.Http;
using CompanyHero.Platform.Data;
using CompanyHero.Platform.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace CompanyHero.Integration.Tests;

/// <summary>
/// Zugang 10.3, 10.4, 10.9 (A-007, A-015, A-019): Login mit Microsoft, Google und tenant-eigenem OIDC-Anbieter über die
/// Framework-Middleware (Code-Flow mit PKCE, Nonce, State); im Datenbestand nur der Hash aus Issuer und Subject, kein Name,
/// keine E-Mail; dieselbe Identität keiner zweiten aktiven Person; Austritt entfernt alles; Callback auf zweiter Instanz.
/// </summary>
[Collection(PostgresTests.Name)]
public sealed class OidcLoginTests(PostgresFixture pg) : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ScratchTenant _t = null!;
    private string _joinCode = null!;

    public async ValueTask InitializeAsync()
    {
        _t = await pg.CreateScratchTenantWithMembersAsync($"Anbieter {Guid.NewGuid():N}");
        _joinCode = await AccessJoinTests.CreateJoinCodeAsync(pg, _t, Ct);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    /// <summary>Beitritt über einen Anbieter: Login ohne Zuordnung führt in den Beitritt; der Beitritt übernimmt die geprüfte Identität.</summary>
    public static async Task<(TestBrowser Browser, JoinResponse Join)> JoinViaProviderAsync(PostgresFixture pg, string joinCode, string providerKey, string subject, string displayName, CancellationToken ct)
    {
        var browser = pg.Browser();
        var outcome = await OidcFlow.LoginAsync(pg, browser, providerKey, subject, ct);
        Assert.Equal(HttpStatusCode.Found, outcome.CallbackStatus);
        Assert.Equal("/zugang/beitritt?extern=1", outcome.FinalLocation);
        Assert.Null(browser.Cookies[SessionCookies.Session]);
        Assert.NotNull(browser.Cookies[SessionCookies.ExternalHandoff]);

        var preview = await browser.GetJsonAsync<JoinPreviewResponse>($"/api/join/{joinCode}", ct);
        Assert.Equal(providerKey, preview.PendingExternal?.ProviderKey);
        var join = await browser.PostJsonAsync<JoinResponse>($"/api/join/{joinCode}", new JoinRequest(displayName, VisibilityDto.Company, null, null, true, null, null), ct, HttpStatusCode.Created);
        Assert.NotNull(browser.Cookies[SessionCookies.Session]);
        Assert.Null(browser.Cookies[SessionCookies.ExternalHandoff]);
        Assert.Null(join.RecoveryCode);
        return (browser, join);
    }

    [Fact]
    public async Task Login_mit_Microsoft_Google_und_tenant_eigenem_Anbieter_im_Datenbestand_nur_Hash_aus_Issuer_und_Subject()
    {
        var tenantProvider = await ConfigureTenantProviderAsync();
        var subjects = new Dictionary<string, string>(StringComparer.Ordinal) { ["microsoft"] = "ms-anna-42", ["google"] = "google-bea-7", [tenantProvider] = "wiesner-cem-9" };
        var persons = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (provider, subject) in subjects)
        {
            var (browser, join) = await JoinViaProviderAsync(pg, _joinCode, provider, subject, "Person " + provider, Ct);
            using (browser)
            {
                persons[provider] = join.PersonId;
                using var logout = await browser.PostAsync("/api/auth/logout", null, Ct);
                Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
                Assert.Null(browser.Cookies[SessionCookies.Session]);

                // Erneuter Login: die Zuordnung im Identitätsindex führt direkt in die App.
                var login = await OidcFlow.LoginAsync(pg, browser, provider, subject, Ct, returnUrl: "/t/_/challenges");
                Assert.Equal("/t/_/challenges", login.FinalLocation);
                var me = await browser.GetJsonAsync<JsonElement>("/api/me", Ct);
                Assert.Equal(join.PersonId, me.GetProperty("personId").GetString());
                Assert.Equal("Person " + provider, me.GetProperty("displayName").GetString());
                var session = await browser.GetJsonAsync<SessionResponse>("/api/auth/session", Ct);
                Assert.Equal(SessionKindDto.Member, session.Kind);
            }
        }

        Assert.True(pg.Idp.TokenRequests >= 6);

        // Datenbestand (A-007 Claim-Minimierung): genau der Hash aus Issuer und Subject; nirgends Name oder E-Mail des Anbieters.
        await pg.Api.Services.GetRequiredService<ITenantScopeFactory>().RunAsync(TenantContext.ForTenant(_t.Id), async (sp, ct) =>
        {
            await using var tx = await sp.GetRequiredService<IContextTransaction>().BeginAsync(ct);
            var db = sp.GetRequiredService<IdentityDbContext>();
            foreach (var (provider, subject) in subjects)
            {
                var person = new PersonId(Guid.ParseExact(persons[provider], "D"));
                var login = await db.ExternalLogins.SingleAsync(e => e.PersonId == person, ct);
                var issuer = provider is "microsoft" or "google" ? FakeIdentityProvider.Issuer(provider) : FakeIdentityProvider.Issuer("wiesner");
                Assert.Equal(IdentityIndexEntry.HashExternal(issuer, subject), login.SubjectHash);
                Assert.Equal(provider, login.ProviderKey);
                Assert.DoesNotContain(subject, login.SubjectHash, StringComparison.Ordinal);
                Assert.Equal(0, await db.EmailLogins.CountAsync(e => e.PersonId == person, ct));
                Assert.Null((await db.Persons.SingleAsync(p => p.Id == person, ct)).RealName);
            }
        }, Ct);

        await using var conn = new NpgsqlConnection(pg.SuperuserDatabaseConnectionString);
        await conn.OpenAsync(Ct);
        await using var tables = new NpgsqlCommand("select table_schema, table_name from information_schema.tables where table_schema in ('identity','organisation','privacy','metering') and table_type = 'BASE TABLE'", conn);
        var names = new List<(string Schema, string Table)>();
        await using (var reader = await tables.ExecuteReaderAsync(Ct))
        {
            while (await reader.ReadAsync(Ct))
            {
                names.Add((reader.GetString(0), reader.GetString(1)));
            }
        }

        foreach (var (schema, table) in names)
        {
            foreach (var needle in new[] { "Klara Klarname", "@anbieter.example", "ms-anna-42", "google-bea-7", "wiesner-cem-9" })
            {
                await using var scan = new NpgsqlCommand($"select count(*) from \"{schema}\".\"{table}\" t where t::text ilike @needle", conn);
                scan.Parameters.AddWithValue("needle", "%" + needle + "%");
                Assert.True(0L == (long)(await scan.ExecuteScalarAsync(Ct))!, $"{schema}.{table} enthält „{needle}“");
            }
        }
    }

    [Fact]
    public async Task Dieselbe_externe_Identitaet_kann_keiner_zweiten_aktiven_Person_zugeordnet_werden()
    {
        var (first, join) = await JoinViaProviderAsync(pg, _joinCode, "google", "google-doppelt-1", "Erste", Ct);
        using (first)
        {
            // Bestehendes Mitglied versucht, dieselbe Anbieteridentität zu verknüpfen (Zugang 4 Verknüpfen aus bestehender Sitzung).
            using var second = pg.Browser();
            var session = pg.SessionOf(_t.Id, _t.MemberA);
            second.Cookies.Absorb(new HttpResponseMessage { Headers = { { "Set-Cookie", $"{SessionCookies.Session}={session.Token}" }, { "Set-Cookie", $"{SessionCookies.Csrf}={session.CsrfToken}" } } });
            var link = await OidcFlow.LoginAsync(pg, second, "google", "google-doppelt-1", Ct, intent: "link", returnUrl: "/t/_/ich");
            Assert.Equal("/t/_/ich?anbieter=identitaet_vergeben", link.FinalLocation);

            // Ein zweiter Beitritt mit derselben Identität ist kein Beitritt: der Login führt zur ersten Person.
            using var third = pg.Browser();
            var login = await OidcFlow.LoginAsync(pg, third, "google", "google-doppelt-1", Ct);
            Assert.Equal("/t/_/start", login.FinalLocation);
            var me = await third.GetJsonAsync<JsonElement>("/api/me", Ct);
            Assert.Equal(join.PersonId, me.GetProperty("personId").GetString());

            var count = await pg.Api.Services.GetRequiredService<ITenantScopeFactory>().RunAsync(TenantContext.ForPlatform(), async (sp, ct) =>
            {
                await using var tx = await sp.GetRequiredService<IContextTransaction>().BeginAsync(ct);
                var db = sp.GetRequiredService<IdentityDbContext>();
                var hash = IdentityIndexEntry.HashExternal(FakeIdentityProvider.Issuer("google"), "google-doppelt-1");
                return await db.IdentityIndex.CountAsync(i => i.Hash == hash, ct);
            }, Ct);
            Assert.Equal(1, count);

            // Eine andere Identität lässt sich verknüpfen; danach hat MemberA zwei Wege.
            var ok = await OidcFlow.LoginAsync(pg, second, "microsoft", "ms-verknuepft-5", Ct, intent: "link", returnUrl: "/t/_/ich");
            Assert.Equal("/t/_/ich?anbieter=verknuepft", ok.FinalLocation);
            var overview = await second.GetJsonAsync<AccessOverviewResponse>("/api/me/access", Ct);
            Assert.Single(overview.Providers);
            Assert.Equal("Microsoft", overview.Providers[0].DisplayName);
        }
    }

    [Fact]
    public async Task Austritt_entfernt_alle_Identitaeten_und_erneuter_Login_mit_derselben_Identitaet_fuehrt_in_den_Beitritt()
    {
        var (browser, join) = await JoinViaProviderAsync(pg, _joinCode, "microsoft", "ms-austritt-3", "Austretende", Ct);
        using (browser)
        {
            using var setEmail = await browser.PutAsync("/api/me/email", new EmailRequest("austritt@example.org"), Ct);
            Assert.Equal(HttpStatusCode.NoContent, setEmail.StatusCode);
            using var pin = await browser.PostAsync("/api/kiosk/pin", new PinRequest("7391"), Ct);
            Assert.Equal(HttpStatusCode.NoContent, pin.StatusCode);
            var person = new PersonId(Guid.ParseExact(join.PersonId, "D"));

            using var leave = await browser.PostAsync("/api/me/leave", null, Ct);
            Assert.Equal(HttpStatusCode.NoContent, leave.StatusCode);
            Assert.Null(browser.Cookies[SessionCookies.Session]);

            await pg.Api.Services.GetRequiredService<ITenantScopeFactory>().RunAsync(TenantContext.ForTenant(_t.Id), async (sp, ct) =>
            {
                await using var tx = await sp.GetRequiredService<IContextTransaction>().BeginAsync(ct);
                var db = sp.GetRequiredService<IdentityDbContext>();
                Assert.Equal(0, await db.ExternalLogins.CountAsync(e => e.PersonId == person, ct));
                Assert.Equal(0, await db.EmailLogins.CountAsync(e => e.PersonId == person, ct));
                Assert.Equal(0, await db.Passkeys.CountAsync(p => p.PersonId == person, ct));
                Assert.Equal(0, await db.RecoveryCodes.CountAsync(r => r.PersonId == person, ct));
                Assert.Equal(0, await db.KioskCredentials.CountAsync(k => k.PersonId == person, ct));
                Assert.Equal(0, await db.IdentityIndex.CountAsync(i => i.PersonId == person, ct));
                Assert.Equal(0, await db.Sessions.CountAsync(s => s.PersonId == person && s.RevokedAt == null, ct));
                Assert.Equal(PersonState.Left, (await db.Persons.SingleAsync(p => p.Id == person, ct)).State);
            }, Ct);

            // Erneuter Login mit derselben Identität: keine Zuordnung mehr, der Weg führt in den Beitritt; neuer Beitritt ergibt eine neue Person.
            using var again = pg.Browser();
            var login = await OidcFlow.LoginAsync(pg, again, "microsoft", "ms-austritt-3", Ct);
            Assert.Equal("/zugang/beitritt?extern=1", login.FinalLocation);
            Assert.Null(again.Cookies[SessionCookies.Session]);
            var rejoin = await again.PostJsonAsync<JoinResponse>($"/api/join/{_joinCode}", new JoinRequest("Austretende neu", VisibilityDto.OnlyMe, null, null, true, null, null), Ct, HttpStatusCode.Created);
            Assert.NotEqual(join.PersonId, rejoin.PersonId);

            // Die Mitgliedschaft der alten Person ist beendet; ihre alte E-Mail ist wieder frei.
            using var members = pg.Client(_t.Id, _t.Admin);
            var list = await members.GetFromJsonAsync<JsonElement>("/api/members", Ct);
            Assert.DoesNotContain(list.EnumerateArray(), m => m.GetProperty("personId").GetString() == join.PersonId);
        }
    }

    [Fact]
    public async Task Callback_auf_zweiter_API_Instanz_gelingt_mit_gemeinsamem_Sitzungsspeicher_und_gemeinsamen_Data_Protection_Schluesseln()
    {
        using var instanceB = pg.CreateApi();
        var (setup, join) = await JoinViaProviderAsync(pg, _joinCode, "google", "google-instanz-2", "Wanderin", Ct);
        setup.Dispose();

        using var browserA = pg.Browser();
        using var browserB = pg.Browser(instanceB);
        // Start auf Instanz A (State, Nonce, Korrelation von A geschützt), Callback auf Instanz B.
        var outcome = await OidcFlow.LoginAsync(pg, browserA, "google", "google-instanz-2", Ct, callbackBrowser: browserB);
        Assert.Equal("/t/_/start", outcome.FinalLocation);
        Assert.NotNull(browserB.Cookies[SessionCookies.Session]);

        // Die Sitzung aus B gilt auf A: gemeinsamer Sitzungsspeicher in PostgreSQL.
        var me = await browserB.GetJsonAsync<JsonElement>("/api/me", Ct);
        Assert.Equal(join.PersonId, me.GetProperty("personId").GetString());
        using var onA = pg.Api.CreateClient();
        onA.DefaultRequestHeaders.Add("Cookie", $"{SessionCookies.Session}={browserB.Cookies[SessionCookies.Session]}");
        using var meOnA = await onA.GetAsync("/api/me", Ct);
        Assert.Equal(HttpStatusCode.OK, meOnA.StatusCode);
    }

    [Fact]
    public async Task Fehlerhafte_Callback_Werte_und_fehlerhafte_Anbieter_werden_abgelehnt()
    {
        using var browser = pg.Browser();
        using var start = await browser.GetAsync("/api/auth/oidc/google/start?intent=login", Ct);
        Assert.Equal(HttpStatusCode.Found, start.StatusCode);

        // Manipulierter State: die Middleware weist den Callback ab, es entsteht keine Sitzung.
        using var tampered = await browser.GetAsync("/api/auth/oidc/google/callback?code=egal&state=manipuliert", Ct);
        Assert.Equal(HttpStatusCode.Found, tampered.StatusCode);
        Assert.Equal("/zugang?fehler=anbieter", tampered.Headers.Location!.ToString());
        Assert.Null(browser.Cookies[SessionCookies.Session]);

        // Unbekannter Anbieter.
        using var unknown = await browser.GetAsync("/api/auth/oidc/unbekannt/start", Ct);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        // Discovery-Validierung eines fehlerhaften Anbieters schlägt fehl (A-015): Issuer stimmt nicht, kein S256.
        using var admin = pg.Client(_t.Id, _t.Admin);
        using var broken = await admin.PostAsJsonAsync("/api/access/providers", new ConfigureProviderRequest("Kaputt", FakeIdentityProvider.Issuer("broken"), "client", "secret"), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, broken.StatusCode);
        var problem = await broken.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var reasons = problem.GetProperty("errors").GetProperty("issuer").EnumerateArray().Select(r => r.GetString()).ToList();
        Assert.Contains("issuer_mismatch", reasons);
        Assert.Contains("pkce_s256_unsupported", reasons);
        using var unreachable = await admin.PostAsJsonAsync("/api/access/providers", new ConfigureProviderRequest("Weg", "https://nirgendwo.idp.test", "client", "secret"), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, unreachable.StatusCode);
        using var plain = await admin.PostAsJsonAsync("/api/access/providers", new ConfigureProviderRequest("Unsicher", "http://idp.test/plain", "client", "secret"), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, plain.StatusCode);
    }

    private async Task<string> ConfigureTenantProviderAsync()
    {
        using var admin = pg.Client(_t.Id, _t.Admin);
        using var response = await admin.PostAsJsonAsync("/api/access/providers", new ConfigureProviderRequest("Wiesner Konto", FakeIdentityProvider.Issuer("wiesner"), "wiesner-client", "wiesner-secret"), Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var provider = (await response.Content.ReadFromJsonAsync<TenantProviderResponse>(Ct))!;
        Assert.NotNull(provider.ValidatedAt);

        // Die Anmeldeseite des Tenants kennt drei Anbieter; die Plattformseite nur zwei.
        using var anonymous = pg.Browser();
        var forTenant = await anonymous.GetJsonAsync<List<ProviderResponse>>($"/api/auth/providers?tenant={_t.Id}", Ct);
        Assert.Equal(new[] { "google", "microsoft", provider.Key }.Order(StringComparer.Ordinal), forTenant.Select(p => p.Key).Order(StringComparer.Ordinal));
        var platform = await anonymous.GetJsonAsync<List<ProviderResponse>>("/api/auth/providers", Ct);
        Assert.Equal(["google", "microsoft"], platform.Select(p => p.Key).ToList());

        // Im Datenbestand liegt das Client-Secret nicht im Klartext.
        await using var conn = new NpgsqlConnection(pg.SuperuserDatabaseConnectionString);
        await conn.OpenAsync(Ct);
        await using var scan = new NpgsqlCommand("select count(*) from identity.external_provider where client_secret_protected like '%wiesner-secret%'", conn);
        Assert.Equal(0L, (long)(await scan.ExecuteScalarAsync(Ct))!);
        return provider.Key;
    }
}
