using CompanyHero.Modules.Identity.Infrastructure;
using CompanyHero.Platform.Data;
using CompanyHero.Platform.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace CompanyHero.Integration.Tests;

/// <summary>
/// Backend 5.1 Nr. 2: Worker setzen für jeden Job einen neuen Kontext. Die Scope-Fabrik ist der Baustein, den der
/// Worker der Stufe 3 je Job verwendet; hier wird ihr Verhalten mit nacheinander laufenden Jobs zweier Tenants belegt.
/// </summary>
[Collection(PostgresTests.Name)]
public sealed class JobContextTests(PostgresFixture pg)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Jeder_Job_erhaelt_einen_eigenen_Kontext_auf_derselben_Poolverbindung()
    {
        var pooled = new NpgsqlConnectionStringBuilder(pg.AppConnectionString) { MaxPoolSize = 1, MinPoolSize = 1 }.ConnectionString;
        using var api = pg.CreateApi(pooled);
        var scopes = api.Services.GetRequiredService<ITenantScopeFactory>();

        var sequence = new List<(TenantId Tenant, int Persons)>();
        foreach (var tenant in new[] { pg.Tenants.WiesnerId, pg.Tenants.HoedlId, pg.Tenants.WiesnerId, pg.Tenants.HoedlId })
        {
            var count = await scopes.RunAsync(TenantContext.ForTenant(tenant), async (sp, ct) =>
            {
                Assert.Same(sp.GetRequiredService<ITenantContextAccessor>().Current, sp.GetRequiredService<ITenantContextAccessor>().Require());
                var db = sp.GetRequiredService<IdentityDbContext>();
                await using var tx = await sp.GetRequiredService<IContextTransaction>().BeginAsync(ct);
                var tenants = await db.Persons.Select(p => p.TenantId).Distinct().ToListAsync(ct);
                Assert.Equal([tenant], tenants);
                return await db.Persons.CountAsync(ct);
            }, Ct);
            sequence.Add((tenant, count));
        }

        Assert.Equal(
            [(pg.Tenants.WiesnerId, TwoTenants.WiesnerMembers), (pg.Tenants.HoedlId, TwoTenants.HoedlMembers), (pg.Tenants.WiesnerId, TwoTenants.WiesnerMembers), (pg.Tenants.HoedlId, TwoTenants.HoedlMembers)],
            sequence);
    }

    [Fact]
    public async Task Der_Kontext_eines_Jobs_existiert_nach_dem_Job_nicht_mehr()
    {
        var scopes = pg.Api.Services.GetRequiredService<ITenantScopeFactory>();

        IServiceProvider? leaked = null;
        await scopes.RunAsync(TenantContext.ForTenant(pg.Tenants.WiesnerId), (sp, _) =>
        {
            leaked = sp;
            return Task.CompletedTask;
        }, Ct);

        // Der Scope ist entsorgt; ein neuer Scope hat keinen Kontext.
        Assert.NotNull(leaked);
        Assert.Throws<ObjectDisposedException>(() => leaked.GetRequiredService<ITenantContextAccessor>());
        await using var fresh = pg.Api.Services.CreateAsyncScope();
        Assert.Null(fresh.ServiceProvider.GetRequiredService<ITenantContextAccessor>().Current);
    }

    [Fact]
    public async Task Ein_Job_ohne_Person_hat_keine_Rollen_und_keine_Person()
    {
        var scopes = pg.Api.Services.GetRequiredService<ITenantScopeFactory>();
        await scopes.RunAsync(TenantContext.ForTenant(pg.Tenants.WiesnerId), (sp, _) =>
        {
            var context = sp.GetRequiredService<ITenantContextAccessor>().Require();
            Assert.Equal(TenantContextKind.Tenant, context.Kind);
            Assert.Null(context.PersonId);
            Assert.Empty(context.Roles);
            Assert.Throws<TenantContextMissingException>(() => context.RequirePerson());
            return Task.CompletedTask;
        }, Ct);
    }
}
