using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CompanyHero.Modules.Challenges.Api;
using CompanyHero.Modules.Challenges.Application;
using CompanyHero.Modules.Challenges.Domain;
using CompanyHero.Modules.Identity.Api;
using CompanyHero.Modules.Identity.Infrastructure.Http;
using CompanyHero.Modules.Organisation.Domain;
using CompanyHero.Platform.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyHero.Integration.Tests;

/// <summary>
/// Zugang 10.7 und 10.8 (A-007, A-017, A-019): Verlängerung, absolutes Ende, „Abmeldung aus allen Sitzungen“ über zwei
/// API-Instanzen mit gemeinsamem Sitzungsspeicher; CSRF-Schutz zustandsändernder Aufrufe; wartende Beiträge nach erneuter
/// Anmeldung synchronisiert, nach Austritt nicht.
/// </summary>
[Collection(PostgresTests.Name)]
public sealed class SessionTests(PostgresFixture pg) : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ScratchTenant _t = null!;

    public async ValueTask InitializeAsync() => _t = await pg.CreateScratchTenantWithMembersAsync($"Sitzung {Guid.NewGuid():N}");

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Verlaengerung_absolutes_Ende_und_Abmeldung_aus_allen_Sitzungen_ueber_zwei_API_Instanzen()
    {
        var start = pg.Clock.GetUtcNow();
        using var instanceB = pg.CreateApi();
        try
        {
            var member = await pg.IssueSessionAsync(_t.Id, _t.MemberA, Ct);
            using var onA = pg.ClientWithSession(member);
            using var onB = pg.ClientWithSession(member, instanceB);

            var info = await onA.GetFromJsonAsync<SessionResponse>("/api/auth/session", Ct);
            Assert.Equal(SessionKindDto.Member, info!.Kind);
            Assert.Equal(start.AddDays(30), info.SlidingUntil);
            Assert.Equal(start.AddDays(180), info.AbsoluteUntil);

            // Aktivität verlängert gleitend, auch auf der anderen Instanz; die Verlängerung ist auf beiden sichtbar.
            pg.Clock.Advance(TimeSpan.FromDays(29));
            var extended = await onB.GetFromJsonAsync<SessionResponse>("/api/auth/session", Ct);
            Assert.Equal(start.AddDays(59), extended!.SlidingUntil);
            pg.Clock.Advance(TimeSpan.FromDays(29));
            var extendedAgain = await onA.GetFromJsonAsync<SessionResponse>("/api/auth/session", Ct);
            Assert.Equal(start.AddDays(88), extendedAgain!.SlidingUntil);

            // Ohne Aktivität endet die Sitzung nach 30 Tagen.
            pg.Clock.Advance(TimeSpan.FromDays(31));
            using var expired = await onB.GetAsync("/api/auth/session", Ct);
            Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);

            // Absolutes Ende nach 180 Tagen trotz Aktivität: die Verlängerung endet am absoluten Ende.
            pg.Clock.Set(start);
            var active = await pg.IssueSessionAsync(_t.Id, _t.MemberB, Ct);
            using var activeA = pg.ClientWithSession(active);
            using var activeB = pg.ClientWithSession(active, instanceB);
            for (var day = 20; day < 180; day += 20)
            {
                pg.Clock.Set(start.AddDays(day));
                using var tick = await (day % 40 == 0 ? activeA : activeB).GetAsync("/api/auth/session", Ct);
                Assert.Equal(HttpStatusCode.OK, tick.StatusCode);
            }

            pg.Clock.Set(start.AddDays(179));
            var nearEnd = await activeA.GetFromJsonAsync<SessionResponse>("/api/auth/session", Ct);
            Assert.Equal(start.AddDays(180), nearEnd!.SlidingUntil);
            pg.Clock.Set(start.AddDays(180).AddSeconds(1));
            using var absolute = await activeB.GetAsync("/api/auth/session", Ct);
            Assert.Equal(HttpStatusCode.Unauthorized, absolute.StatusCode);

            // Privilegierte Rolle: 8 Stunden gleitend, 24 Stunden absolut.
            pg.Clock.Set(start);
            var admin = await pg.IssueSessionAsync(_t.Id, _t.Admin, Ct);
            using var adminClient = pg.ClientWithSession(admin, instanceB);
            var adminInfo = await adminClient.GetFromJsonAsync<SessionResponse>("/api/auth/session", Ct);
            Assert.Equal(SessionKindDto.Privileged, adminInfo!.Kind);
            Assert.Equal(start.AddHours(8), adminInfo.SlidingUntil);
            pg.Clock.Advance(TimeSpan.FromHours(9));
            using var adminExpired = await adminClient.GetAsync("/api/auth/session", Ct);
            Assert.Equal(HttpStatusCode.Unauthorized, adminExpired.StatusCode);

            // „Abmeldung aus allen Sitzungen“ auf Instanz B beendet Sitzungen auf Instanz A, einschließlich der Kiosk-Personensitzung.
            pg.Clock.Set(start);
            var first = await pg.IssueSessionAsync(_t.Id, _t.MemberA, Ct);
            var second = await pg.IssueSessionAsync(_t.Id, _t.MemberA, Ct);
            var kiosk = await pg.KioskSessionAsync(_t.Id, _t.MemberA, Ct);
            using var firstOnA = pg.ClientWithSession(first);
            using var secondOnB = pg.ClientWithSession(second, instanceB);
            using var kioskOnA = pg.KioskClient(kiosk);
            using var kioskAlive = await kioskOnA.GetAsync("/api/auth/session", Ct);
            Assert.Equal(HttpStatusCode.OK, kioskAlive.StatusCode);

            using var logoutAll = await secondOnB.PostAsync("/api/auth/logout-all", null, Ct);
            Assert.Equal(HttpStatusCode.OK, logoutAll.StatusCode);
            Assert.True((await logoutAll.Content.ReadFromJsonAsync<CountResponse>(Ct))!.Count >= 3);
            using var firstGone = await firstOnA.GetAsync("/api/me", Ct);
            Assert.Equal(HttpStatusCode.Unauthorized, firstGone.StatusCode);
            using var secondGone = await secondOnB.GetAsync("/api/me", Ct);
            Assert.Equal(HttpStatusCode.Unauthorized, secondGone.StatusCode);
            using var kioskGone = await kioskOnA.GetAsync("/api/auth/session", Ct);
            Assert.Equal("kiosk_device", (await kioskGone.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("kind").GetString());
        }
        finally
        {
            pg.Clock.Set(start);
        }
    }

    [Fact]
    public async Task Zustandsaendernder_Request_ohne_passendes_CSRF_Token_gilt_als_nicht_angemeldet()
    {
        var session = await pg.IssueSessionAsync(_t.Id, _t.MemberA, Ct);
        using var client = pg.Api.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", $"{SessionCookies.Session}={session.Token}; {SessionCookies.Csrf}={session.CsrfToken}");

        using var read = await client.GetAsync("/api/me", Ct);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);

        using var withoutHeader = await client.PostAsync("/api/me/recovery-code", null, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, withoutHeader.StatusCode);

        using var wrong = new HttpRequestMessage(HttpMethod.Post, "/api/me/recovery-code");
        wrong.Headers.Add(SessionCookies.CsrfHeader, "falsch");
        using var wrongResponse = await client.SendAsync(wrong, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongResponse.StatusCode);

        using var right = new HttpRequestMessage(HttpMethod.Post, "/api/me/recovery-code");
        right.Headers.Add(SessionCookies.CsrfHeader, session.CsrfToken);
        using var rightResponse = await client.SendAsync(right, Ct);
        Assert.Equal(HttpStatusCode.OK, rightResponse.StatusCode);

        // Das Sitzungscookie ist HttpOnly, das CSRF-Cookie lesbar; beide Secure und SameSite=Lax (A-007).
        using var browser = pg.Browser();
        using var admin = pg.Client(_t.Id, _t.Admin);
        using var joinCode = await admin.PostAsJsonAsync("/api/access/join-codes", new CreateJoinCodeRequest(1, 1), Ct);
        var code = (await joinCode.Content.ReadFromJsonAsync<JoinCodeResponse>(Ct))!.Code;
        using var authenticator = new SoftwareAuthenticator();
        var ceremony = await browser.PostJsonAsync<PasskeyCeremonyResponse>($"/api/join/{code}/passkey-options", new JoinPasskeyOptionsRequest("Cookiekind"), Ct);
        using var joined = await browser.PostAsync($"/api/join/{code}", new JoinRequest("Cookiekind", VisibilityDto.Company, new PasskeyAnswerRequest(ceremony.State, authenticator.CreateAttestation(ceremony.Options), null), null, false, null, null), Ct);
        Assert.Equal(HttpStatusCode.Created, joined.StatusCode);
        var setCookies = joined.Headers.GetValues("Set-Cookie").ToList();
        var sessionCookie = setCookies.Single(c => c.StartsWith(SessionCookies.Session + "=", StringComparison.Ordinal));
        var csrfCookie = setCookies.Single(c => c.StartsWith(SessionCookies.Csrf + "=", StringComparison.Ordinal));
        Assert.Contains("httponly", sessionCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", sessionCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", sessionCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("httponly", csrfCookie, StringComparison.OrdinalIgnoreCase);
        // Das Nutzungslimit des Codes ist erreicht.
        using var second = pg.Browser();
        using var exhausted = await second.GetAsync($"/api/join/{code}", Ct);
        Assert.Equal(HttpStatusCode.NotFound, exhausted.StatusCode);
    }

    [Fact]
    public async Task Wartende_Beitraege_werden_nach_erneuter_Anmeldung_synchronisiert_nach_Austritt_nicht()
    {
        // Offline-Freigabe ist ein lokaler Ablaufzeitpunkt (Zugang 7); die Synchronisierung verlangt eine gültige Serversitzung derselben Person.
        var start = pg.Clock.GetUtcNow();
        try
        {
            var challenge = await pg.Api.Services.GetRequiredService<ITenantScopeFactory>().RunAsync(TenantContext.ForPerson(_t.Id, _t.Manager, [Role.Member, Role.ProgrammeManager]), (sp, ct) =>
                sp.GetRequiredService<IChallengeCatalog>().StartRunningAsync("Offline-Challenge", ChallengeMetric.Checkmark, 50m, start.AddDays(-2), start.AddDays(12), ct), Ct);
            using var admin = pg.Client(_t.Id, _t.Admin);
            var code = await AccessJoinTests.CreateJoinCodeAsync(pg, _t, Ct);
            var (phone, join, authenticator) = await AccessJoinTests.JoinWithPasskeyAsync(pg, code, "Offline", Ct);
            using (phone)
            using (authenticator)
            {
                // Zwei Tage offline erfasst; die Serversitzung ist inzwischen abgelaufen (Test: Abmeldung), der Beitrag wartet mit seinem Schlüssel.
                var recordedAt = start.AddDays(-1);
                var key = Guid.CreateVersion7().ToString("D");
                using var logout = await phone.PostAsync("/api/auth/logout", null, Ct);
                using var unauthenticated = await phone.PostAsync($"/api/challenges/{challenge:D}/contributions", new ContributionRequest("1", recordedAt, "mobile", key, null), Ct);
                Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

                // Erneute Online-Anmeldung derselben Person: der wartende Beitrag wird synchronisiert (einmal, idempotent).
                pg.Clock.Advance(TimeSpan.FromDays(1));
                await AccessJoinTests.LoginWithPasskeyAsync(phone, authenticator, Ct);
                using var synced = await phone.PostAsync($"/api/challenges/{challenge:D}/contributions", new ContributionRequest("1", recordedAt, "mobile", key, null), Ct);
                Assert.Equal(HttpStatusCode.Created, synced.StatusCode);
                using var again = await phone.PostAsync($"/api/challenges/{challenge:D}/contributions", new ContributionRequest("1", recordedAt, "mobile", key, null), Ct);
                Assert.Equal(HttpStatusCode.OK, again.StatusCode);

                // Nach dem Austritt gibt es keine Sitzung und keinen Weg mehr: nichts wird synchronisiert.
                var waiting = Guid.CreateVersion7().ToString("D");
                using var leave = await phone.PostAsync("/api/me/leave", null, Ct);
                Assert.Equal(HttpStatusCode.NoContent, leave.StatusCode);
                using var afterLeave = await phone.PostAsync($"/api/challenges/{challenge:D}/contributions", new ContributionRequest("1", start, "mobile", waiting, null), Ct);
                Assert.Equal(HttpStatusCode.Unauthorized, afterLeave.StatusCode);
                using var relogin = pg.Browser();
                using var noWay = await relogin.PostAsync("/api/auth/passkey/options", null, Ct);
                var ceremony = (await noWay.Content.ReadFromJsonAsync<PasskeyCeremonyResponse>(Ct))!;
                using var rejected = await relogin.PostAsync("/api/auth/passkey", new PasskeyAnswerRequest(ceremony.State, authenticator.CreateAssertion(ceremony.Options), null), Ct);
                Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
                _ = join;
            }
        }
        finally
        {
            pg.Clock.Set(start);
        }
    }
}
