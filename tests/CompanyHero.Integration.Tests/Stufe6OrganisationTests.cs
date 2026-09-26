using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CompanyHero.Modules.Identity.Api;
using CompanyHero.Modules.Organisation.Api;
using CompanyHero.Modules.Organisation.Application;
using CompanyHero.Platform.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyHero.Integration.Tests;

/// <summary>Organisation 1.3, 2.1, 3, 7 (A-033 bis A-036): Tenant-Lebenszyklus, Dimensionen und Gruppen, Sollstärke, Beitritt mit Gruppenwahl.</summary>
[Collection(PostgresTests.Name)]
public sealed class Stufe6OrganisationTests(PostgresFixture pg) : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ScratchTenant _t = null!;

    public async ValueTask InitializeAsync() => _t = await pg.CreateScratchTenantWithMembersAsync($"Org {Guid.NewGuid():N}");

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Dimensionen_Gruppen_Sollstaerke_und_eigene_Gruppenwahl()
    {
        using var admin = pg.Client(_t.Id, _t.Admin);
        using var manager = pg.Client(_t.Id, _t.Manager);
        using var member = pg.Client(_t.Id, _t.MemberA);

        // Drei Dimensionen mit Standardnamen entstehen beim ersten Zugriff (Organisation 2.1).
        var dimensions = await Stufe6.GetAsync<List<DimensionResponse>>(member, "/api/organisation/dimensions", Ct);
        Assert.Equal(["Standort", "Abteilung", "Schicht"], dimensions.Select(d => d.Name).ToList());
        var standort = dimensions[0];

        // Mitglieder legen keine Gruppen an; Programm-Manager schon. Sollstärke unter fünf erzeugt die Warnung (Organisation 3.2).
        using var denied = await member.PostAsJsonAsync($"/api/organisation/dimensions/{standort.DimensionId}/groups", new CreateGroupRequest("Wien", 4), Ct);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var wienResponse = await manager.PostAsJsonAsync($"/api/organisation/dimensions/{standort.DimensionId}/groups", new CreateGroupRequest("Wien", 4), Ct);
        Assert.Equal(HttpStatusCode.Created, wienResponse.StatusCode);
        var wien = (await wienResponse.Content.ReadFromJsonAsync<CreatedResponse>(Stufe6.Json, Ct))!;
        using var grazResponse = await manager.PostAsJsonAsync($"/api/organisation/dimensions/{standort.DimensionId}/groups", new CreateGroupRequest("Graz", 12), Ct);
        var graz = (await grazResponse.Content.ReadFromJsonAsync<CreatedResponse>(Stufe6.Json, Ct))!;

        dimensions = await Stufe6.GetAsync<List<DimensionResponse>>(manager, "/api/organisation/dimensions", Ct);
        var groups = dimensions[0].Groups;
        Assert.True(groups.Single(g => g.GroupId == wien.Id).HeadcountWarning);
        Assert.False(groups.Single(g => g.GroupId == graz.Id).HeadcountWarning);
        Assert.Equal(4, groups.Single(g => g.GroupId == wien.Id).Headcount);

        // Sollstärke des Tenants mit Stichtag; Umbenennen der Dimension.
        using var headcount = await admin.PostAsJsonAsync("/api/organisation/tenant/headcount", new HeadcountRequest(new DateOnly(2026, 9, 1), 40), Ct);
        Assert.Equal(HttpStatusCode.NoContent, headcount.StatusCode);
        // Die Sollstärke sehen Funktionsrollen, Mitglieder nicht (Organisation 3.2).
        Assert.Equal(40, (await Stufe6.GetAsync<TenantResponse>(admin, "/api/organisation/tenant", Ct)).Headcount);
        Assert.Null((await Stufe6.GetAsync<TenantResponse>(member, "/api/organisation/tenant", Ct)).Headcount);
        using var rename = await manager.PutAsJsonAsync($"/api/organisation/dimensions/{standort.DimensionId}", new DimensionRequest("Werk", true), Ct);
        Assert.Equal(HttpStatusCode.NoContent, rename.StatusCode);

        // Eigene Wahl je Dimension, jederzeit änderbar; archivierte Gruppen sind nicht wählbar (Organisation 2.1).
        using var choose = await member.PutAsJsonAsync("/api/me/groups", new GroupChoiceRequest(standort.DimensionId, wien.Id), Ct);
        Assert.Equal(HttpStatusCode.NoContent, choose.StatusCode);
        var mine = await Stufe6.GetAsync<List<GroupChoiceResponse>>(member, "/api/me/groups", Ct);
        Assert.Equal(wien.Id, mine.Single(c => c.DimensionId == standort.DimensionId).GroupId);
        using var archive = await manager.PostAsync($"/api/organisation/groups/{wien.Id}/archive", null, Ct);
        Assert.Equal(HttpStatusCode.NoContent, archive.StatusCode);
        using var chooseArchived = await member.PutAsJsonAsync("/api/me/groups", new GroupChoiceRequest(standort.DimensionId, wien.Id), Ct);
        Assert.Equal(HttpStatusCode.Conflict, chooseArchived.StatusCode);
        using var change = await member.PutAsJsonAsync("/api/me/groups", new GroupChoiceRequest(standort.DimensionId, graz.Id), Ct);
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);
    }

    [Fact]
    public async Task Tenant_Zeitzone_nur_durch_Tenant_Admin_und_nur_bekannte_Zonen()
    {
        using var admin = pg.Client(_t.Id, _t.Admin);
        using var member = pg.Client(_t.Id, _t.MemberA);
        Assert.Equal("Europe/Vienna", (await Stufe6.GetAsync<TenantResponse>(member, "/api/organisation/tenant", Ct)).TimeZone);
        using var denied = await member.PutAsJsonAsync("/api/organisation/tenant", new TenantSettingsRequest("Europe/Berlin"), Ct);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var unknown = await admin.PutAsJsonAsync("/api/organisation/tenant", new TenantSettingsRequest("Mars/Olympus"), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        using var ok = await admin.PutAsJsonAsync("/api/organisation/tenant", new TenantSettingsRequest("Europe/Berlin"), Ct);
        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);
        Assert.Equal("Europe/Berlin", (await Stufe6.GetAsync<TenantResponse>(member, "/api/organisation/tenant", Ct)).TimeZone);
    }

    [Fact]
    public async Task Beitritt_mit_Gruppenwahl_am_Kiosk()
    {
        using var manager = pg.Client(_t.Id, _t.Manager);
        var dimensions = await Stufe6.GetAsync<List<DimensionResponse>>(manager, "/api/organisation/dimensions", Ct);
        using var created = await manager.PostAsJsonAsync($"/api/organisation/dimensions/{dimensions[1].DimensionId}/groups", new CreateGroupRequest("Logistik", 9), Ct);
        var logistik = (await created.Content.ReadFromJsonAsync<CreatedResponse>(Stufe6.Json, Ct))!;

        var code = await AccessJoinTests.CreateJoinCodeAsync(pg, _t, Ct);
        using var anonymous = pg.Api.CreateClient();
        var preview = await Stufe6.GetAsync<JoinPreviewResponse>(anonymous, $"/api/join/{code}", Ct);
        var abteilung = preview.Dimensions.Single(d => d.DimensionId == dimensions[1].DimensionId);
        Assert.Contains(abteilung.Groups, g => g.GroupId == logistik.Id && g.Name == "Logistik");

        var (kiosk, _) = await KioskTests.RegisterKioskAsync(pg, _t, Ct);
        using var kioskClient = kiosk;
        var joined = await kiosk.PostJsonAsync<JoinResponse>($"/api/join/{code}/kiosk", new KioskJoinRequest("Neu am Kiosk", VisibilityDto.Team, "4711", [new JoinGroupChoiceRequest(abteilung.DimensionId, logistik.Id)]), Ct, HttpStatusCode.Created);

        var groups = await Stufe6.Scopes(pg).RunAsync(TenantContext.ForTenant(_t.Id), (sp, ct) => sp.GetRequiredService<IOrganisationDirectory>().GetGroupsOfAsync(new PersonId(Guid.Parse(joined.PersonId)), ct), Ct);
        Assert.Equal(logistik.Id, groups.Single(g => g.DimensionId == Guid.Parse(abteilung.DimensionId)).GroupId!.Value.ToString("D"));

        // Fremde Gruppenkennung im Beitritt wird abgelehnt.
        using var wrong = await kiosk.PostAsync($"/api/join/{code}/kiosk", new KioskJoinRequest("Falsch", VisibilityDto.Team, "4712", [new JoinGroupChoiceRequest(abteilung.DimensionId, Guid.NewGuid().ToString("D"))]), Ct);
        Assert.True(wrong.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity or HttpStatusCode.BadRequest, wrong.StatusCode.ToString());
    }

    [Fact]
    public async Task Sperre_Kuendigung_Lesefenster_und_Loeschung_des_Tenants()
    {
        var scopes = Stufe6.Scopes(pg);
        using var member = pg.Client(_t.Id, _t.MemberA);
        using var admin = pg.Client(_t.Id, _t.Admin);

        await scopes.RunAsync(TenantContext.ForPlatform(), (sp, ct) => sp.GetRequiredService<IOrganisationDirectory>().SuspendTenantAsync(_t.Id, "Zahlungsverzug", ct), Ct);
        using (var me = await member.GetAsync(new Uri("/api/me", UriKind.Relative), Ct))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
        }

        using (var session = await member.GetAsync(new Uri("/api/auth/session", UriKind.Relative), Ct))
        {
            Assert.Equal(HttpStatusCode.Forbidden, session.StatusCode);
            Assert.Equal("tenant_suspended", await Stufe6.DetailAsync(session, Ct));
        }

        await scopes.RunAsync(TenantContext.ForPlatform(), (sp, ct) => sp.GetRequiredService<IOrganisationDirectory>().UnsuspendTenantAsync(_t.Id, ct), Ct);
        using (var me = await member.GetAsync(new Uri("/api/me", UriKind.Relative), Ct))
        {
            Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        }

        // Kündigung durch den Tenant-Admin zum Monatsende in der Tenant-Zeitzone (Organisation 1.3).
        using var terminate = await admin.PostAsync(new Uri("/api/organisation/tenant/terminate", UriKind.Relative), null, Ct);
        Assert.Equal(HttpStatusCode.NoContent, terminate.StatusCode);
        var tenant = await Stufe6.GetAsync<TenantResponse>(member, "/api/organisation/tenant", Ct);
        Assert.Equal(new DateTimeOffset(2026, 9, 30, 22, 0, 0, TimeSpan.Zero), tenant.TerminationEffectiveAt);

        var start = pg.Clock.GetUtcNow();
        try
        {
            pg.Clock.Set(tenant.TerminationEffectiveAt!.Value.AddDays(1));
            // Die Sitzungen der Testuhr sind nach dem Sprung abgelaufen (gleitendes Ende): neue Sitzungen wie nach einer Anmeldung.
            using var memberLater = pg.ClientWithSession(await pg.IssueSessionAsync(_t.Id, _t.MemberA, Ct));
            using var adminLater = pg.ClientWithSession(await pg.IssueSessionAsync(_t.Id, _t.Admin, Ct));
            using (var session = await memberLater.GetAsync(new Uri("/api/auth/session", UriKind.Relative), Ct))
            {
                Assert.Equal(HttpStatusCode.Forbidden, session.StatusCode);
                Assert.Equal("tenant_terminated", await Stufe6.DetailAsync(session, Ct));
            }

            // 90 Tage lesender Zugriff für den Tenant-Admin zum Export; Schreiben ist gesperrt.
            using (var read = await adminLater.GetAsync(new Uri("/api/organisation/tenant", UriKind.Relative), Ct))
            {
                Assert.Equal(HttpStatusCode.OK, read.StatusCode);
            }

            using (var write = await adminLater.PutAsJsonAsync("/api/organisation/tenant", new TenantSettingsRequest("Europe/Berlin"), Ct))
            {
                Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
                Assert.Equal("tenant_read_only", await Stufe6.DetailAsync(write, Ct));
            }

            // Nach der Lesefrist löscht der Lauf die Mitgliedschaften und markiert den Tenant (Datenschutz 5.2).
            pg.Clock.Set(tenant.ReadOnlyUntil!.Value.AddDays(1));
            await Stufe6.RunTaskAsync(pg.Api.Services, "privacy.retention", Ct);
            var record = await scopes.RunAsync(TenantContext.ForPlatform(), (sp, ct) => sp.GetRequiredService<IOrganisationDirectory>().GetTenantAsync(_t.Id, ct), Ct);
            Assert.Equal(Modules.Organisation.Domain.OrganisationState.Deleted, record!.Value.State);
            var members = await scopes.RunAsync(TenantContext.ForTenant(_t.Id), (sp, ct) => sp.GetRequiredService<IOrganisationDirectory>().ListActiveMemberIdsAsync(ct), Ct);
            Assert.Empty(members);
        }
        finally
        {
            pg.Clock.Set(start);
        }
    }
}
