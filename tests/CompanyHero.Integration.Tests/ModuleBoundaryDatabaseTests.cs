using CompanyHero.Modules.Identity.Infrastructure;
using CompanyHero.Modules.Organisation.Infrastructure;
using CompanyHero.Platform.Modules;
using CompanyHero.Platform.Data;
using CompanyHero.Platform.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace CompanyHero.Integration.Tests;

/// <summary>
/// Domänenkarte 7 und Backend 11.9: Datenbanktests scheitern, wenn ein Modul ein fremdes Schema liest oder schreibt;
/// Schemata belegen, dass kein fremder Zugriff existiert (Herauslösung eines Moduls als Trockenübung).
/// </summary>
[Collection(PostgresTests.Name)]
public sealed class ModuleBoundaryDatabaseTests(PostgresFixture pg)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static IEnumerable<string> ModuleSchemasWithoutPlatform => ModuleSchemas.All.Where(s => s != ModuleSchemas.Platform);

    [Fact]
    public async Task Jede_Tabelle_eines_Modulschemas_hat_Row_Level_Security_mit_FORCE_und_Policy()
    {
        await using var conn = new NpgsqlConnection(pg.SuperuserDatabaseConnectionString);
        await conn.OpenAsync(Ct);
        await using var cmd = new NpgsqlCommand(
            """
            select n.nspname, c.relname, c.relrowsecurity, c.relforcerowsecurity,
                   (select count(*) from pg_policy p where p.polrelid = c.oid) as policies
            from pg_class c join pg_namespace n on n.oid = c.relnamespace
            where c.relkind = 'r' and n.nspname = any(@schemas) and c.relname <> '__ef_migrations'
            order by 1, 2
            """, conn);
        cmd.Parameters.AddWithValue("schemas", ModuleSchemasWithoutPlatform.ToArray());

        var tables = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(Ct);
        while (await reader.ReadAsync(Ct))
        {
            var table = $"{reader.GetString(0)}.{reader.GetString(1)}";
            tables.Add(table);
            Assert.True(reader.GetBoolean(2), $"{table}: RLS nicht aktiviert");
            Assert.True(reader.GetBoolean(3), $"{table}: RLS nicht erzwungen (FORCE)");
            Assert.True(reader.GetInt64(4) >= 1, $"{table}: keine Policy");
        }

        Assert.Equal(
            [
                "branding.tenant_theme", "challenges.challenge", "challenges.collective_state", "challenges.contribution", "challenges.contribution_key", "challenges.domain_event",
                "entitlements.entitlement", "entitlements.entitlement_history", "entitlements.tenant_limit",
                "feed.feed_entry",
                "identity.email_login", "identity.external_login", "identity.external_provider", "identity.identity_index", "identity.join_code", "identity.kiosk_credential", "identity.kiosk_device",
                "identity.kiosk_failed_attempt", "identity.login_policy", "identity.magic_link", "identity.passkey", "identity.person", "identity.recovery_code", "identity.role_code", "identity.session", "identity.transfer_link",
                "metering.billing_period", "metering.daily_aggregate", "metering.invoice_draft", "metering.ledger_event", "metering.price_plan_version",
                "notifications.aushang", "notifications.challenge_snapshot", "notifications.delivery", "notifications.notification_entry", "notifications.person_settings", "notifications.push_subscription", "notifications.tenant_settings",
                "organisation.group_dimension", "organisation.group_membership", "organisation.headcount", "organisation.member_group", "organisation.membership", "organisation.organisation", "organisation.role_assignment",
                "privacy.audit_entry", "privacy.consent_entry", "privacy.security_event", "privacy.visibility_setting",
                "progress.activity_event", "progress.badge_award", "progress.check_in", "progress.domain_event", "progress.person_progress",
            ],
            tables);
    }

    [Fact]
    public async Task Kein_Fremdschluessel_ueberschreitet_eine_Schemagrenze()
    {
        await using var conn = new NpgsqlConnection(pg.SuperuserDatabaseConnectionString);
        await conn.OpenAsync(Ct);
        await using var cmd = new NpgsqlCommand(
            """
            select count(*)
            from pg_constraint k
            join pg_class c on c.oid = k.conrelid join pg_namespace n on n.oid = c.relnamespace
            join pg_class pc on pc.oid = k.confrelid join pg_namespace pn on pn.oid = pc.relnamespace
            where k.contype = 'f' and n.nspname <> pn.nspname
            """, conn);

        Assert.Equal(0L, (long)(await cmd.ExecuteScalarAsync(Ct))!);
    }

    [Fact]
    public async Task Jedes_Modulschema_fuehrt_seine_eigene_Migrationshistorie_ohne_Rechte_der_Laufzeitrolle()
    {
        await using var conn = new NpgsqlConnection(pg.SuperuserDatabaseConnectionString);
        await conn.OpenAsync(Ct);

        foreach (var schema in new[] { ModuleSchemas.Platform, ModuleSchemas.Organisation, ModuleSchemas.Identity, ModuleSchemas.Privacy, ModuleSchemas.Progress })
        {
            await using var rows = new NpgsqlCommand($"select count(*) from \"{schema}\".\"__ef_migrations\"", conn);
            Assert.True((long)(await rows.ExecuteScalarAsync(Ct))! >= 1, $"{schema}: keine Migrationshistorie");

            await using var privilege = new NpgsqlCommand($"select has_table_privilege('ch_app', '\"{schema}\".\"__ef_migrations\"', 'SELECT')", conn);
            Assert.False((bool)(await privilege.ExecuteScalarAsync(Ct))!, $"{schema}: Laufzeitrolle darf die Historie nicht lesen");
        }
    }

    [Fact]
    public async Task Modulkontext_lehnt_SQL_gegen_ein_fremdes_Schema_ab()
    {
        var scopes = pg.Api.Services.GetRequiredService<ITenantScopeFactory>();
        await scopes.RunAsync(TenantContext.ForTenant(pg.Tenants.WiesnerId), async (sp, ct) =>
        {
            var identity = sp.GetRequiredService<IdentityDbContext>();
            await using var tx = await sp.GetRequiredService<IContextTransaction>().BeginAsync(ct);

            await Assert.ThrowsAsync<SchemaBoundaryViolationException>(() =>
                identity.Database.SqlQueryRaw<int>("select count(*)::int as \"Value\" from organisation.membership").ToListAsync(ct));
            await Assert.ThrowsAsync<SchemaBoundaryViolationException>(() =>
                identity.Database.ExecuteSqlRawAsync("delete from \"progress\".\"activity_event\"", ct));

            // Das eigene Schema bleibt erreichbar.
            var own = await identity.Database.SqlQueryRaw<int>("select count(*)::int as \"Value\" from identity.person").SingleAsync(ct);
            Assert.Equal(TwoTenants.WiesnerMembers, own);
        }, Ct);
    }

    [Fact]
    public async Task Plattformkontext_sieht_Organisationen_aber_keine_tenantbezogenen_Zeilen()
    {
        var scopes = pg.Api.Services.GetRequiredService<ITenantScopeFactory>();
        var (organisations, memberships, persons) = await scopes.RunAsync(TenantContext.ForPlatform(), async (sp, ct) =>
        {
            var organisation = sp.GetRequiredService<OrganisationDbContext>();
            await using var tx = await sp.GetRequiredService<IContextTransaction>().BeginAsync(ct);
            var organisations = await organisation.Organisations.CountAsync(ct);
            var memberships = await organisation.Memberships.CountAsync(ct);

            var identity = sp.GetRequiredService<IdentityDbContext>();
            await using var tx2 = await sp.GetRequiredService<IContextTransaction>().BeginAsync(ct);
            var persons = await identity.Persons.CountAsync(ct);
            return (organisations, memberships, persons);
        }, Ct);

        Assert.True(organisations >= 3, "Operator und zwei Tenants erwartet");
        Assert.Equal(0, memberships);
        Assert.Equal(0, persons);
    }

    [Fact]
    public async Task Tenantkontext_sieht_nur_die_eigene_Organisation()
    {
        var scopes = pg.Api.Services.GetRequiredService<ITenantScopeFactory>();
        var visible = await scopes.RunAsync(TenantContext.ForTenant(pg.Tenants.HoedlId), async (sp, ct) =>
        {
            var db = sp.GetRequiredService<OrganisationDbContext>();
            await using var tx = await sp.GetRequiredService<IContextTransaction>().BeginAsync(ct);
            return await db.Organisations.Select(o => o.Id).ToListAsync(ct);
        }, Ct);

        Assert.Equal([pg.Tenants.HoedlId.Value], visible);
    }
}
