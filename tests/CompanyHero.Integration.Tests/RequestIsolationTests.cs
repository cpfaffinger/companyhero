using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CompanyHero.Modules.Organisation.Application;
using CompanyHero.Platform.Tenancy;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyHero.Integration.Tests;

/// <summary>
/// Backend 5.1 Nr. 1 und 2, 11.2 (parallele Requests); Datenschutz 7 und 9.10: Für jede Route erzeugt eine Sitzung
/// von Tenant A auf Ressourcen von Tenant B „nicht gefunden“, nicht „verboten“.
/// </summary>
[Collection(PostgresTests.Name)]
public sealed class RequestIsolationTests(PostgresFixture pg)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Parallele_Requests_zweier_Tenants_vermischen_keine_Kontexte()
    {
        using var wiesner = Client(pg.Tenants.WiesnerId, pg.Tenants.WiesnerBea);
        using var hoedl = Client(pg.Tenants.HoedlId, pg.Tenants.HoedlMia);

        var tasks = Enumerable.Range(0, 40).Select(i => i % 2 == 0
            ? Fetch(wiesner, pg.Tenants.WiesnerId, pg.Tenants.WiesnerBea, "Bea", "Wiesner")
            : Fetch(hoedl, pg.Tenants.HoedlId, pg.Tenants.HoedlMia, "Mia", "Hödl"));

        await Task.WhenAll(tasks);

        static async Task Fetch(HttpClient client, TenantId tenant, PersonId person, string name, string tenantName)
        {
            var me = await client.GetFromJsonAsync<JsonElement>(new Uri("/api/me", UriKind.Relative), Ct);
            Assert.Equal(tenant.ToString(), me.GetProperty("tenantId").GetString());
            Assert.Equal(person.ToString(), me.GetProperty("personId").GetString());
            Assert.Equal(name, me.GetProperty("displayName").GetString());
            Assert.Equal(tenantName, me.GetProperty("tenantName").GetString());
        }
    }

    [Fact]
    public async Task Jede_Route_mit_Personenbezug_antwortet_fremdem_Tenant_mit_nicht_gefunden()
    {
        var routes = pg.Api.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText is not null && e.RoutePattern.Parameters.Any(p => p.Name == "personId"))
            .Select(e => e.RoutePattern.RawText!)
            .ToList();

        Assert.NotEmpty(routes);

        using var wiesnerMember = Client(pg.Tenants.WiesnerId, pg.Tenants.WiesnerBea);
        using var wiesnerAdmin = Client(pg.Tenants.WiesnerId, pg.Tenants.WiesnerAdmin);
        using var hoedlMember = Client(pg.Tenants.HoedlId, pg.Tenants.HoedlMia);

        foreach (var route in routes)
        {
            var foreign = new Uri(route.Replace("{personId:guid}", pg.Tenants.HoedlMia.ToString(), StringComparison.Ordinal), UriKind.Relative);

            using var asMember = await wiesnerMember.GetAsync(foreign, Ct);
            using var asAdmin = await wiesnerAdmin.GetAsync(foreign, Ct);
            using var asOwnTenant = await hoedlMember.GetAsync(foreign, Ct);

            Assert.Equal(HttpStatusCode.NotFound, asMember.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, asAdmin.StatusCode);
            Assert.Equal(HttpStatusCode.OK, asOwnTenant.StatusCode);
        }
    }

    [Fact]
    public async Task Ohne_Sitzung_gibt_es_keinen_Kontext_und_keine_Daten()
    {
        using var anonymous = pg.Api.CreateClient();
        using var me = await anonymous.GetAsync(new Uri("/api/me", UriKind.Relative), Ct);
        using var members = await anonymous.GetAsync(new Uri("/api/members", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, members.StatusCode);
    }

    [Fact]
    public async Task Sitzung_mit_Person_eines_anderen_Tenants_ergibt_keinen_Kontext()
    {
        // Die Sitzung benennt Tenant Wiesner, die Person gehört zu Hödl: keine Mitgliedschaft, kein Kontext.
        using var forged = Client(pg.Tenants.WiesnerId, pg.Tenants.HoedlMia);
        using var me = await forged.GetAsync(new Uri("/api/me", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Fact]
    public async Task Rollen_kommen_aus_Organisation_und_nicht_aus_der_Sitzung()
    {
        // Die Test-Sitzung kann keine Rollen behaupten; /api/me zeigt die Rollen laut Organisation.
        using var admin = Client(pg.Tenants.WiesnerId, pg.Tenants.WiesnerAdmin);
        using var member = Client(pg.Tenants.WiesnerId, pg.Tenants.WiesnerBea);

        var adminMe = await admin.GetFromJsonAsync<JsonElement>(new Uri("/api/me", UriKind.Relative), Ct);
        var memberMe = await member.GetFromJsonAsync<JsonElement>(new Uri("/api/me", UriKind.Relative), Ct);

        Assert.Equal(["member", "tenant_admin"], adminMe.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).ToList());
        Assert.Equal(["member"], memberMe.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).ToList());
    }

    [Fact]
    public async Task Eingerichteter_Tenant_ohne_Tenant_Admin_gewaehrt_noch_keinen_Zugang()
    {
        var scopes = pg.Api.Services.GetRequiredService<ITenantScopeFactory>();
        var tenant = await scopes.RunAsync(TenantContext.ForPlatform(), (sp, ct) =>
            sp.GetRequiredService<IOrganisationDirectory>().CreateTenantAsync("Eingerichtet GmbH", pg.Tenants.OperatorId, ct), Ct);
        var person = await scopes.RunAsync(TenantContext.ForTenant(tenant), async (sp, ct) =>
        {
            var id = await sp.GetRequiredService<Modules.Identity.Application.IPersonDirectory>().CreatePersonAsync("Erste Person", ct);
            await sp.GetRequiredService<IOrganisationDirectory>().AddMemberAsync(id, ct);
            return id;
        }, Ct);

        using var client = Client(tenant, person);
        using var me = await client.GetAsync(new Uri("/api/me", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);

        await scopes.RunAsync(TenantContext.ForTenant(tenant), (sp, ct) =>
            sp.GetRequiredService<IOrganisationDirectory>().AssignRoleAsync(person, "tenant_admin", ct), Ct);

        using var meAfter = await client.GetAsync(new Uri("/api/me", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.OK, meAfter.StatusCode);
    }

    private HttpClient Client(TenantId tenant, PersonId person)
    {
        var client = pg.Api.CreateClient();
        client.DefaultRequestHeaders.Add(TestSessionHandler.Header, TestSession.For(tenant, person));
        return client;
    }
}
