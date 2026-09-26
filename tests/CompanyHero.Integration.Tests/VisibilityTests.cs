using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CompanyHero.Modules.Progress.Application;
using CompanyHero.Platform.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyHero.Integration.Tests;

/// <summary>
/// Backend 11.3 und Domänenkarte 7: Ein Tenant-Admin erhält trotz gleicher tenant_id aus keiner Domäne individuelle
/// Aktivitätswerte anderer Personen. Datenschutz 3.3: die restriktivere Einstellung gewinnt.
/// </summary>
[Collection(PostgresTests.Name)]
public sealed class VisibilityTests(PostgresFixture pg)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Tenant_Admin_erhaelt_keine_Aktivitaeten_anderer_Personen_trotz_gleicher_tenant_id()
    {
        using var admin = Client(pg.Tenants.WiesnerId, pg.Tenants.WiesnerAdmin);

        // Bea hat „Ganze Firma“ gewählt, Cem „Nur für mich“: für den Arbeitgeber gibt es beides nicht (A-021 Regel 1).
        using var bea = await admin.GetAsync(Activities(pg.Tenants.WiesnerBea), Ct);
        using var cem = await admin.GetAsync(Activities(pg.Tenants.WiesnerCem), Ct);

        Assert.Equal(HttpStatusCode.NotFound, bea.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, cem.StatusCode);
    }

    [Fact]
    public async Task Programm_Manager_erhaelt_ebenfalls_keine_Aktivitaeten_anderer_Personen()
    {
        using var manager = Client(pg.Tenants.WiesnerId, pg.Tenants.WiesnerManager);
        using var response = await manager.GetAsync(Activities(pg.Tenants.WiesnerBea), Ct);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Tenant_Admin_erhaelt_auch_ueber_die_Anwendungsfunktion_keine_fremden_Aktivitaeten()
    {
        var scopes = pg.Api.Services.GetRequiredService<ITenantScopeFactory>();
        var context = TenantContext.ForPerson(pg.Tenants.WiesnerId, pg.Tenants.WiesnerAdmin, ["member", "tenant_admin"]);

        var result = await scopes.RunAsync(context, (sp, ct) =>
            sp.GetRequiredService<IPersonalActivityQuery>().GetForPersonAsync(pg.Tenants.WiesnerBea, ct), Ct);

        Assert.Null(result);
    }

    [Fact]
    public async Task Mitglied_sieht_Aktivitaeten_bei_Ganze_Firma_und_nicht_bei_Nur_fuer_mich()
    {
        using var cem = Client(pg.Tenants.WiesnerId, pg.Tenants.WiesnerCem);
        using var bea = Client(pg.Tenants.WiesnerId, pg.Tenants.WiesnerBea);

        var beaSeenByCem = await cem.GetFromJsonAsync<List<JsonElement>>(Activities(pg.Tenants.WiesnerBea), Ct);
        using var cemSeenByBea = await bea.GetAsync(Activities(pg.Tenants.WiesnerCem), Ct);

        Assert.NotNull(beaSeenByCem);
        Assert.Equal(TwoTenants.BeaActivities, beaSeenByCem.Count);
        Assert.Equal(HttpStatusCode.NotFound, cemSeenByBea.StatusCode);
    }

    [Fact]
    public async Task Person_sieht_ihre_eigenen_Aktivitaeten_immer()
    {
        using var cem = Client(pg.Tenants.WiesnerId, pg.Tenants.WiesnerCem);
        var own = await cem.GetFromJsonAsync<List<JsonElement>>(Activities(pg.Tenants.WiesnerCem), Ct);

        Assert.NotNull(own);
        Assert.Equal(TwoTenants.CemActivities, own.Count);
    }

    [Fact]
    public async Task Mitgliederliste_nur_fuer_den_Tenant_Admin_und_ohne_Aktivitaetsdaten()
    {
        using var admin = Client(pg.Tenants.WiesnerId, pg.Tenants.WiesnerAdmin);
        using var member = Client(pg.Tenants.WiesnerId, pg.Tenants.WiesnerBea);

        using var list = await admin.GetAsync(new Uri("/api/members", UriKind.Relative), Ct);
        using var denied = await member.GetAsync(new Uri("/api/members", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        var json = await list.Content.ReadAsStringAsync(Ct);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(TwoTenants.WiesnerMembers, doc.RootElement.GetArrayLength());
        foreach (var entry in doc.RootElement.EnumerateArray())
        {
            var fields = entry.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToList();
            Assert.Equal(["displayName", "groups", "joinedAt", "personId", "roles"], fields);
        }

        Assert.DoesNotContain("activit", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("check_in", json, StringComparison.Ordinal);
    }

    private HttpClient Client(TenantId tenant, PersonId person) => pg.Client(tenant, person);

    private static Uri Activities(PersonId person) => new($"/api/persons/{person}/activities", UriKind.Relative);
}
