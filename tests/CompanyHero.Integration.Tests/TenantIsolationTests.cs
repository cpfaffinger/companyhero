using CompanyHero.Modules.Identity.Domain;
using CompanyHero.Modules.Identity.Infrastructure;
using CompanyHero.Modules.Organisation.Infrastructure;
using CompanyHero.Platform.Data;
using CompanyHero.Platform.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace CompanyHero.Integration.Tests;

/// <summary>
/// Backend 11.1: tenantfremde Lese- und Schreibzugriffe einschließlich Raw SQL und Beziehungen scheitern.
/// Backend 11.2: fehlender Kontext, Pool-Wiederverwendung, Rollback und Wiederholung verhalten sich wie festgelegt.
/// Alle Tests laufen mit der Laufzeitrolle ch_app (kein Besitz, kein BYPASSRLS).
/// </summary>
[Collection(PostgresTests.Name)]
public sealed class TenantIsolationTests(PostgresFixture pg)
{
    private const string RlsViolation = "42501";
    private const string ForeignKeyViolation = "23503";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ITenantScopeFactory Scopes => pg.Api.Services.GetRequiredService<ITenantScopeFactory>();

    [Fact]
    public async Task Lesen_im_Kontext_eines_Tenants_liefert_nur_dessen_Zeilen()
    {
        var wiesner = await Scopes.RunAsync(TenantContext.ForTenant(pg.Tenants.WiesnerId), async (sp, ct) =>
        {
            var db = sp.GetRequiredService<IdentityDbContext>();
            await using var tx = await sp.GetRequiredService<IContextTransaction>().BeginAsync(ct);
            return await db.Persons.Select(p => p.TenantId).ToListAsync(ct);
        }, Ct);

        var hoedl = await Scopes.RunAsync(TenantContext.ForTenant(pg.Tenants.HoedlId), async (sp, ct) =>
        {
            var db = sp.GetRequiredService<IdentityDbContext>();
            await using var tx = await sp.GetRequiredService<IContextTransaction>().BeginAsync(ct);
            return await db.Persons.Select(p => p.TenantId).ToListAsync(ct);
        }, Ct);

        Assert.Equal(TwoTenants.WiesnerMembers, wiesner.Count);
        Assert.All(wiesner, t => Assert.Equal(pg.Tenants.WiesnerId, t));
        Assert.Equal(TwoTenants.HoedlMembers, hoedl.Count);
        Assert.All(hoedl, t => Assert.Equal(pg.Tenants.HoedlId, t));
    }

    [Fact]
    public async Task Raw_SQL_im_Kontext_A_sieht_keine_Zeilen_von_B_auch_nicht_mit_explizitem_Filter()
    {
        var (all, filtered) = await Scopes.RunAsync(TenantContext.ForTenant(pg.Tenants.WiesnerId), async (sp, ct) =>
        {
            var db = sp.GetRequiredService<IdentityDbContext>();
            await using var tx = await sp.GetRequiredService<IContextTransaction>().BeginAsync(ct);
            var all = await db.Database.SqlQueryRaw<Guid>("select tenant_id as \"Value\" from identity.person").ToListAsync(ct);
            var filtered = await db.Database
                .SqlQueryRaw<Guid>("select id as \"Value\" from identity.person where tenant_id = {0}", pg.Tenants.HoedlId.Value)
                .ToListAsync(ct);
            return (all, filtered);
        }, Ct);

        Assert.Equal(TwoTenants.WiesnerMembers, all.Count);
        Assert.All(all, t => Assert.Equal(pg.Tenants.WiesnerId.Value, t));
        Assert.Empty(filtered);
    }

    [Fact]
    public async Task Schreiben_mit_fremder_tenant_id_wird_von_Anwendung_und_Datenbank_abgelehnt()
    {
        await Scopes.RunAsync(TenantContext.ForTenant(pg.Tenants.WiesnerId), async (sp, ct) =>
        {
            var db = sp.GetRequiredService<IdentityDbContext>();

            // Anwendungsebene: Entität eines anderen Tenants im Kontext von Wiesner.
            await using (var tx = await sp.GetRequiredService<IContextTransaction>().BeginAsync(ct))
            {
                db.Persons.Add(Person.Create(pg.Tenants.HoedlId, "Eindringling", pg.Clock.GetUtcNow()));
                await Assert.ThrowsAsync<TenantMismatchException>(() => db.SaveChangesAsync(ct));
                db.ChangeTracker.Clear();
            }

            // Datenbankebene: Raw SQL an der Anwendung vorbei; Row Level Security lehnt ab.
            await using (var tx = await sp.GetRequiredService<IContextTransaction>().BeginAsync(ct))
            {
                var ex = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
                    "insert into identity.person (tenant_id, id, display_name, created_at) values ({0}, {1}, 'Eindringling', now())",
                    [pg.Tenants.HoedlId.Value, Guid.CreateVersion7()],
                    ct));
                Assert.Equal(RlsViolation, ex.SqlState);
            }
        }, Ct);

        var hoedlCount = await CountPersonsAsync(pg.Tenants.HoedlId);
        Assert.Equal(TwoTenants.HoedlMembers, hoedlCount);
    }

    [Fact]
    public async Task Update_und_Delete_fremder_Zeilen_treffen_keine_Zeile()
    {
        var (updated, deleted) = await Scopes.RunAsync(TenantContext.ForTenant(pg.Tenants.WiesnerId), async (sp, ct) =>
        {
            var db = sp.GetRequiredService<IdentityDbContext>();
            await using var tx = await sp.GetRequiredService<IContextTransaction>().BeginAsync(ct);
            var updated = await db.Database.ExecuteSqlRawAsync(
                "update identity.person set display_name = display_name || '!' where tenant_id = {0}", [pg.Tenants.HoedlId.Value], ct);
            var deleted = await db.Database.ExecuteSqlRawAsync(
                "delete from identity.person where tenant_id = {0}", [pg.Tenants.HoedlId.Value], ct);
            await tx.CommitAsync(ct);
            return (updated, deleted);
        }, Ct);

        Assert.Equal(0, updated);
        Assert.Equal(0, deleted);
        Assert.Equal(TwoTenants.HoedlMembers, await CountPersonsAsync(pg.Tenants.HoedlId));
    }

    [Fact]
    public async Task Beziehung_ueber_die_Tenantgrenze_scheitert_am_zusammengesetzten_Fremdschluessel()
    {
        await Scopes.RunAsync(TenantContext.ForTenant(pg.Tenants.WiesnerId), async (sp, ct) =>
        {
            var db = sp.GetRequiredService<OrganisationDbContext>();

            // Rolle im Tenant Wiesner für eine Person aus Hödl: (tenant_id, person_id) existiert dort nicht als Mitgliedschaft.
            await using (var tx = await sp.GetRequiredService<IContextTransaction>().BeginAsync(ct))
            {
                var ex = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
                    "insert into organisation.role_assignment (tenant_id, id, person_id, role, assigned_at) values ({0}, {1}, {2}, 'member', now())",
                    [pg.Tenants.WiesnerId.Value, Guid.CreateVersion7(), pg.Tenants.HoedlMia.Value],
                    ct));
                Assert.Equal(ForeignKeyViolation, ex.SqlState);
            }

            // Rolle mit fremder tenant_id: Row Level Security lehnt ab, bevor der Fremdschlüssel geprüft wird.
            await using (var tx = await sp.GetRequiredService<IContextTransaction>().BeginAsync(ct))
            {
                var ex = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
                    "insert into organisation.role_assignment (tenant_id, id, person_id, role, assigned_at) values ({0}, {1}, {2}, 'tenant_admin', now())",
                    [pg.Tenants.HoedlId.Value, Guid.CreateVersion7(), pg.Tenants.HoedlMia.Value],
                    ct));
                Assert.Equal(RlsViolation, ex.SqlState);
            }

            // Navigation über die Beziehung liefert nur Rollen des eigenen Tenants.
            await using (var tx = await sp.GetRequiredService<IContextTransaction>().BeginAsync(ct))
            {
                var memberships = await db.Memberships.Include(m => m.Roles).ToListAsync(ct);
                Assert.Equal(TwoTenants.WiesnerMembers, memberships.Count);
                Assert.All(memberships.SelectMany(m => m.Roles), r => Assert.Equal(pg.Tenants.WiesnerId, r.TenantId));
            }
        }, Ct);
    }

    [Fact]
    public async Task Ohne_Kontext_wird_jede_Operation_abgelehnt_statt_unbeschraenkt_zu_lesen()
    {
        // Anwendungsebene: Scope ohne Kontext (etwa ein Job, der keinen gesetzt hat).
        await using (var scope = pg.Api.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            await Assert.ThrowsAsync<TenantContextMissingException>(() => db.Persons.ToListAsync(Ct));
            await Assert.ThrowsAsync<TenantContextMissingException>(async () => await scope.ServiceProvider.GetRequiredService<IContextTransaction>().BeginAsync(Ct));
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await db.Database.BeginTransactionAsync(Ct));
        }

        // Datenbankebene: Laufzeitrolle ohne set_config sieht nichts und schreibt nichts.
        await using var conn = new NpgsqlConnection(pg.AppConnectionString);
        await conn.OpenAsync(Ct);
        await using var count = new NpgsqlCommand("select count(*) from identity.person", conn);
        Assert.Equal(0L, (long)(await count.ExecuteScalarAsync(Ct))!);

        await using var insert = new NpgsqlCommand("insert into identity.person (tenant_id, id, display_name, created_at) values (@t, @i, 'Niemand', now())", conn);
        insert.Parameters.AddWithValue("t", pg.Tenants.WiesnerId.Value);
        insert.Parameters.AddWithValue("i", Guid.CreateVersion7());
        var ex = await Assert.ThrowsAsync<PostgresException>(() => insert.ExecuteNonQueryAsync(Ct));
        Assert.Equal(RlsViolation, ex.SqlState);
    }

    [Fact]
    public async Task Lesen_ausserhalb_der_Kontexttransaktion_wird_abgelehnt()
    {
        await Scopes.RunAsync(TenantContext.ForTenant(pg.Tenants.WiesnerId), async (sp, ct) =>
        {
            var db = sp.GetRequiredService<IdentityDbContext>();
            await Assert.ThrowsAsync<TenantContextMissingException>(() => db.Persons.ToListAsync(ct));
        }, Ct);
    }

    [Fact]
    public async Task Kontext_endet_mit_der_Transaktion_und_bleibt_nicht_auf_der_Poolverbindung()
    {
        // Poolgröße 1: jede Verbindung dieses Hosts ist dieselbe physische Verbindung.
        var pooled = new NpgsqlConnectionStringBuilder(pg.AppConnectionString) { MaxPoolSize = 1, MinPoolSize = 1 }.ConnectionString;
        using var api = pg.CreateApi(pooled);
        var scopes = api.Services.GetRequiredService<ITenantScopeFactory>();

        var wiesner = await scopes.RunAsync(TenantContext.ForTenant(pg.Tenants.WiesnerId), async (sp, ct) =>
        {
            var db = sp.GetRequiredService<IdentityDbContext>();
            await using var tx = await sp.GetRequiredService<IContextTransaction>().BeginAsync(ct);
            var setting = await db.Database.SqlQueryRaw<string>("select current_setting('app.tenant_id', true) as \"Value\"").SingleAsync(ct);
            Assert.Equal(pg.Tenants.WiesnerId.ToString(), setting);
            var count = await db.Persons.CountAsync(ct);
            await tx.CommitAsync(ct);
            return count;
        }, Ct);
        Assert.Equal(TwoTenants.WiesnerMembers, wiesner);

        // Dieselbe physische Verbindung, nächster Nutzer: kein Kontext mehr.
        await using (var raw = new NpgsqlConnection(pooled))
        {
            await raw.OpenAsync(Ct);
            await using var setting = new NpgsqlCommand("select coalesce(current_setting('app.tenant_id', true), '')", raw);
            Assert.Equal(string.Empty, (string)(await setting.ExecuteScalarAsync(Ct))!);
            await using var count = new NpgsqlCommand("select count(*) from identity.person", raw);
            Assert.Equal(0L, (long)(await count.ExecuteScalarAsync(Ct))!);
        }

        var hoedl = await scopes.RunAsync(TenantContext.ForTenant(pg.Tenants.HoedlId), async (sp, ct) =>
        {
            var db = sp.GetRequiredService<IdentityDbContext>();
            await using var tx = await sp.GetRequiredService<IContextTransaction>().BeginAsync(ct);
            return await db.Persons.Select(p => p.TenantId).ToListAsync(ct);
        }, Ct);
        Assert.Equal(TwoTenants.HoedlMembers, hoedl.Count);
        Assert.All(hoedl, t => Assert.Equal(pg.Tenants.HoedlId, t));
    }

    [Fact]
    public async Task Rollback_verwirft_die_Aenderung_und_die_naechste_Transaktion_erhaelt_frischen_Kontext()
    {
        await Scopes.RunAsync(TenantContext.ForTenant(pg.Tenants.WiesnerId), async (sp, ct) =>
        {
            var db = sp.GetRequiredService<IdentityDbContext>();
            var name = $"Rollback {Guid.NewGuid():N}";

            await using (var tx = await sp.GetRequiredService<IContextTransaction>().BeginAsync(ct))
            {
                db.Persons.Add(Person.Create(pg.Tenants.WiesnerId, name, pg.Clock.GetUtcNow()));
                await db.SaveChangesAsync(ct);
                await tx.RollbackAsync(ct);
            }

            db.ChangeTracker.Clear();
            await using (var tx = await sp.GetRequiredService<IContextTransaction>().BeginAsync(ct))
            {
                var setting = await db.Database.SqlQueryRaw<string>("select current_setting('app.tenant_id', true) as \"Value\"").SingleAsync(ct);
                Assert.Equal(pg.Tenants.WiesnerId.ToString(), setting);
                Assert.False(await db.Persons.AnyAsync(p => p.DisplayName == name, ct));
            }
        }, Ct);
    }

    [Fact]
    public async Task Wiederholung_nach_Transaktionsfehler_baut_Transaktion_und_Kontext_gemeinsam_neu_auf()
    {
        var name = $"Retry {Guid.NewGuid():N}";
        var attempts = 0;
        var scratch = await pg.CreateScratchTenantAsync("Wiederholung GmbH", Ct);

        await Scopes.RunAsync(TenantContext.ForTenant(scratch), async (sp, ct) =>
        {
            var db = sp.GetRequiredService<IdentityDbContext>();
            var strategy = new ProbeExecutionStrategy(db);

            await strategy.ExecuteAsync(async () =>
            {
                attempts++;
                await using var tx = await sp.GetRequiredService<IContextTransaction>().BeginAsync(ct);
                var setting = await db.Database.SqlQueryRaw<string>("select current_setting('app.tenant_id', true) as \"Value\"").SingleAsync(ct);
                Assert.Equal(scratch.ToString(), setting);

                db.ChangeTracker.Clear();
                db.Persons.Add(Person.Create(scratch, name, pg.Clock.GetUtcNow()));
                await db.SaveChangesAsync(ct);

                if (attempts == 1)
                {
                    throw new ProbeFailureException();
                }

                await tx.CommitAsync(ct);
            });

            await using (var tx = await sp.GetRequiredService<IContextTransaction>().BeginAsync(ct))
            {
                Assert.Equal(1, await db.Persons.CountAsync(p => p.DisplayName == name, ct));
            }
        }, Ct);

        Assert.Equal(2, attempts);
    }

    private Task<int> CountPersonsAsync(TenantId tenant) =>
        Scopes.RunAsync(TenantContext.ForTenant(tenant), async (sp, ct) =>
        {
            var db = sp.GetRequiredService<IdentityDbContext>();
            await using var tx = await sp.GetRequiredService<IContextTransaction>().BeginAsync(ct);
            return await db.Persons.CountAsync(ct);
        }, Ct);

    /// <summary>Simuliert einen vorübergehenden Fehler, den die Ausführungsstrategie wiederholt.</summary>
    private sealed class ProbeFailureException : Exception
    {
        public ProbeFailureException()
            : base("Simulierter Transaktionsfehler")
        {
        }
    }

    private sealed class ProbeExecutionStrategy(DbContext context) : ExecutionStrategy(context, maxRetryCount: 3, maxRetryDelay: TimeSpan.FromMilliseconds(10))
    {
        protected override bool ShouldRetryOn(Exception exception) => exception is ProbeFailureException;
    }
}
