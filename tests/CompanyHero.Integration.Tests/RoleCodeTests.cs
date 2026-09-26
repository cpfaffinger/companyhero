using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CompanyHero.Modules.Identity.Api;
using CompanyHero.Modules.Identity.Infrastructure.Http;
using CompanyHero.Modules.Privacy.Api;
using CompanyHero.Platform.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyHero.Integration.Tests;

/// <summary>
/// Zugang 10.5 (A-014, A-015, A-017): Rollencode für Programm-Manager setzt den Klarnamen; Magic-Link-Login für den
/// Tenant-Admin abgelehnt, Passkey-Login angenommen; sensible Aktion ohne frische Anmeldung (15 Minuten) abgelehnt.
/// Rollencode-Ausgabe und -Einlösung stehen pseudonymisiert im Prüfprotokoll (Zugang 9).
/// </summary>
[Collection(PostgresTests.Name)]
public sealed class RoleCodeTests(PostgresFixture pg) : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ScratchTenant _t = null!;
    private string _joinCode = null!;

    public async ValueTask InitializeAsync()
    {
        _t = await pg.CreateScratchTenantWithMembersAsync($"Rollen {Guid.NewGuid():N}");
        _joinCode = await AccessJoinTests.CreateJoinCodeAsync(pg, _t, Ct);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Rollencode_Programm_Manager_setzt_Klarnamen_Magic_Link_fuer_Tenant_Admin_abgelehnt_Passkey_angenommen()
    {
        // Ein Mitglied mit Passkey und E-Mail tritt bei (Voraussetzungen der Funktionsrolle: E-Mail oder Anbieter, Passkey oder Anbieter).
        var (member, join, authenticator) = await AccessJoinTests.JoinWithPasskeyAsync(pg, _joinCode, "Dana", Ct, email: "dana@example.org");
        using (member)
        using (authenticator)
        {
            using var admin = pg.Client(_t.Id, _t.Admin);
            using var issued = await admin.PostAsJsonAsync("/api/access/role-codes", new IssueRoleCodeRequest("programme_manager", "dana@example.org"), Ct);
            Assert.Equal(HttpStatusCode.Created, issued.StatusCode);
            var code = (await issued.Content.ReadFromJsonAsync<RoleCodeResponse>(Ct))!;
            Assert.Equal("programme_manager", code.Role);
            Assert.Contains(pg.Mails.Sent, m => m.Recipient == "dana@example.org" && m.TemplateKey == "konto.rollencode" && m.Link == code.Code);

            // Ohne Klarnamen keine Funktionsrolle (Zugang 2.3).
            using var noName = await member.PostAsync("/api/access/role-codes/redeem", new RedeemRoleCodeRequest(code.Code, null), Ct);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, noName.StatusCode);

            using var redeemed = await member.PostAsync("/api/access/role-codes/redeem", new RedeemRoleCodeRequest(code.Code.ToLowerInvariant(), "Dana Manager"), Ct);
            Assert.Equal(HttpStatusCode.NoContent, redeemed.StatusCode);
            // Die Rolle ändert die Sitzungsart (Zugang 5): die lange Mitgliedssitzung endet, Login mit Passkey ergibt eine privilegierte Sitzung.
            using var old = await member.GetAsync("/api/me", Ct);
            Assert.Equal(HttpStatusCode.Unauthorized, old.StatusCode);
            using var fresh = pg.Browser();
            var login = await AccessJoinTests.LoginWithPasskeyAsync(fresh, authenticator, Ct);
            Assert.Equal(SessionKindDto.Privileged, login.Kind);
            var me = await fresh.GetJsonAsync<JsonElement>("/api/me", Ct);
            Assert.Equal("Dana Manager", me.GetProperty("displayName").GetString());
            Assert.Contains("programme_manager", me.GetProperty("roles").EnumerateArray().Select(r => r.GetString()));
            var session = await fresh.GetJsonAsync<SessionResponse>("/api/auth/session", Ct);
            Assert.Equal(SessionKindDto.Privileged, session.Kind);
            Assert.Equal(session.AuthenticatedAt.AddHours(8), session.SlidingUntil);
            Assert.Equal(session.AuthenticatedAt.AddHours(24), session.AbsoluteUntil);
            // Der Code ist verbraucht.
            using var reused = await fresh.PostAsync("/api/access/role-codes/redeem", new RedeemRoleCodeRequest(code.Code, "Dana Manager"), Ct);
            Assert.Equal(HttpStatusCode.NotFound, reused.StatusCode);

            // Magic-Link allein ist für die privilegierte Rolle kein Anmeldeweg (Zugang 3.3); für ein Mitglied schon.
            using var anonymous = pg.Browser();
            using var requested = await anonymous.PostAsync("/api/auth/magic-link", new MagicLinkRequest("dana@example.org"), Ct);
            Assert.Equal(HttpStatusCode.Accepted, requested.StatusCode);
            var token = pg.Mails.LastTokenFor("dana@example.org", "token");
            Assert.NotNull(token);
            using var rejected = await anonymous.PostAsync("/api/auth/magic-link/consume", new TokenRequest(token!), Ct);
            Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);
            Assert.Equal("magic_link_not_allowed", (await rejected.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("detail").GetString());
            Assert.Null(anonymous.Cookies[SessionCookies.Session]);

            using var memberB = pg.Client(_t.Id, _t.MemberB);
            using var setEmail = await memberB.PutAsJsonAsync("/api/me/email", new EmailRequest("mitglied-b@example.org"), Ct);
            Assert.Equal(HttpStatusCode.NoContent, setEmail.StatusCode);
            using var memberBrowser = pg.Browser();
            using var requestedB = await memberBrowser.PostAsync("/api/auth/magic-link", new MagicLinkRequest("Mitglied-B@example.org"), Ct);
            var tokenB = pg.Mails.LastTokenFor("mitglied-b@example.org", "token")!;
            var loginB = await memberBrowser.PostJsonAsync<LoginResponse>("/api/auth/magic-link/consume", new TokenRequest(tokenB), Ct);
            Assert.Equal(SessionKindDto.Member, loginB.Kind);
            using var twice = await memberBrowser.PostAsync("/api/auth/magic-link/consume", new TokenRequest(tokenB), Ct);
            Assert.Equal(HttpStatusCode.Unauthorized, twice.StatusCode);

            // Unbekannte Adresse: keine Auskunft, keine Nachricht.
            var before = pg.Mails.Sent.Count;
            using var unknown = await anonymous.PostAsync("/api/auth/magic-link", new MagicLinkRequest("niemand@example.org"), Ct);
            Assert.Equal(HttpStatusCode.Accepted, unknown.StatusCode);
            Assert.Equal(before, pg.Mails.Sent.Count);

            // Prüfprotokoll: Ausgabe und Einlösung mit Rolle und handelnder Kennung, ohne Namen (Zugang 9, Datenschutz 6.2).
            var audit = await admin.GetFromJsonAsync<List<AuditEntryResponse>>("/api/audit", Ct);
            Assert.Contains(audit!, a => a.Action == "access.role_code.issued" && a.Detail == "programme_manager" && a.ActorId == _t.Admin.ToString());
            Assert.Contains(audit!, a => a.Action == "access.role_code.redeemed" && a.Detail == "programme_manager" && a.ActorId == join.PersonId);
            Assert.DoesNotContain(audit!, a => (a.Detail ?? string.Empty).Contains("Dana", StringComparison.Ordinal) || (a.SubjectRef ?? string.Empty).Contains("Dana", StringComparison.Ordinal));
            using var insightForbidden = await memberB.GetAsync("/api/audit", Ct);
            Assert.Equal(HttpStatusCode.Forbidden, insightForbidden.StatusCode);
        }
    }

    [Fact]
    public async Task Sensible_Aktion_ohne_frische_Anmeldung_wird_abgelehnt_und_nach_erneuter_Anmeldung_angenommen()
    {
        var start = pg.Clock.GetUtcNow();
        try
        {
            var admin = await pg.IssueSessionAsync(_t.Id, _t.Admin, Ct);
            using var client = pg.ClientWithSession(admin);
            using var freshEnough = await client.PostAsJsonAsync("/api/access/kiosk-devices", new CreateKioskDeviceRequest("Halle 1"), Ct);
            Assert.Equal(HttpStatusCode.Created, freshEnough.StatusCode);

            // 16 Minuten später: Lesen bleibt möglich, sensible Aktionen (Rollencodes, Kiosk-Geräte, Anmeldewege, Marke) sind gesperrt.
            pg.Clock.Advance(TimeSpan.FromMinutes(16));
            using var readable = await client.GetAsync("/api/access/policy", Ct);
            Assert.Equal(HttpStatusCode.OK, readable.StatusCode);
            using var stale = await client.PostAsJsonAsync("/api/access/role-codes", new IssueRoleCodeRequest("editor", null), Ct);
            Assert.Equal(HttpStatusCode.Forbidden, stale.StatusCode);
            Assert.Equal("fresh_login_required", (await stale.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("detail").GetString());
            using var staleDevice = await client.PostAsJsonAsync("/api/access/kiosk-devices", new CreateKioskDeviceRequest("Halle 2"), Ct);
            Assert.Equal(HttpStatusCode.Forbidden, staleDevice.StatusCode);
            using var staleTheme = await client.PutAsJsonAsync("/api/branding/theme", BrandingThemeTests.Hoedl, Ct);
            Assert.Equal(HttpStatusCode.Forbidden, staleTheme.StatusCode);
            var policy = await client.GetFromJsonAsync<LoginPolicyResponse>("/api/access/policy", Ct);
            using var stalePolicy = await client.PutAsJsonAsync("/api/access/policy", new LoginPolicyRequest(policy!.MagicLink, policy.Passkey, policy.Kiosk, policy.DisabledProviderKeys, policy.ForcedProviderKeys, 90), Ct);
            Assert.Equal(HttpStatusCode.Forbidden, stalePolicy.StatusCode);

            // Erneute Anmeldung liefert eine frische Sitzung: die Aktion gelingt und steht im Prüfprotokoll.
            var renewed = await pg.IssueSessionAsync(_t.Id, _t.Admin, Ct);
            using var freshClient = pg.ClientWithSession(renewed);
            using var accepted = await freshClient.PostAsJsonAsync("/api/access/role-codes", new IssueRoleCodeRequest("editor", null), Ct);
            Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
            var audit = await freshClient.GetFromJsonAsync<List<AuditEntryResponse>>("/api/audit", Ct);
            Assert.Contains(audit!, a => a.Action == "access.role_code.issued" && a.Detail == "editor");

            // Programm-Manager stellt nur Botschaftercodes aus (Zugang 2.3).
            using var manager = pg.ClientWithSession(await pg.IssueSessionAsync(_t.Id, _t.Manager, Ct));
            using var forbidden = await manager.PostAsJsonAsync("/api/access/role-codes", new IssueRoleCodeRequest("tenant_admin", null), Ct);
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
            using var ambassador = await manager.PostAsJsonAsync("/api/access/role-codes", new IssueRoleCodeRequest("health_ambassador", null), Ct);
            Assert.Equal(HttpStatusCode.Created, ambassador.StatusCode);
        }
        finally
        {
            pg.Clock.Set(start);
        }
    }

    [Fact]
    public async Task Erster_Tenant_Admin_entsteht_ueber_Rollencode_mit_Klarname_und_Passkey_und_aktiviert_den_Tenant()
    {
        var tenant = await pg.CreateScratchTenantAsync($"Neu {Guid.NewGuid():N}", Ct);
        // Der Operator stellt den ersten Tenant-Admin-Code aus (Organisation 1.4); hier direkt im Tenant-Kontext ohne Person.
        var (entry, code) = CompanyHero.Modules.Identity.Domain.RoleCode.Issue(tenant, "tenant_admin", null, pg.Clock.GetUtcNow());
        await pg.Api.Services.GetRequiredService<ITenantScopeFactory>().RunAsync(TenantContext.ForTenant(tenant), async (sp, ct) =>
        {
            await using var tx = await sp.GetRequiredService<CompanyHero.Platform.Data.IContextTransaction>().BeginAsync(ct);
            var db = sp.GetRequiredService<CompanyHero.Modules.Identity.Infrastructure.IdentityDbContext>();
            db.RoleCodes.Add(entry);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }, Ct);

        using var browser = pg.Browser();
        using var authenticator = new SoftwareAuthenticator();
        var preview = await browser.GetJsonAsync<JoinPreviewResponse>($"/api/join/role/{code}", Ct);
        Assert.Equal("tenant_admin", preview.Role);
        var ceremony = await browser.PostJsonAsync<PasskeyCeremonyResponse>($"/api/join/role/{code}/passkey-options", new JoinPasskeyOptionsRequest("Erste Admin"), Ct);

        // Ohne E-Mail oder Anbieter und ohne Passkey keine privilegierte Rolle (Zugang 2.3).
        using var incomplete = await browser.PostAsync($"/api/join/role/{code}", new RoleJoinRequest("Erste Admin", VisibilityDto.Company, null, "erste@example.org", false), Ct);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, incomplete.StatusCode);

        var join = await browser.PostJsonAsync<JoinResponse>($"/api/join/role/{code}", new RoleJoinRequest("Erste Admin", VisibilityDto.Company, new PasskeyAnswerRequest(ceremony.State, authenticator.CreateAttestation(ceremony.Options), "Laptop"), "erste@example.org", false), Ct, HttpStatusCode.Created);
        Assert.Equal(SessionKindDto.Privileged, join.SessionKind);
        var me = await browser.GetJsonAsync<JsonElement>("/api/me", Ct);
        Assert.Equal("Erste Admin", me.GetProperty("displayName").GetString());
        Assert.Contains("tenant_admin", me.GetProperty("roles").EnumerateArray().Select(r => r.GetString()));
        Assert.Equal(tenant.ToString(), me.GetProperty("tenantId").GetString());
    }
}
