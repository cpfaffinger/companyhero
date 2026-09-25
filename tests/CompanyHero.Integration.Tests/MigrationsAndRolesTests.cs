using CompanyHero.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace CompanyHero.Integration.Tests;

/// <summary>Nachweise Backend 5.1 Nr. 4 (Rollen) und Betrieb 9.4 (Migration vor Anwendungsstart).</summary>
[Collection(PostgresTests.Name)]
public sealed class MigrationsAndRolesTests(PostgresFixture pg)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Migrationslauf_endet_erfolgreich()
    {
        Assert.Equal(0, pg.MigrationExitCode);
    }

    [Fact]
    public async Task Laufzeitrolle_hat_weder_Superuser_noch_BypassRls_noch_Besitz()
    {
        await using var conn = new NpgsqlConnection(pg.SuperuserConnectionString);
        await conn.OpenAsync(Ct);

        await using var roleCmd = new NpgsqlCommand(
            "select rolsuper, rolbypassrls, rolcreatedb, rolcreaterole from pg_roles where rolname = 'ch_app'", conn);
        await using (var reader = await roleCmd.ExecuteReaderAsync(Ct))
        {
            Assert.True(await reader.ReadAsync(Ct), "Rolle ch_app fehlt");
            Assert.False(reader.GetBoolean(0), "ch_app darf kein Superuser sein");
            Assert.False(reader.GetBoolean(1), "ch_app darf RLS nicht umgehen");
            Assert.False(reader.GetBoolean(2));
            Assert.False(reader.GetBoolean(3));
        }

        await using var ownedCmd = new NpgsqlCommand("select count(*) from pg_tables where tableowner = 'ch_app'", conn);
        Assert.Equal(0L, (long)(await ownedCmd.ExecuteScalarAsync(Ct))!);
    }

    [Fact]
    public async Task Laufzeitrolle_kann_Daten_lesen_und_schreiben_aber_keine_Objekte_anlegen()
    {
        await using var conn = new NpgsqlConnection(pg.AppConnectionString);
        await conn.OpenAsync(Ct);

        await using var canUse = new NpgsqlCommand("select has_schema_privilege('ch_app', 'platform', 'USAGE')", conn);
        Assert.True((bool)(await canUse.ExecuteScalarAsync(Ct))!, "USAGE auf Schema platform fehlt");

        await using var canCreate = new NpgsqlCommand("select has_schema_privilege('ch_app', 'platform', 'CREATE')", conn);
        Assert.False((bool)(await canCreate.ExecuteScalarAsync(Ct))!, "ch_app darf keine Objekte anlegen");

        await using var canSelect = new NpgsqlCommand("select has_table_privilege('ch_app', 'platform.release', 'SELECT')", conn);
        Assert.True((bool)(await canSelect.ExecuteScalarAsync(Ct))!, "SELECT auf platform.release fehlt");

        await using var canInsert = new NpgsqlCommand("select has_table_privilege('ch_app', 'platform.release', 'INSERT')", conn);
        Assert.True((bool)(await canInsert.ExecuteScalarAsync(Ct))!, "INSERT auf platform.release fehlt");

        await using var createTable = new NpgsqlCommand("create table platform.darf_nicht (id int)", conn);
        await Assert.ThrowsAsync<PostgresException>(() => createTable.ExecuteNonQueryAsync(Ct));
    }

    [Fact]
    public async Task Migrationslauf_protokolliert_den_Release_Stand()
    {
        await using var conn = new NpgsqlConnection(pg.AppConnectionString);
        await conn.OpenAsync(Ct);
        await using var cmd = new NpgsqlCommand("select count(*) from platform.release where version = @v and image_digest = 'sha256:test'", conn);
        cmd.Parameters.AddWithValue("v", PostgresFixture.ReleaseVersion);
        Assert.Equal(1L, (long)(await cmd.ExecuteScalarAsync(Ct))!);
    }

    [Fact]
    public async Task Zweiter_Migrationslauf_ist_idempotent()
    {
        var exit = await MigrationRunner.RunAsync(
            new MigrationOptions { MigratorConnectionString = pg.MigratorConnectionString, ReleaseVersion = "test-release-2" },
            NullLoggerFactory.Instance,
            Ct);

        Assert.Equal(0, exit);

        await using var conn = new NpgsqlConnection(pg.AppConnectionString);
        await conn.OpenAsync(Ct);
        await using var cmd = new NpgsqlCommand("select count(*) from platform.release where version = 'test-release-2'", conn);
        Assert.Equal(1L, (long)(await cmd.ExecuteScalarAsync(Ct))!);
    }

    [Fact]
    public async Task Fehlerhafte_Migration_bricht_ab_und_hinterlaesst_keinen_Teilzustand()
    {
        // Betrieb 9.4: Eine Migration, die scheitert, darf keinen halben Stand hinterlassen; der Deploy bricht vor dem Anwendungsstart ab.
        // Simuliert wird ein echtes SQL-Scheitern: Das Zielobjekt existiert bereits in unvereinbarer Form.
        const string db = "companyhero_defekt";
        await using (var su = new NpgsqlConnection(pg.SuperuserConnectionString))
        {
            await su.OpenAsync(Ct);
            await using var create = new NpgsqlCommand($"create database {db} owner ch_migrator", su);
            await create.ExecuteNonQueryAsync(Ct);
        }

        var defectConnection = pg.For("ch_migrator", PostgresFixture.MigratorPassword, db);
        await using (var mig = new NpgsqlConnection(defectConnection))
        {
            await mig.OpenAsync(Ct);
            await using var sabotage = new NpgsqlCommand("create schema platform; create table platform.release (kaputt text)", mig);
            await sabotage.ExecuteNonQueryAsync(Ct);
        }

        var exit = await MigrationRunner.RunAsync(
            new MigrationOptions { MigratorConnectionString = defectConnection, ReleaseVersion = "defekt" },
            NullLoggerFactory.Instance,
            Ct);

        Assert.NotEqual(0, exit);

        await using var check = new NpgsqlConnection(defectConnection);
        await check.OpenAsync(Ct);
        await using var history = new NpgsqlCommand(
            "select count(*) from information_schema.tables where table_schema = 'platform' and table_name = '__ef_migrations'", check);
        var historyTables = (long)(await history.ExecuteScalarAsync(Ct))!;
        if (historyTables == 1)
        {
            await using var applied = new NpgsqlCommand("select count(*) from platform.__ef_migrations", check);
            Assert.Equal(0L, (long)(await applied.ExecuteScalarAsync(Ct))!);
        }

        await using var columns = new NpgsqlCommand(
            "select string_agg(column_name, ',' order by column_name) from information_schema.columns where table_schema = 'platform' and table_name = 'release'", check);
        Assert.Equal("kaputt", (string)(await columns.ExecuteScalarAsync(Ct))!);
    }
}
