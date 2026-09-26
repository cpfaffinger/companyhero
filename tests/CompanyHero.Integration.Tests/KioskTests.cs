using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CompanyHero.Modules.Challenges.Api;
using CompanyHero.Modules.Challenges.Application;
using CompanyHero.Modules.Challenges.Domain;
using CompanyHero.Modules.Identity.Api;
using CompanyHero.Modules.Identity.Infrastructure.Http;
using CompanyHero.Modules.Metering.Application;
using CompanyHero.Modules.Organisation.Domain;
using CompanyHero.Modules.Privacy.Api;
using CompanyHero.Platform.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyHero.Integration.Tests;

/// <summary>
/// Zugang 10.2 und 10.6 sowie A-005 Sitzungsteile (A-018): Beitritt am Kiosk mit Kennung und PIN, Übertragung per QR auf ein
/// eigenes Gerät, danach Passkey; Drosselung (fünf Fehlversuche je Kennung, 20 je Gerät); Widerruf beendet Personensitzungen;
/// keine Namensliste; Auto-Logout nach 60 Sekunden (je Tenant 30 bis 120), absolut 10 Minuten; Personenwechsel ohne Rest;
/// Rotation und Widerruf des Gerätegeheimnisses; Vorgangskennung nur aus der Kiosk-Personensitzung.
/// </summary>
[Collection(PostgresTests.Name)]
public sealed class KioskTests(PostgresFixture pg) : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ScratchTenant _t = null!;
    private string _joinCode = null!;

    public async ValueTask InitializeAsync()
    {
        _t = await pg.CreateScratchTenantWithMembersAsync($"Kiosk {Guid.NewGuid():N}");
        _joinCode = await AccessJoinTests.CreateJoinCodeAsync(pg, _t, Ct);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    /// <summary>Tenant-Admin legt ein Gerät an, das Gerät registriert sich mit dem einmaligen Code (Zugang 6.1).</summary>
    public static async Task<(TestBrowser Kiosk, string DeviceId)> RegisterKioskAsync(PostgresFixture pg, ScratchTenant tenant, CancellationToken ct)
    {
        using var admin = pg.Client(tenant.Id, tenant.Admin);
        using var created = await admin.PostAsJsonAsync("/api/access/kiosk-devices", new CreateKioskDeviceRequest("Eingang Halle 2"), ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var device = (await created.Content.ReadFromJsonAsync<KioskDeviceAdminResponse>(ct))!;
        Assert.NotNull(device.RegistrationCode);

        var kiosk = pg.Browser();
        var state = await kiosk.PostJsonAsync<KioskDeviceResponse>("/api/kiosk/register", new KioskRegisterRequest(device.RegistrationCode!), ct);
        Assert.Equal(device.DeviceId, state.DeviceId);
        Assert.Equal(60, state.IdleSeconds);
        Assert.NotNull(kiosk.Cookies[SessionCookies.KioskDevice]);
        Assert.NotNull(kiosk.Cookies[SessionCookies.Csrf]);
        Assert.Null(kiosk.Cookies[SessionCookies.Session]);

        // Der Code ist verbraucht.
        using var second = pg.Browser();
        using var reuse = await second.PostAsync("/api/kiosk/register", new KioskRegisterRequest(device.RegistrationCode!), ct);
        Assert.Equal(HttpStatusCode.NotFound, reuse.StatusCode);
        return (kiosk, device.DeviceId);
    }

    public static Task<KioskLoginResponse> KioskLoginAsync(TestBrowser kiosk, string kioskId, string pin, CancellationToken ct) =>
        kiosk.PostJsonAsync<KioskLoginResponse>("/api/kiosk/login", new KioskLoginRequest(kioskId, pin), ct);

    [Fact]
    public async Task Beitritt_am_Kiosk_mit_Kennung_und_PIN_Uebertragung_per_QR_auf_eigenes_Geraet_danach_Passkey_Einrichtung()
    {
        var (kiosk, deviceId) = await RegisterKioskAsync(pg, _t, Ct);
        using (kiosk)
        {
            // Gerätesitzung: Kollektivstand und Marke des Tenants, Beitritt und Anmeldemaske; nichts Persönliches, keine Liste.
            using var theme = await kiosk.GetAsync("/api/branding/theme", Ct);
            Assert.Equal(HttpStatusCode.OK, theme.StatusCode);
            using var cards = await kiosk.GetAsync("/api/challenges", Ct);
            Assert.Equal(HttpStatusCode.OK, cards.StatusCode);
            using var members = await kiosk.GetAsync("/api/members", Ct);
            Assert.Equal(HttpStatusCode.Forbidden, members.StatusCode);
            using var me = await kiosk.GetAsync("/api/me", Ct);
            Assert.Equal(HttpStatusCode.Forbidden, me.StatusCode);
            var device = await kiosk.GetJsonAsync<KioskDeviceResponse>("/api/kiosk/device", Ct);
            Assert.Equal("Eingang Halle 2", device.Name);

            // Beitritt am Kiosk: Kennung wird angezeigt, PIN gewählt, Wiederherstellungscode dazu (Zugang 2.2, 6.2).
            using var trivial = await kiosk.PostAsync($"/api/join/{_joinCode}/kiosk", new KioskJoinRequest("Kioskkind", VisibilityDto.Team, "1234"), Ct);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, trivial.StatusCode);
            var join = await kiosk.PostJsonAsync<JoinResponse>($"/api/join/{_joinCode}/kiosk", new KioskJoinRequest("Kioskkind", VisibilityDto.Team, "7391"), Ct, HttpStatusCode.Created);
            Assert.Matches("^[0-9]{6}$", join.KioskId);
            Assert.NotNull(join.RecoveryCode);
            Assert.Null(join.SessionKind);
            Assert.Null(kiosk.Cookies[SessionCookies.Session]);

            // Anmeldung mit Kennung und PIN: Personensitzung mit Countdown; Kiosk-Umfang nach Zugang 6.3.
            var login = await KioskLoginAsync(kiosk, join.KioskId, "7391", Ct);
            Assert.Equal("Kioskkind", login.DisplayName);
            Assert.Equal(60, login.IdleSeconds);
            Assert.Equal(pg.Clock.GetUtcNow().AddSeconds(60), login.IdleUntil);
            Assert.Equal(pg.Clock.GetUtcNow().AddMinutes(10), login.AbsoluteUntil);
            Assert.NotNull(kiosk.Cookies[SessionCookies.Session]);
            var session = await kiosk.GetJsonAsync<SessionResponse>("/api/auth/session", Ct);
            Assert.Equal(SessionKindDto.KioskPerson, session.Kind);
            Assert.Equal(deviceId, session.KioskDeviceId);
            using var profile = await kiosk.GetAsync("/api/me/access", Ct);
            Assert.Equal(HttpStatusCode.Forbidden, profile.StatusCode);
            using var history = await kiosk.GetAsync($"/api/persons/{join.PersonId}/activities", Ct);
            Assert.Equal(HttpStatusCode.Forbidden, history.StatusCode);
            using var list = await kiosk.GetAsync("/api/members", Ct);
            Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);

            // Beitrag am Kiosk mit Vorgangskennung (A-005): Reservierung nur aus der Kiosk-Personensitzung.
            var challenge = await pg.Api.Services.GetRequiredService<ITenantScopeFactory>().RunAsync(TenantContext.ForPerson(_t.Id, _t.Manager, [Role.Member, Role.ProgrammeManager]), (sp, ct) =>
                sp.GetRequiredService<IChallengeCatalog>().StartRunningAsync("Kiosk-Challenge", ChallengeMetric.Checkmark, 50m, pg.Clock.GetUtcNow().AddDays(-1), pg.Clock.GetUtcNow().AddDays(7), ct), Ct);
            var operation = await kiosk.PostJsonAsync<OperationResponse>($"/api/challenges/{challenge:D}/contribution-operations", null, Ct, HttpStatusCode.Created);
            var contribution = await kiosk.PostJsonAsync<ContributionResponse>($"/api/challenges/{challenge:D}/contributions", new ContributionRequest("1", pg.Clock.GetUtcNow(), "kiosk", null, operation.OperationId), Ct, HttpStatusCode.Created);
            Assert.Equal("recorded", contribution.Outcome);

            // Übertragung auf das eigene Gerät: einmaliger Link, fünf Minuten; dort Mitgliedssitzung und Einrichtungspflicht (Zugang 6.4).
            var transfer = await kiosk.PostJsonAsync<TransferResponse>("/api/kiosk/transfer", null, Ct);
            Assert.StartsWith(PostgresFixture.Origin + "/zugang/transfer?token=", transfer.Url, StringComparison.Ordinal);
            Assert.Equal(pg.Clock.GetUtcNow().AddMinutes(5), transfer.ExpiresAt);
            var token = System.Web.HttpUtility.ParseQueryString(new Uri(transfer.Url).Query)["token"]!;
            using var phone = pg.Browser();
            var transferred = await phone.PostJsonAsync<LoginResponse>("/api/auth/transfer", new TokenRequest(token), Ct);
            Assert.Equal(join.PersonId, transferred.PersonId);
            Assert.True(transferred.MustSetUpAccess);
            Assert.Equal(SessionKindDto.Member, transferred.Kind);
            using var twice = pg.Browser();
            using var reused = await twice.PostAsync("/api/auth/transfer", new TokenRequest(token), Ct);
            Assert.Equal(HttpStatusCode.Unauthorized, reused.StatusCode);

            // Kennung und PIN sind außerhalb der Gerätesitzung kein Anmeldeweg (Zugang 6.4).
            using var noDevice = await phone.PostAsync("/api/kiosk/login", new KioskLoginRequest(join.KioskId, "7391"), Ct);
            Assert.Equal(HttpStatusCode.Forbidden, noDevice.StatusCode);

            // Passkey-Einrichtung auf dem eigenen Gerät.
            using var authenticator = new SoftwareAuthenticator();
            var ceremony = await phone.PostJsonAsync<PasskeyCeremonyResponse>("/api/me/passkeys/options", new JoinPasskeyOptionsRequest("Kioskkind"), Ct);
            var added = await phone.PostJsonAsync<PasskeyResponse>("/api/me/passkeys", new PasskeyAnswerRequest(ceremony.State, authenticator.CreateAttestation(ceremony.Options), "Handy"), Ct, HttpStatusCode.Created);
            Assert.Equal("Handy", added.DeviceName);
            var overview = await phone.GetJsonAsync<AccessOverviewResponse>("/api/me/access", Ct);
            Assert.Single(overview.Passkeys);
            Assert.Equal(join.KioskId, overview.KioskId);
            Assert.True(overview.KioskPinSet);

            // Metering: Beitritt und Gerätemonat ohne Personenbezug (Zugang 9).
            var ledger = await pg.Api.Services.GetRequiredService<ITenantScopeFactory>().RunAsync(TenantContext.ForTenant(_t.Id), (sp, ct) => sp.GetRequiredService<IMeteringLedger>().ListAsync(null, ct), Ct);
            Assert.Contains(ledger, e => e.Metric == "member.joined" && e.SubjectRef.StartsWith("join-code:", StringComparison.Ordinal));
            Assert.Contains(ledger, e => e.Metric == "kiosk.device_month" && e.SubjectRef == "kiosk-device:" + deviceId);
            Assert.DoesNotContain(ledger, e => e.SubjectRef.Contains(join.PersonId, StringComparison.Ordinal) || e.IdempotencyKey.Contains(join.KioskId, StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task Fuenf_Fehlversuche_sperren_die_Kennung_20_das_Geraet_Widerruf_beendet_Personensitzungen_keine_Namensliste()
    {
        var start = pg.Clock.GetUtcNow();
        var (kiosk, deviceId) = await RegisterKioskAsync(pg, _t, Ct);
        using (kiosk)
        {
            try
            {
                var join = await kiosk.PostJsonAsync<JoinResponse>($"/api/join/{_joinCode}/kiosk", new KioskJoinRequest("Gesperrt", VisibilityDto.Company, "8524"), Ct, HttpStatusCode.Created);
                var other = await kiosk.PostJsonAsync<JoinResponse>($"/api/join/{_joinCode}/kiosk", new KioskJoinRequest("Andere", VisibilityDto.Company, "9137"), Ct, HttpStatusCode.Created);

                // Fünf Fehlversuche für eine Kennung: die Kennung ist am Gerät 15 Minuten gesperrt, auch mit richtiger PIN.
                for (var i = 0; i < 5; i++)
                {
                    using var wrong = await kiosk.PostAsync("/api/kiosk/login", new KioskLoginRequest(join.KioskId, "0000"), Ct);
                    Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
                }

                using var locked = await kiosk.PostAsync("/api/kiosk/login", new KioskLoginRequest(join.KioskId, "8524"), Ct);
                Assert.Equal(HttpStatusCode.Locked, locked.StatusCode);
                Assert.Equal("kiosk_id_locked", (await locked.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("detail").GetString());
                // Eine andere Kennung ist am selben Gerät weiter anmeldbar.
                var otherLogin = await KioskLoginAsync(kiosk, other.KioskId, "9137", Ct);
                Assert.Equal("Andere", otherLogin.DisplayName);
                using var logout = await kiosk.PostAsync("/api/auth/logout", null, Ct);
                Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

                // Nach 15 Minuten ist die Kennung wieder frei.
                pg.Clock.Advance(TimeSpan.FromMinutes(15).Add(TimeSpan.FromSeconds(1)));
                var again = await KioskLoginAsync(kiosk, join.KioskId, "8524", Ct);
                Assert.Equal("Gesperrt", again.DisplayName);
                using var logout2 = await kiosk.PostAsync("/api/auth/logout", null, Ct);

                // 20 Fehlversuche über verschiedene Kennungen in 15 Minuten sperren das Gerät.
                for (var i = 0; i < 20; i++)
                {
                    using var wrong = await kiosk.PostAsync("/api/kiosk/login", new KioskLoginRequest((100000 + i).ToString("000000", System.Globalization.CultureInfo.InvariantCulture), "0000"), Ct);
                    Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
                }

                using var deviceLocked = await kiosk.PostAsync("/api/kiosk/login", new KioskLoginRequest(other.KioskId, "9137"), Ct);
                Assert.Equal(HttpStatusCode.Locked, deviceLocked.StatusCode);
                Assert.Equal("device_locked", (await deviceLocked.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("detail").GetString());
                pg.Clock.Advance(TimeSpan.FromMinutes(15).Add(TimeSpan.FromSeconds(1)));
                var free = await KioskLoginAsync(kiosk, other.KioskId, "9137", Ct);
                Assert.Equal("Andere", free.DisplayName);

                // Sicherheitsprotokoll pseudonymisiert: Fehlversuche ohne Kennung im Klartext (Zugang 6.2, 9).
                using var admin = pg.Client(_t.Id, _t.Admin);
                var security = await admin.GetFromJsonAsync<List<SecurityEntryResponse>>("/api/audit/security", Ct);
                var failures = security!.Where(e => e.EventType == "kiosk.login" && !e.Success).ToList();
                Assert.True(failures.Count >= 26, $"{failures.Count} Fehlversuche");
                Assert.DoesNotContain(failures, e => (e.Pseudonym ?? string.Empty).Contains(join.KioskId, StringComparison.Ordinal) || (e.Detail ?? string.Empty).Contains(join.KioskId, StringComparison.Ordinal));
                Assert.Contains(failures, e => e.Detail == "kiosk_id_locked");
                Assert.Contains(failures, e => e.Detail == "device_locked");

                // Widerruf des Geräts beendet alle Personensitzungen auf diesem Gerät (Zugang 6.5); die Verwaltung sieht Zahlen, nie Personen.
                var devices = await admin.GetFromJsonAsync<List<KioskDeviceAdminResponse>>("/api/access/kiosk-devices", Ct);
                var mine = devices!.Single(d => d.DeviceId == deviceId);
                Assert.Equal(3, mine.LoginCount);
                Assert.NotNull(mine.LastSeenAt);
                Assert.Null(mine.RegistrationCode);
                // Der Widerruf ist eine sensible Aktion (Zugang 3.3): die über 30 Minuten alte Admin-Sitzung wird abgelehnt, eine frische angenommen.
                using var stale = await admin.PostAsync($"/api/access/kiosk-devices/{deviceId}/revoke", null, Ct);
                Assert.Equal(HttpStatusCode.Forbidden, stale.StatusCode);
                using var freshAdmin = pg.ClientWithSession(await pg.IssueSessionAsync(_t.Id, _t.Admin, Ct));
                using var revoke = await freshAdmin.PostAsync($"/api/access/kiosk-devices/{deviceId}/revoke", null, Ct);
                Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
                using var personGone = await kiosk.GetAsync("/api/auth/session", Ct);
                Assert.Equal(HttpStatusCode.Unauthorized, personGone.StatusCode);
                using var deviceGone = await kiosk.GetAsync("/api/kiosk/device", Ct);
                Assert.Equal(HttpStatusCode.Unauthorized, deviceGone.StatusCode);
                var audit = await admin.GetFromJsonAsync<List<AuditEntryResponse>>("/api/audit", Ct);
                Assert.Contains(audit!, a => a.Action == "access.kiosk_device.revoked" && a.SubjectRef == deviceId);
            }
            finally
            {
                pg.Clock.Set(start);
            }
        }
    }

    [Fact]
    public async Task Auto_Logout_nach_60_Sekunden_je_Tenant_30_bis_120_absolut_10_Minuten_Personenwechsel_ohne_Rest_Rotation_des_Geraetegeheimnisses()
    {
        var start = pg.Clock.GetUtcNow();
        var (kiosk, _) = await RegisterKioskAsync(pg, _t, Ct);
        using (kiosk)
        {
            try
            {
                var anna = await kiosk.PostJsonAsync<JoinResponse>($"/api/join/{_joinCode}/kiosk", new KioskJoinRequest("Anna K", VisibilityDto.Company, "8524"), Ct, HttpStatusCode.Created);
                var ben = await kiosk.PostJsonAsync<JoinResponse>($"/api/join/{_joinCode}/kiosk", new KioskJoinRequest("Ben K", VisibilityDto.Company, "9137"), Ct, HttpStatusCode.Created);

                // Jede Eingabe setzt den Countdown zurück; 60 Sekunden ohne Eingabe beenden die Personensitzung.
                await KioskLoginAsync(kiosk, anna.KioskId, "8524", Ct);
                pg.Clock.Advance(TimeSpan.FromSeconds(59));
                using var alive = await kiosk.GetAsync("/api/auth/session", Ct);
                Assert.Equal(HttpStatusCode.OK, alive.StatusCode);
                pg.Clock.Advance(TimeSpan.FromSeconds(59));
                using var stillAlive = await kiosk.GetAsync("/api/auth/session", Ct);
                Assert.Equal(HttpStatusCode.OK, stillAlive.StatusCode);
                pg.Clock.Advance(TimeSpan.FromSeconds(61));
                // Die Personensitzung ist beendet; die Gerätesitzung besteht weiter und antwortet ohne Person.
                Assert.Equal(SessionKindDto.KioskDevice, (await kiosk.GetJsonAsync<SessionResponse>("/api/auth/session", Ct)).Kind);
                using var noPerson = await kiosk.PostAsync("/api/kiosk/transfer", null, Ct);
                Assert.Equal(HttpStatusCode.Forbidden, noPerson.StatusCode);

                // Absolutes Ende nach 10 Minuten trotz laufender Eingaben.
                await KioskLoginAsync(kiosk, anna.KioskId, "8524", Ct);
                for (var i = 0; i < 11; i++)
                {
                    pg.Clock.Advance(TimeSpan.FromSeconds(50));
                    using var tick = await kiosk.GetAsync("/api/auth/session", Ct);
                    Assert.Equal(HttpStatusCode.OK, tick.StatusCode);
                }

                pg.Clock.Advance(TimeSpan.FromSeconds(50));
                Assert.Equal(SessionKindDto.KioskDevice, (await kiosk.GetJsonAsync<SessionResponse>("/api/auth/session", Ct)).Kind);

                // Personenwechsel: Bens Anmeldung beendet Annas Sitzung sofort; nichts aus der Vorsitzung bleibt sichtbar.
                await KioskLoginAsync(kiosk, anna.KioskId, "8524", Ct);
                var annaSession = kiosk.Cookies[SessionCookies.Session]!;
                var annaCsrf = kiosk.Cookies[SessionCookies.Csrf]!;
                var benLogin = await KioskLoginAsync(kiosk, ben.KioskId, "9137", Ct);
                Assert.Equal(ben.PersonId, benLogin.PersonId);
                var current = await kiosk.GetJsonAsync<SessionResponse>("/api/auth/session", Ct);
                Assert.Equal(ben.PersonId, current.PersonId);
                using var annaClient = pg.Api.CreateClient();
                annaClient.DefaultRequestHeaders.Add("Cookie", $"{SessionCookies.Session}={annaSession}; {SessionCookies.Csrf}={annaCsrf}; {SessionCookies.KioskDevice}={kiosk.Cookies[SessionCookies.KioskDevice]}");
                using var annaGone = await annaClient.GetAsync("/api/auth/session", Ct);
                Assert.Equal(HttpStatusCode.OK, annaGone.StatusCode); // Gerätesitzung antwortet ...
                Assert.Equal("kiosk_device", (await annaGone.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("kind").GetString()); // ... aber ohne Person.

                // Der Tenant stellt den Auto-Logout ein (30 bis 120 Sekunden); außerhalb wird abgelehnt.
                using var admin = pg.Client(_t.Id, _t.Admin);
                var policy = await admin.GetFromJsonAsync<LoginPolicyResponse>("/api/access/policy", Ct);
                using var tooShort = await admin.PutAsJsonAsync("/api/access/policy", new LoginPolicyRequest(true, true, true, [], [], 20), Ct);
                Assert.Equal(HttpStatusCode.UnprocessableEntity, tooShort.StatusCode);
                using var shorter = await admin.PutAsJsonAsync("/api/access/policy", new LoginPolicyRequest(true, true, true, [], [], 30), Ct);
                Assert.Equal(HttpStatusCode.OK, shorter.StatusCode);
                var login30 = await KioskLoginAsync(kiosk, ben.KioskId, "9137", Ct);
                Assert.Equal(30, login30.IdleSeconds);
                pg.Clock.Advance(TimeSpan.FromSeconds(31));
                Assert.Equal(SessionKindDto.KioskDevice, (await kiosk.GetJsonAsync<SessionResponse>("/api/auth/session", Ct)).Kind);
                _ = policy;

                // Gerätegeheimnis rotiert nach 30 Tagen automatisch: neues Cookie, altes ungültig.
                var oldSecret = kiosk.Cookies[SessionCookies.KioskDevice]!;
                pg.Clock.Advance(TimeSpan.FromDays(30));
                using var rotated = await kiosk.GetAsync("/api/kiosk/device", Ct);
                Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);
                Assert.NotEqual(oldSecret, kiosk.Cookies[SessionCookies.KioskDevice]);
                using var withOld = pg.Api.CreateClient();
                withOld.DefaultRequestHeaders.Add("Cookie", $"{SessionCookies.KioskDevice}={oldSecret}");
                using var oldGone = await withOld.GetAsync("/api/kiosk/device", Ct);
                Assert.Equal(HttpStatusCode.Unauthorized, oldGone.StatusCode);
                using var withNew = await kiosk.GetAsync("/api/kiosk/device", Ct);
                Assert.Equal(HttpStatusCode.OK, withNew.StatusCode);
            }
            finally
            {
                pg.Clock.Set(start);
            }
        }
    }

    [Fact]
    public async Task PIN_Neusetzung_mit_Wiederherstellungscode_am_Kiosk_und_erzwungener_Anbieter_verhindert_Kiosk_Beitritt_nicht_Kiosk_Nutzung()
    {
        var (kiosk, _) = await RegisterKioskAsync(pg, _t, Ct);
        using (kiosk)
        {
            var join = await kiosk.PostJsonAsync<JoinResponse>($"/api/join/{_joinCode}/kiosk", new KioskJoinRequest("Vergesslich", VisibilityDto.Company, "8524"), Ct, HttpStatusCode.Created);

            // Vergessene PIN ohne eigenes Gerät: Neusetzung mit dem Wiederherstellungscode am Kiosk (Zugang 6.2); der Code erneuert sich.
            using var wrongCode = await kiosk.PostAsync("/api/kiosk/pin/reset", new PinResetRequest(join.KioskId, "AAA-AAA-AAA-AAA", "2580"), Ct);
            Assert.Equal(HttpStatusCode.Unauthorized, wrongCode.StatusCode);
            using var reset = await kiosk.PostAsync("/api/kiosk/pin/reset", new PinResetRequest(join.KioskId, join.RecoveryCode!, "2580"), Ct);
            Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
            using var oldPin = await kiosk.PostAsync("/api/kiosk/login", new KioskLoginRequest(join.KioskId, "8524"), Ct);
            Assert.Equal(HttpStatusCode.Unauthorized, oldPin.StatusCode);
            await KioskLoginAsync(kiosk, join.KioskId, "2580", Ct);
            using var codeUsed = await kiosk.PostAsync("/api/kiosk/pin/reset", new PinResetRequest(join.KioskId, join.RecoveryCode!, "3690"), Ct);
            Assert.Equal(HttpStatusCode.Unauthorized, codeUsed.StatusCode);
            // PIN-Änderung aus der Personensitzung.
            using var change = await kiosk.PostAsync("/api/kiosk/pin", new PinRequest("4826"), Ct);
            Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);
            using var logout = await kiosk.PostAsync("/api/auth/logout", null, Ct);

            // Erzwungener Anbieter (Zugang 3.4, A-015): kein Beitritt am Kiosk, aber Kiosk-Nutzung für bestehende Personen.
            using var admin = pg.Client(_t.Id, _t.Admin);
            using var forced = await admin.PutAsJsonAsync("/api/access/policy", new LoginPolicyRequest(true, true, true, [], ["microsoft"], 60), Ct);
            Assert.Equal(HttpStatusCode.OK, forced.StatusCode);
            using var preview = pg.Browser();
            var ways = await preview.GetJsonAsync<JoinPreviewResponse>($"/api/join/{_joinCode}", Ct);
            Assert.False(ways.Ways.Kiosk);
            Assert.Equal(["microsoft"], ways.Ways.ForcedProviderKeys);
            Assert.Equal(["microsoft"], ways.Ways.Providers.Select(p => p.Key).ToList());
            using var noKioskJoin = await kiosk.PostAsync($"/api/join/{_joinCode}/kiosk", new KioskJoinRequest("Zu spät", VisibilityDto.Company, "2580"), Ct);
            Assert.Equal(HttpStatusCode.Forbidden, noKioskJoin.StatusCode);
            using var noPasskeyJoin = pg.Browser();
            using var ceremony = await noPasskeyJoin.PostAsync($"/api/join/{_joinCode}/passkey-options", new JoinPasskeyOptionsRequest("X"), Ct);
            using var authenticator = new SoftwareAuthenticator();
            var options = (await ceremony.Content.ReadFromJsonAsync<PasskeyCeremonyResponse>(Ct))!;
            using var rejected = await noPasskeyJoin.PostAsync($"/api/join/{_joinCode}", new JoinRequest("Ohne Anbieter", VisibilityDto.Company, new PasskeyAnswerRequest(options.State, authenticator.CreateAttestation(options.Options), null), null, false, null), Ct);
            Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);
            var still = await KioskLoginAsync(kiosk, join.KioskId, "4826", Ct);
            Assert.Equal("Vergesslich", still.DisplayName);

            // Auswirkung einer Änderung als Zahl ohne Namen (Zugang 3.4): Vergesslich hätte ohne Kiosk und Passkey keinen Weg.
            var impact = await admin.PostAsJsonAsync("/api/access/policy/preview", new LoginPolicyRequest(false, false, true, [], [], 60), Ct);
            Assert.Equal(HttpStatusCode.OK, impact.StatusCode);
            Assert.True((await impact.Content.ReadFromJsonAsync<LoginPolicyImpactResponse>(Ct))!.PersonsWithoutWay >= 1);
            using var restore = await admin.PutAsJsonAsync("/api/access/policy", new LoginPolicyRequest(true, true, true, [], [], 60), Ct);
            Assert.Equal(HttpStatusCode.OK, restore.StatusCode);
        }
    }
}
