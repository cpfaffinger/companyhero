using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CompanyHero.Modules.Branding.Api;
using CompanyHero.Modules.Challenges.Api;
using CompanyHero.Modules.Identity.Api;
using CompanyHero.Modules.Challenges.Application;
using CompanyHero.Modules.Challenges.Domain;
using CompanyHero.Modules.Organisation.Domain;
using CompanyHero.Platform.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyHero.Integration.Tests;

/// <summary>
/// A-009 Nachweis „echte API-Antworten durch den generierten Client prüfen“: Diese Tests nehmen echte Antworten der API
/// (Testcontainer-PostgreSQL, Laufzeitrolle) als Vertragsproben unter <c>src/frontend/e2e/fixtures/api/</c> auf. Die
/// eingecheckten Proben müssen in Struktur und Werttypen mit der laufenden API übereinstimmen; das Frontend prüft dieselben
/// Proben gegen den generierten Client (Typprüfung und Laufzeitprüfung) und verwendet sie als Antworten der Ende-zu-Ende-Tests.
/// Aktualisieren: Umgebungsvariable <c>CH_WRITE_SAMPLES=1</c> setzen und diesen Test ausführen.
/// </summary>
[Collection(PostgresTests.Name)]
public sealed class ContractSamplesTests(PostgresFixture pg)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static string SamplesDir => Path.Combine(RepositoryRoot.Find().FullName, "src", "frontend", "e2e", "fixtures", "api");

    [Fact]
    public async Task Vertragsproben_stimmen_in_Struktur_und_Werttypen_mit_der_laufenden_API_ueberein()
    {
        var wiesner = await pg.CreateScratchTenantWithMembersAsync("Wiesner Probe");
        var hoedl = await pg.CreateScratchTenantWithMembersAsync("Hödl Probe");
        // Dritter Tenant ohne veröffentlichtes Theme: Standard-Theme der Plattform mit dem Anzeigenamen des Tenants (A-013).
        var standard = await pg.CreateScratchTenantWithMembersAsync("Standard Probe");
        var scopes = pg.Api.Services.GetRequiredService<ITenantScopeFactory>();

        using var wiesnerAdmin = Client(wiesner.Id, wiesner.Admin);
        using var hoedlAdmin = Client(hoedl.Id, hoedl.Admin);
        using var member = Client(wiesner.Id, wiesner.MemberA);
        using var anonymous = pg.Api.CreateClient();

        (await wiesnerAdmin.PutAsJsonAsync("/api/branding/theme", BrandingThemeTests.Wiesner, Ct)).EnsureSuccessStatusCode();
        (await hoedlAdmin.PutAsJsonAsync("/api/branding/theme", BrandingThemeTests.Hoedl, Ct)).EnsureSuccessStatusCode();

        // Kickoff-Challenge (Challenges 4.3): Sammelziel mit Häkchen, ganze Firma; drei Beiträge, Kollektivstand durch den Worker.
        var now = pg.Clock.GetUtcNow();
        var challenge = await scopes.RunAsync(TenantContext.ForPerson(wiesner.Id, wiesner.Manager, [Role.Member, Role.ProgrammeManager]), (sp, ct) =>
            sp.GetRequiredService<IChallengeCatalog>().StartRunningAsync("Rad oder Fuß zur Arbeit", ChallengeMetric.Checkmark, 25m, now.AddDays(-12), now.AddDays(9), ct), Ct);
        using var memberB = Client(wiesner.Id, wiesner.MemberB);
        foreach (var (client, daysAgo) in new[] { (member, 1), (member, 2), (memberB, 1) })
        {
            using var created = await client.PostAsJsonAsync($"/api/challenges/{challenge:D}/contributions", new ContributionRequest("1", now.AddDays(-daysAgo), "mobile", Guid.CreateVersion7().ToString("D"), null), Ct);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }

        var control = new TestJobControl { SubscriberPerson = wiesner.MemberA };
        using var worker = WorkerHost.Create(pg, control, "w-proben");
        await worker.StartAsync(Ct);
        await TestJobControl.WaitUntilAsync(() => member.GetFromJsonAsync<CollectiveResponse>($"/api/challenges/{challenge:D}/collective", Ct).Result!.Percent == 12, TimeSpan.FromSeconds(20), Ct);
        await worker.StopAsync(Ct);

        // Zugang (Stufe 4): Sitzung, Beitrittsvorschau, Anmeldewege, Kiosk-Gerät und -Anmeldung, Anbieterliste.
        var joinCode = await AccessJoinTests.CreateJoinCodeAsync(pg, wiesner, Ct);
        var (kiosk, _) = await KioskTests.RegisterKioskAsync(pg, wiesner, Ct);
        using var kioskDevice = kiosk;
        var kioskJoin = await kiosk.PostJsonAsync<JoinResponse>($"/api/join/{joinCode}/kiosk", new KioskJoinRequest("Probe Kiosk", VisibilityDto.Company, "7391", null), Ct, HttpStatusCode.Created);

        // Fachpfad (Stufe 6): Feed, Fortschritt, Benachrichtigungen, Organisation, Datenschutz und Challenge-Verwaltung als Proben.
        using var wiesnerManager = Client(wiesner.Id, wiesner.Manager);
        using var checkIn = await member.PostAsJsonAsync("/api/me/check-in", new Modules.Progress.Api.CheckInRequest([Modules.Progress.Api.CheckInTileDto.Moved]), Ct);
        Assert.Equal(HttpStatusCode.Created, checkIn.StatusCode);
        using var post = await member.PostAsJsonAsync("/api/feed/posts", new Modules.Feed.Api.FeedPostRequest("Heute mit dem Rad zur Arbeit."), Ct);
        Assert.Equal(HttpStatusCode.Created, post.StatusCode);
        var firstDimension = (await member.GetFromJsonAsync<List<Modules.Organisation.Api.DimensionResponse>>("/api/organisation/dimensions", Ct))![0].DimensionId;
        using var groupCreated = await wiesnerManager.PostAsJsonAsync($"/api/organisation/dimensions/{firstDimension}/groups", new Modules.Organisation.Api.CreateGroupRequest("Wien", 12), Ct);
        Assert.Equal(HttpStatusCode.Created, groupCreated.StatusCode);
        using var subscriptionKey = System.Security.Cryptography.ECDiffieHellman.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        using var subscription = await member.PostAsJsonAsync("/api/me/notifications/subscriptions", new Modules.Notifications.Api.PushSubscriptionRequest("https://push.example/probe", System.Buffers.Text.Base64Url.EncodeToString(Modules.Notifications.Application.WebPushCrypto.ExportRaw(subscriptionKey.PublicKey)), "b2Zmb25lc29mZm9uZXM", "Handy"), Ct);
        Assert.Equal(HttpStatusCode.Created, subscription.StatusCode);
        using var feedWorker = WorkerHost.Create(pg, control, "w-proben-feed");
        await feedWorker.StartAsync(Ct);
        await Stufe6.WaitForQueueAsync(pg, wiesner.Id, Ct);
        await feedWorker.StopAsync(Ct);

        var samples = new Dictionary<string, HttpResponseMessage>(StringComparer.Ordinal)
        {
            ["feed"] = await member.GetAsync("/api/feed", Ct),
            ["feed-post-created"] = await member.PostAsJsonAsync("/api/feed/posts", new Modules.Feed.Api.FeedPostRequest("Zweiter Beitrag."), Ct),
            ["me-progress"] = await member.GetAsync("/api/me/progress", Ct),
            ["check-in-already"] = await member.PostAsJsonAsync("/api/me/check-in", new Modules.Progress.Api.CheckInRequest([Modules.Progress.Api.CheckInTileDto.Rested]), Ct),
            ["participation"] = await wiesnerAdmin.GetAsync("/api/progress/participation?period=month", Ct),
            ["notifications"] = await member.GetAsync("/api/notifications", Ct),
            ["me-notifications"] = await member.GetAsync("/api/me/notifications", Ct),
            ["me-notification-subscriptions"] = await member.GetAsync("/api/me/notifications/subscriptions", Ct),
            ["vapid"] = await member.GetAsync("/api/notifications/vapid", Ct),
            ["notifications-tenant"] = await wiesnerAdmin.GetAsync("/api/notifications/tenant", Ct),
            ["dimensions"] = await member.GetAsync("/api/organisation/dimensions", Ct),
            ["me-groups"] = await member.GetAsync("/api/me/groups", Ct),
            ["tenant"] = await wiesnerAdmin.GetAsync("/api/organisation/tenant", Ct),
            ["me-visibility"] = await member.GetAsync("/api/me/visibility", Ct),
            ["me-consents"] = await member.GetAsync("/api/me/consents", Ct),
            ["challenges-manage"] = await wiesnerManager.GetAsync("/api/challenges/manage", Ct),
            ["challenge-kickoff"] = await wiesnerManager.PostAsync(new Uri("/api/challenges/kickoff", UriKind.Relative), null, Ct),
            ["session"] = await member.GetAsync("/api/auth/session", Ct),
            ["session-none"] = await anonymous.GetAsync("/api/auth/session", Ct),
            ["join-preview"] = await anonymous.GetAsync($"/api/join/{joinCode}", Ct),
            ["providers"] = await anonymous.GetAsync($"/api/auth/providers?tenant={wiesner.Id}", Ct),
            ["me-access"] = await member.GetAsync("/api/me/access", Ct),
            ["kiosk-device"] = await kiosk.GetAsync("/api/kiosk/device", Ct),
            ["kiosk-join"] = await kiosk.PostAsync($"/api/join/{joinCode}/kiosk", new KioskJoinRequest("Probe Kiosk Zwei", VisibilityDto.Team, "4826", null), Ct),
            ["kiosk-login"] = await kiosk.PostAsync("/api/kiosk/login", new KioskLoginRequest(kioskJoin.KioskId, "7391"), Ct),
            ["kiosk-login-rejected"] = await kiosk.PostAsync("/api/kiosk/login", new KioskLoginRequest(kioskJoin.KioskId, "0000"), Ct),
            ["access-policy"] = await wiesnerAdmin.GetAsync("/api/access/policy", Ct),
            ["theme-wiesner"] = await member.GetAsync("/api/branding/theme", Ct),
            ["theme-hoedl"] = await hoedlAdmin.GetAsync("/api/branding/theme", Ct),
            ["theme-standard"] = await Client(standard.Id, standard.MemberA).GetAsync("/api/branding/theme", Ct),
            ["platform"] = await anonymous.GetAsync("/api/branding/platform", Ct),
            ["manifest-wiesner"] = await member.GetAsync($"/api/branding/tenants/{wiesner.Id}/manifest.webmanifest", Ct),
            ["challenges"] = await member.GetAsync("/api/challenges", Ct),
            ["collective"] = await member.GetAsync($"/api/challenges/{challenge:D}/collective", Ct),
            ["contribution-created"] = await member.PostAsJsonAsync($"/api/challenges/{challenge:D}/contributions", new ContributionRequest("1", now, "mobile", Guid.CreateVersion7().ToString("D"), null), Ct),
            ["contribution-rejected"] = await member.PostAsJsonAsync($"/api/challenges/{challenge:D}/contributions", new ContributionRequest("1", now.AddHours(2), "mobile", Guid.CreateVersion7().ToString("D"), null), Ct),
            ["theme-rejected"] = await wiesnerAdmin.PutAsJsonAsync("/api/branding/theme", BrandingThemeTests.Wiesner with { Saatfarbe = "rot" }, Ct),
        };

        Assert.Equal(HttpStatusCode.Created, samples["contribution-created"].StatusCode);
        Assert.Equal(HttpStatusCode.OK, samples["feed"].StatusCode);
        Assert.Equal(HttpStatusCode.Created, samples["feed-post-created"].StatusCode);
        Assert.Equal(HttpStatusCode.OK, samples["me-progress"].StatusCode);
        Assert.Equal(HttpStatusCode.OK, samples["check-in-already"].StatusCode);
        Assert.Equal(HttpStatusCode.OK, samples["notifications"].StatusCode);
        Assert.Equal(HttpStatusCode.Created, samples["challenge-kickoff"].StatusCode);
        Assert.Equal(HttpStatusCode.OK, samples["session"].StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, samples["session-none"].StatusCode);
        Assert.Equal(HttpStatusCode.OK, samples["join-preview"].StatusCode);
        Assert.Equal(HttpStatusCode.OK, samples["me-access"].StatusCode);
        Assert.Equal(HttpStatusCode.Created, samples["kiosk-join"].StatusCode);
        Assert.Equal(HttpStatusCode.OK, samples["kiosk-login"].StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, samples["kiosk-login-rejected"].StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, samples["contribution-rejected"].StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, samples["theme-rejected"].StatusCode);

        var write = Environment.GetEnvironmentVariable("CH_WRITE_SAMPLES") == "1";
        var problems = new List<string>();
        foreach (var (name, response) in samples)
        {
            var body = await response.Content.ReadAsStringAsync(Ct);
            var live = body.Length == 0 ? null : JsonNode.Parse(body);
            var envelope = new JsonObject
            {
                ["status"] = (int)response.StatusCode,
                ["contentType"] = response.Content.Headers.ContentType?.MediaType,
                ["body"] = live,
            };
            var path = Path.Combine(SamplesDir, name + ".json");
            if (write)
            {
                Directory.CreateDirectory(SamplesDir);
                await File.WriteAllTextAsync(path, envelope.ToJsonString(Pretty).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n", new UTF8Encoding(false), Ct);
                continue;
            }

            if (!File.Exists(path))
            {
                problems.Add($"{name}: Probe fehlt ({path}); mit CH_WRITE_SAMPLES=1 aufnehmen.");
                continue;
            }

            var checkedIn = JsonNode.Parse(await File.ReadAllTextAsync(path, Ct))!;
            Compare(name, checkedIn, envelope, problems);
            response.Dispose();
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    /// <summary>Struktur- und Typvergleich: gleiche Schlüssel, gleiche JSON-Werttypen, rekursiv; Werte dürfen abweichen.</summary>
    private static void Compare(string path, JsonNode? expected, JsonNode? actual, List<string> problems)
    {
        switch (expected, actual)
        {
            case (JsonObject e, JsonObject a):
                foreach (var key in e.Select(p => p.Key).Union(a.Select(p => p.Key), StringComparer.Ordinal))
                {
                    if (!e.ContainsKey(key))
                    {
                        problems.Add($"{path}.{key}: neu in der API, fehlt in der Probe");
                    }
                    else if (!a.ContainsKey(key))
                    {
                        problems.Add($"{path}.{key}: in der Probe, fehlt in der API");
                    }
                    else if (IsDynamicMap(key))
                    {
                        Compare($"{path}.{key}", e[key] is JsonObject ? new JsonObject() : e[key], a[key] is JsonObject ? new JsonObject() : a[key], problems);
                    }
                    else
                    {
                        Compare($"{path}.{key}", e[key], a[key], problems);
                    }
                }

                break;
            case (JsonArray e, JsonArray a):
                if (e.Count > 0 && a.Count > 0)
                {
                    Compare($"{path}[0]", e[0], a[0], problems);
                }
                else if (e.Count != a.Count)
                {
                    problems.Add($"{path}: Liste leer in {(e.Count == 0 ? "Probe" : "API")}, gefüllt in der anderen");
                }

                break;
            case (JsonValue e, JsonValue a):
                var et = e.GetValueKind();
                var at = a.GetValueKind();
                if (et != at)
                {
                    problems.Add($"{path}: Werttyp {et} in der Probe, {at} in der API");
                }

                break;
            case (null, null):
                break;
            default:
                if (expected?.GetValueKind() != actual?.GetValueKind())
                {
                    problems.Add($"{path}: {expected?.GetValueKind()} in der Probe, {actual?.GetValueKind()} in der API");
                }

                break;
        }
    }

    /// <summary>Wörterbücher mit Rollen- oder Textschlüsseln: Schlüsselmenge ist Inhalt, nicht Struktur.</summary>
    private static bool IsDynamicMap(string key) => key is "hell" or "dunkel" or "texte" or "errors";

    private HttpClient Client(TenantId tenant, PersonId person) => pg.Client(tenant, person);
}
