using System.Net;
using System.Net.Http.Json;
using CompanyHero.Modules.Challenges.Api;
using CompanyHero.Modules.Feed.Api;
using CompanyHero.Modules.Notifications.Api;
using CompanyHero.Modules.Organisation.Api;
using CompanyHero.Modules.Organisation.Domain;
using CompanyHero.Modules.Privacy.Domain;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyHero.Integration.Tests;

/// <summary>
/// Datenschutz 7, 9.10 und Backend 5.1 für alle Routen der Stufe 6 mit Kennung: eine Person eines fremden Tenants mit allen Rollen
/// erhält auf Objekte eines anderen Tenants „nicht gefunden“, nie „verboten“ und nie Daten. Jede Route mit Kennung braucht einen Fall,
/// sonst schlägt der Test fehl. Dazu die Rechtematrix der Verwaltungsrouten.
/// </summary>
[Collection(PostgresTests.Name)]
public sealed class Stufe6IsolationTests(PostgresFixture pg) : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ScratchTenant _w = null!;
    private ScratchTenant _h = null!;

    public async ValueTask InitializeAsync()
    {
        _w = await pg.CreateScratchTenantWithMembersAsync($"Iso W {Guid.NewGuid():N}");
        _h = await pg.CreateScratchTenantWithMembersAsync($"Iso H {Guid.NewGuid():N}");
        await Stufe6.AssignRolesAsync(pg, _h.Id, _h.Admin, Ct, Role.ProgrammeManager, Role.Insight, Role.HealthAmbassador);
        await pg.IssueSessionAsync(_h.Id, _h.Admin, Ct);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Jede_Route_mit_Kennung_antwortet_fremdem_Tenant_mit_nicht_gefunden()
    {
        using var wManager = pg.Client(_w.Id, _w.Manager);
        using var wMember = pg.Client(_w.Id, _w.MemberA);
        using var wAdmin = pg.Client(_w.Id, _w.Admin);

        // Objekte des Tenants W.
        var challenge = await Stufe6.PlanChallengeAsync(wManager, pg.Clock.GetUtcNow(), "Isolation", 4m, Ct);
        var draft = (await (await wManager.PostAsync(new Uri("/api/challenges/kickoff", UriKind.Relative), null, Ct)).Content.ReadFromJsonAsync<ChallengeCardResponse>(Stufe6.Json, Ct))!.ChallengeId;
        using var post = await wMember.PostAsJsonAsync("/api/feed/posts", new FeedPostRequest("W"), Ct);
        var entry = (await post.Content.ReadFromJsonAsync<FeedPostResponse>(Stufe6.Json, Ct))!.Id;
        using var sub = await wMember.PostAsJsonAsync("/api/me/notifications/subscriptions", new PushSubscriptionRequest("https://push.test/iso", "AAAA", "BBBB", "W"), Ct);
        var subscription = (await sub.Content.ReadFromJsonAsync<PushSubscriptionResponse>(Stufe6.Json, Ct))!.Id;
        var dimension = (await Stufe6.GetAsync<List<DimensionResponse>>(wMember, "/api/organisation/dimensions", Ct))[0].DimensionId;
        using var groupResponse = await wManager.PostAsJsonAsync($"/api/organisation/dimensions/{dimension}/groups", new CreateGroupRequest("W-Gruppe", 9), Ct);
        var group = (await groupResponse.Content.ReadFromJsonAsync<CreatedResponse>(Stufe6.Json, Ct))!.Id;
        var joinCode = await AccessJoinTests.CreateJoinCodeAsync(pg, _w, Ct);
        var joinCodeId = (await Stufe6.GetAsync<List<Modules.Identity.Api.JoinCodeResponse>>(wAdmin, "/api/access/join-codes", Ct)).Single(c => c.Code == joinCode).JoinCodeId;
        var (kiosk, deviceId) = await KioskTests.RegisterKioskAsync(pg, _w, Ct);
        kiosk.Dispose();
        var now = pg.Clock.GetUtcNow();
        var contribution = Guid.CreateVersion7().ToString("D");
        var notification = Guid.CreateVersion7().ToString("D");

        var cases = new Dictionary<string, (HttpMethod Method, string Path, object? Body)>(StringComparer.Ordinal)
        {
            ["/api/challenges/{challengeId:guid}"] = (HttpMethod.Get, $"/api/challenges/{challenge:D}", null),
            ["/api/challenges/{challengeId:guid}/preview"] = (HttpMethod.Post, $"/api/challenges/{draft}/preview", null),
            ["/api/challenges/{challengeId:guid}/plan"] = (HttpMethod.Post, $"/api/challenges/{draft}/plan", null),
            ["/api/challenges/{challengeId:guid}/end"] = (HttpMethod.Post, $"/api/challenges/{challenge:D}/end", new EndChallengeRequest("fremd")),
            ["/api/challenges/{challengeId:guid}/texts"] = (HttpMethod.Put, $"/api/challenges/{challenge:D}/texts", new ChallengeTextsRequest("fremd", null)),
            ["/api/challenges/{challengeId:guid}/contributions"] = (HttpMethod.Post, $"/api/challenges/{challenge:D}/contributions", new ContributionRequest("1", now, "mobile", Guid.CreateVersion7().ToString("D"), null)),
            ["/api/challenges/{challengeId:guid}/contributions/mine"] = (HttpMethod.Get, $"/api/challenges/{challenge:D}/contributions/mine", null),
            ["/api/challenges/{challengeId:guid}/contributions/{contributionId:guid}/reverse"] = (HttpMethod.Post, $"/api/challenges/{challenge:D}/contributions/{contribution}/reverse", null),
            ["/api/challenges/{challengeId:guid}/collective"] = (HttpMethod.Get, $"/api/challenges/{challenge:D}/collective", null),
            ["/api/feed/posts/{entryId:guid}"] = (HttpMethod.Delete, $"/api/feed/posts/{entry}", null),
            ["/api/notifications/{entryId:guid}/read"] = (HttpMethod.Post, $"/api/notifications/{notification}/read", null),
            ["/api/me/notifications/subscriptions/{subscriptionId:guid}"] = (HttpMethod.Delete, $"/api/me/notifications/subscriptions/{subscription}", null),
            ["/api/organisation/dimensions/{dimensionId:guid}"] = (HttpMethod.Put, $"/api/organisation/dimensions/{dimension}", new DimensionRequest("fremd", true)),
            ["/api/organisation/dimensions/{dimensionId:guid}/groups"] = (HttpMethod.Post, $"/api/organisation/dimensions/{dimension}/groups", new CreateGroupRequest("fremd", null)),
            ["/api/organisation/groups/{groupId:guid}/archive"] = (HttpMethod.Post, $"/api/organisation/groups/{group}/archive", null),
            ["/api/organisation/groups/{groupId:guid}/headcount"] = (HttpMethod.Post, $"/api/organisation/groups/{group}/headcount", new HeadcountRequest(new DateOnly(2026, 9, 1), 5)),
            ["/api/access/members/{personId:guid}/remove"] = (HttpMethod.Post, $"/api/access/members/{_w.MemberA}/remove", null),
            ["/api/persons/{personId:guid}/activities"] = (HttpMethod.Get, $"/api/persons/{_w.MemberA}/activities", null),
            ["/api/access/join-codes/{id:guid}/revoke"] = (HttpMethod.Post, $"/api/access/join-codes/{joinCodeId}/revoke", null),
            ["/api/access/kiosk-devices/{id:guid}/revoke"] = (HttpMethod.Post, $"/api/access/kiosk-devices/{deviceId}/revoke", null),
            ["/api/access/providers/{id:guid}"] = (HttpMethod.Delete, $"/api/access/providers/{Guid.CreateVersion7():D}", null),
            ["/api/me/passkeys/{passkeyId:guid}"] = (HttpMethod.Delete, $"/api/me/passkeys/{Guid.CreateVersion7():D}", null),
            ["/api/me/providers/{linkId:guid}"] = (HttpMethod.Delete, $"/api/me/providers/{Guid.CreateVersion7():D}", null),
        };

        // Kiosk-only-Routen und Routen ohne Fachobjekt (Beitrittscodes ohne Sitzung, Icons) sind hier nicht gemeint.
        string[] excluded = ["/api/challenges/{challengeId:guid}/contribution-operations", "/api/branding/tenants/{tenantId:guid}/icons/{name}", "/api/branding/tenants/{tenantId:guid}/theme.css", "/api/branding/tenants/{tenantId:guid}/manifest.webmanifest"];
        var routes = pg.Api.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Select(e => e.RoutePattern.RawText!)
            .Where(r => r.Contains(":guid}", StringComparison.Ordinal) && !excluded.Contains(r, StringComparer.Ordinal))
            .Distinct()
            .ToList();
        var missing = routes.Where(r => !cases.ContainsKey(r)).ToList();
        Assert.True(missing.Count == 0, "Routen ohne Isolationsfall: " + string.Join(", ", missing));

        using var foreign = pg.Client(_h.Id, _h.Admin);
        var problems = new List<string>();
        foreach (var (route, (method, path, body)) in cases)
        {
            using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
            if (body is not null)
            {
                request.Content = JsonContent.Create(body);
            }

            using var response = await foreign.SendAsync(request, Ct);
            if (response.StatusCode != HttpStatusCode.NotFound)
            {
                problems.Add($"{method} {route}: {(int)response.StatusCode}");
            }
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public async Task Rechtematrix_der_Verwaltungsrouten()
    {
        using var member = pg.Client(_w.Id, _w.MemberA);
        using var manager = pg.Client(_w.Id, _w.Manager);
        using var admin = pg.Client(_w.Id, _w.Admin);
        var dimension = (await Stufe6.GetAsync<List<DimensionResponse>>(member, "/api/organisation/dimensions", Ct))[0].DimensionId;

        var matrix = new (string Name, Func<HttpClient, Task<HttpResponseMessage>> Call, HttpClient Client, HttpStatusCode Expected)[]
        {
            ("Mitglied: Verwaltung Challenges", c => c.GetAsync(new Uri("/api/challenges/manage", UriKind.Relative), Ct), member, HttpStatusCode.Forbidden),
            ("Programm-Manager: Verwaltung Challenges", c => c.GetAsync(new Uri("/api/challenges/manage", UriKind.Relative), Ct), manager, HttpStatusCode.OK),
            ("Tenant-Admin: Verwaltung Challenges", c => c.GetAsync(new Uri("/api/challenges/manage", UriKind.Relative), Ct), admin, HttpStatusCode.OK),
            ("Mitglied: Tenant-Schalter lesen", c => c.GetAsync(new Uri("/api/notifications/tenant", UriKind.Relative), Ct), member, HttpStatusCode.Forbidden),
            ("Programm-Manager: Tenant-Schalter lesen", c => c.GetAsync(new Uri("/api/notifications/tenant", UriKind.Relative), Ct), manager, HttpStatusCode.OK),
            ("Mitglied: Beteiligung", c => c.GetAsync(new Uri("/api/progress/participation?period=month", UriKind.Relative), Ct), member, HttpStatusCode.Forbidden),
            ("Programm-Manager: Beteiligung", c => c.GetAsync(new Uri("/api/progress/participation?period=month", UriKind.Relative), Ct), manager, HttpStatusCode.OK),
            ("Mitglied: Aushang", c => c.GetAsync(new Uri("/api/notifications/aushang", UriKind.Relative), Ct), member, HttpStatusCode.Forbidden),
            ("Programm-Manager: Zeitzone", c => c.PutAsJsonAsync("/api/organisation/tenant", new TenantSettingsRequest("Europe/Vienna"), Ct), manager, HttpStatusCode.Forbidden),
            ("Tenant-Admin: Zeitzone", c => c.PutAsJsonAsync("/api/organisation/tenant", new TenantSettingsRequest("Europe/Vienna"), Ct), admin, HttpStatusCode.NoContent),
            ("Mitglied: Gruppe anlegen", c => c.PostAsJsonAsync($"/api/organisation/dimensions/{dimension}/groups", new CreateGroupRequest("X", null), Ct), member, HttpStatusCode.Forbidden),
            ("Tenant-Admin: Gruppe anlegen", c => c.PostAsJsonAsync($"/api/organisation/dimensions/{dimension}/groups", new CreateGroupRequest("X", null), Ct), admin, HttpStatusCode.Created),
            ("Mitglied: Kickoff", c => c.PostAsync(new Uri("/api/challenges/kickoff", UriKind.Relative), null, Ct), member, HttpStatusCode.Forbidden),
            ("Mitglied: Mitglied entfernen", c => c.PostAsync(new Uri($"/api/access/members/{_w.MemberB}/remove", UriKind.Relative), null, Ct), member, HttpStatusCode.Forbidden),
            ("Programm-Manager: Mitglied entfernen", c => c.PostAsync(new Uri($"/api/access/members/{_w.MemberB}/remove", UriKind.Relative), null, Ct), manager, HttpStatusCode.Forbidden),
        };

        var problems = new List<string>();
        foreach (var (name, call, client, expected) in matrix)
        {
            using var response = await call(client);
            if (response.StatusCode != expected)
            {
                problems.Add($"{name}: {(int)response.StatusCode} statt {(int)expected}");
            }
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }
}
