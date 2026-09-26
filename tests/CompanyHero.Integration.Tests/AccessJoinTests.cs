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

namespace CompanyHero.Integration.Tests;

/// <summary>
/// Zugang 10.1 (A-014, A-016): Beitritt ohne E-Mail mit Passkey und Wiederherstellungscode; Login auf zweitem Gerät mit dem
/// Code; neuer Code wird erzeugt. Ergänzend: Passkey-Login, Klonschutz, keine Verknüpfung über gleiche E-Mail, letzter Weg bleibt.
/// </summary>
[Collection(PostgresTests.Name)]
public sealed class AccessJoinTests(PostgresFixture pg) : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ScratchTenant _t = null!;
    private string _joinCode = null!;

    public async ValueTask InitializeAsync()
    {
        _t = await pg.CreateScratchTenantWithMembersAsync($"Beitritt {Guid.NewGuid():N}");
        _joinCode = await CreateJoinCodeAsync(pg, _t, Ct);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    /// <summary>Tenant-Admin erstellt einen Beitrittscode (Zugang 2.1); die Antwort trägt Code und QR-URL.</summary>
    public static async Task<string> CreateJoinCodeAsync(PostgresFixture pg, ScratchTenant tenant, CancellationToken ct)
    {
        using var admin = pg.Client(tenant.Id, tenant.Admin);
        using var response = await admin.PostAsJsonAsync("/api/access/join-codes", new CreateJoinCodeRequest(null, null), ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var code = (await response.Content.ReadFromJsonAsync<JoinCodeResponse>(ct))!;
        Assert.Equal(8, code.Code.Length);
        Assert.Equal(PostgresFixture.Origin + "/join/" + code.Code, code.JoinUrl);
        return code.Code;
    }

    /// <summary>Beitritt auf dem eigenen Gerät mit Passkey; liefert Browser mit Sitzung, Antwort und Authenticator.</summary>
    public static async Task<(TestBrowser Browser, JoinResponse Join, SoftwareAuthenticator Authenticator)> JoinWithPasskeyAsync(PostgresFixture pg, string code, string displayName, CancellationToken ct, string? email = null)
    {
        var browser = pg.Browser();
        var authenticator = new SoftwareAuthenticator();
        var preview = await browser.GetJsonAsync<JoinPreviewResponse>($"/api/join/{code}", ct);
        Assert.True(preview.Ways.Passkey);
        var ceremony = await browser.PostJsonAsync<PasskeyCeremonyResponse>($"/api/join/{code}/passkey-options", new JoinPasskeyOptionsRequest(displayName), ct);
        Assert.Equal("localhost", ceremony.Options.GetProperty("rp").GetProperty("id").GetString());
        var credential = authenticator.CreateAttestation(ceremony.Options);
        var join = await browser.PostJsonAsync<JoinResponse>($"/api/join/{code}", new JoinRequest(displayName, VisibilityDto.Company, new PasskeyAnswerRequest(ceremony.State, credential, "Testgerät"), email, false, null, null), ct, HttpStatusCode.Created);
        Assert.NotNull(browser.Cookies[SessionCookies.Session]);
        Assert.NotNull(browser.Cookies[SessionCookies.Csrf]);
        return (browser, join, authenticator);
    }

    public static async Task<LoginResponse> LoginWithPasskeyAsync(TestBrowser browser, SoftwareAuthenticator authenticator, CancellationToken ct)
    {
        var ceremony = await browser.PostJsonAsync<PasskeyCeremonyResponse>("/api/auth/passkey/options", null, ct);
        var assertion = authenticator.CreateAssertion(ceremony.Options);
        return await browser.PostJsonAsync<LoginResponse>("/api/auth/passkey", new PasskeyAnswerRequest(ceremony.State, assertion, null), ct);
    }

    [Fact]
    public async Task Beitritt_ohne_E_Mail_mit_Passkey_und_Wiederherstellungscode_Login_auf_zweitem_Geraet_mit_dem_Code_neuer_Code_wird_erzeugt()
    {
        var (device1, join, authenticator) = await JoinWithPasskeyAsync(pg, _joinCode, "Sonnenblume", Ct);
        using (device1)
        using (authenticator)
        {
            Assert.Equal(_t.Id.ToString(), join.TenantId);
            Assert.Matches("^[0-9]{6}$", join.KioskId);
            Assert.NotNull(join.RecoveryCode);
            Assert.Matches("^[A-Z2-9]{3}-[A-Z2-9]{3}-[A-Z2-9]{3}-[A-Z2-9]{3}$", join.RecoveryCode);
            Assert.Equal(SessionKindDto.Member, join.SessionKind);

            var me = await device1.GetJsonAsync<JsonElement>("/api/me", Ct);
            Assert.Equal("Sonnenblume", me.GetProperty("displayName").GetString());
            Assert.Equal(["member"], me.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).ToList());

            // Datenbestand: Passkey und Hash des Codes, keine E-Mail, kein Klarname, kein Klartextcode (Zugang 3.1, 4).
            var person = new PersonId(Guid.ParseExact(join.PersonId, "D"));
            await pg.Api.Services.GetRequiredService<ITenantScopeFactory>().RunAsync(TenantContext.ForTenant(_t.Id), async (sp, ct) =>
            {
                await using var tx = await sp.GetRequiredService<IContextTransaction>().BeginAsync(ct);
                var db = sp.GetRequiredService<IdentityDbContext>();
                Assert.Equal(1, await db.Passkeys.CountAsync(p => p.PersonId == person, ct));
                Assert.Equal(0, await db.EmailLogins.CountAsync(e => e.PersonId == person, ct));
                Assert.Equal(0, await db.IdentityIndex.CountAsync(i => i.PersonId == person, ct));
                var recovery = await db.RecoveryCodes.SingleAsync(r => r.PersonId == person, ct);
                Assert.DoesNotContain(AccessCodes.Normalize(join.RecoveryCode!), recovery.CodeHash, StringComparison.Ordinal);
                Assert.Null((await db.Persons.SingleAsync(p => p.Id == person, ct)).RealName);
                var access = await db.KioskCredentials.SingleAsync(k => k.PersonId == person, ct);
                Assert.Equal(join.KioskId, access.KioskId);
                Assert.Null(access.PinHash);
            }, Ct);

            // Zweites Gerät: Login mit dem Wiederherstellungscode, sofort neuer Code, Einrichtung eines Weges verlangt (Zugang 3.1).
            using var device2 = pg.Browser();
            var recovered = await device2.PostJsonAsync<LoginResponse>("/api/auth/recovery", new RecoveryLoginRequest(join.RecoveryCode!.ToLowerInvariant()), Ct);
            Assert.Equal(join.PersonId, recovered.PersonId);
            Assert.True(recovered.MustSetUpAccess);
            Assert.NotNull(recovered.NewRecoveryCode);
            Assert.NotEqual(join.RecoveryCode, recovered.NewRecoveryCode);
            var me2 = await device2.GetJsonAsync<JsonElement>("/api/me", Ct);
            Assert.Equal("Sonnenblume", me2.GetProperty("displayName").GetString());

            // Der verbrauchte Code ist ungültig, der neue gilt genau einmal.
            using var device3 = pg.Browser();
            using var reused = await device3.PostAsync("/api/auth/recovery", new RecoveryLoginRequest(join.RecoveryCode), Ct);
            Assert.Equal(HttpStatusCode.Unauthorized, reused.StatusCode);
            var again = await device3.PostJsonAsync<LoginResponse>("/api/auth/recovery", new RecoveryLoginRequest(recovered.NewRecoveryCode!), Ct);
            Assert.NotEqual(recovered.NewRecoveryCode, again.NewRecoveryCode);

            // Passkey-Login auf dem zweiten Gerät mit demselben Authenticator; Zähler wird geführt.
            using var device4 = pg.Browser();
            var passkeyLogin = await LoginWithPasskeyAsync(device4, authenticator, Ct);
            Assert.Equal(join.PersonId, passkeyLogin.PersonId);
            Assert.False(passkeyLogin.MustSetUpAccess);
            var overview = await device4.GetJsonAsync<AccessOverviewResponse>("/api/me/access", Ct);
            Assert.Single(overview.Passkeys);
            Assert.NotNull(overview.Passkeys[0].LastUsedAt);
            Assert.Null(overview.Email);
            Assert.True(overview.RecoveryCodeActive);
            Assert.Equal(join.KioskId, overview.KioskId);
            Assert.False(overview.KioskPinSet);
        }
    }

    [Fact]
    public async Task Passkey_Antworten_mit_fremder_Origin_falscher_Challenge_oder_wiederholtem_Zaehler_werden_abgelehnt()
    {
        var (browser, join, authenticator) = await JoinWithPasskeyAsync(pg, _joinCode, "Kornblume", Ct);
        using (browser)
        using (authenticator)
        {
            using var device2 = pg.Browser();
            // Fremde Origin (Phishing-Seite): der Authenticator signiert für eine andere Origin.
            using var evil = new SoftwareAuthenticator { Origin = "https://evil.example" };
            var ceremony = await device2.PostJsonAsync<PasskeyCeremonyResponse>("/api/auth/passkey/options", null, Ct);
            using var wrongOrigin = await device2.PostAsync("/api/auth/passkey", new PasskeyAnswerRequest(ceremony.State, evil.CreateAssertion(ceremony.Options), null), Ct);
            Assert.Equal(HttpStatusCode.Unauthorized, wrongOrigin.StatusCode); // unbekannte Credential-ID
            Assert.Null(device2.Cookies[SessionCookies.Session]);

            // Bekannter Passkey, aber Antwort auf eine andere Challenge (Zustand einer anderen Zeremonie).
            var other = await device2.PostJsonAsync<PasskeyCeremonyResponse>("/api/auth/passkey/options", null, Ct);
            using var wrongChallenge = await device2.PostAsync("/api/auth/passkey", new PasskeyAnswerRequest(other.State, authenticator.CreateAssertion(ceremony.Options), null), Ct);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, wrongChallenge.StatusCode);
            Assert.Null(device2.Cookies[SessionCookies.Session]);

            // Wiederholte Assertion (gleicher Zähler): Klonschutz nach WebAuthn.
            var third = await device2.PostJsonAsync<PasskeyCeremonyResponse>("/api/auth/passkey/options", null, Ct);
            var assertion = authenticator.CreateAssertion(third.Options);
            await device2.PostJsonAsync<LoginResponse>("/api/auth/passkey", new PasskeyAnswerRequest(third.State, assertion, null), Ct);
            using var device3 = pg.Browser();
            using var replay = await device3.PostAsync("/api/auth/passkey", new PasskeyAnswerRequest(third.State, assertion, null), Ct);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, replay.StatusCode);
            Assert.Null(device3.Cookies[SessionCookies.Session]);
            _ = join;
        }
    }

    [Fact]
    public async Task Keine_Verknuepfung_ueber_gleiche_E_Mail_und_der_letzte_Weg_bleibt()
    {
        var (a, joinA, authA) = await JoinWithPasskeyAsync(pg, _joinCode, "Ahorn", Ct, email: "ahorn@example.org");
        var (b, joinB, authB) = await JoinWithPasskeyAsync(pg, _joinCode, "Birke", Ct);
        using (a)
        using (b)
        using (authA)
        using (authB)
        {
            Assert.Null(joinA.RecoveryCode); // mit E-Mail kein Wiederherstellungscode nötig (Zugang 2.2)
            Assert.NotNull(joinB.RecoveryCode);

            // Dieselbe Adresse gehört höchstens einer aktiven Person; keine automatische Verknüpfung (A-016).
            using var taken = await b.PutAsync("/api/me/email", new EmailRequest("Ahorn@Example.org"), Ct);
            Assert.Equal(HttpStatusCode.Conflict, taken.StatusCode);

            // Birke besitzt nur den Passkey: Trennen abgelehnt, bis ein zweiter Weg besteht.
            var overview = await b.GetJsonAsync<AccessOverviewResponse>("/api/me/access", Ct);
            using var lastWay = await b.DeleteAsync($"/api/me/passkeys/{overview.Passkeys[0].PasskeyId}", Ct);
            Assert.Equal(HttpStatusCode.Conflict, lastWay.StatusCode);

            using var setEmail = await b.PutAsync("/api/me/email", new EmailRequest("birke@example.org"), Ct);
            Assert.Equal(HttpStatusCode.NoContent, setEmail.StatusCode);
            using var removed = await b.DeleteAsync($"/api/me/passkeys/{overview.Passkeys[0].PasskeyId}", Ct);
            Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
            var after = await b.GetJsonAsync<AccessOverviewResponse>("/api/me/access", Ct);
            Assert.Empty(after.Passkeys);
            Assert.Equal("birke@example.org", after.Email);

            // Ungültiger oder verbrauchter Beitrittscode gibt keine Auskunft (Zugang 2.1).
            using var unknown = pg.Browser();
            using var missing = await unknown.GetAsync("/api/join/ZZZZZZZZ", Ct);
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }
    }
}
