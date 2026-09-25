using CompanyHero.Modules.Progress.Application;
using CompanyHero.Modules.Progress.Domain;
using CompanyHero.Modules.Progress.Infrastructure;
using CompanyHero.Platform.Data;
using CompanyHero.Platform.Jobs;
using CompanyHero.Platform.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace CompanyHero.Integration.Tests;

/// <summary>
/// A-006 und Backend 6, 11.5: logische Queue in PostgreSQL mit atomarer Kopplung, Leases, Wiederholungen, Dead-Letter,
/// Replay, Worker-Replikaten, Fairness und zeitgesteuerten Aufgaben. Alle Läufe mit der Laufzeitrolle.
/// </summary>
[Collection(PostgresTests.Name)]
public sealed class JobQueueTests(PostgresFixture pg) : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ScratchTenant _a = null!;
    private ScratchTenant _b = null!;

    public async ValueTask InitializeAsync()
    {
        _a = await pg.CreateScratchTenantWithMembersAsync($"Queue A {Guid.NewGuid():N}");
        _b = await pg.CreateScratchTenantWithMembersAsync($"Queue B {Guid.NewGuid():N}");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(20);

    private ITenantScopeFactory Scopes => pg.Api.Services.GetRequiredService<ITenantScopeFactory>();

    [Fact]
    public async Task Einreihung_nur_innerhalb_der_Kontexttransaktion()
    {
        await Scopes.RunAsync(TenantContext.ForTenant(_a.Id), async (sp, ct) =>
        {
            var queue = sp.GetRequiredService<IJobQueue>();
            await Assert.ThrowsAsync<TenantContextMissingException>(() => queue.EnqueueAsync(new JobRequest(EffectJobHandler.JobType, "x", "ohne-transaktion"), ct));
        }, Ct);
    }

    [Fact]
    public async Task Job_und_fachliche_Aenderung_werden_gemeinsam_dauerhaft_oder_gar_nicht()
    {
        var person = _a.MemberA;
        var rolledBack = $"{person}#rollback-{Guid.NewGuid():N}";
        var committed = $"{person}#commit-{Guid.NewGuid():N}";

        await Scopes.RunAsync(TenantContext.ForTenant(_a.Id), async (sp, ct) =>
        {
            var transaction = sp.GetRequiredService<IContextTransaction>();
            var queue = sp.GetRequiredService<IJobQueue>();
            var activities = sp.GetRequiredService<IActivityRecorder>();

            // Ohne Commit: weder Fachvorgang noch Job.
            await using (var tx = await transaction.BeginAsync(ct))
            {
                await activities.RecordAsync(person, rolledBack[..60], ActivitySource.Platform, pg.Clock.GetUtcNow(), ct);
                var result = await queue.EnqueueAsync(new JobRequest(EffectJobHandler.JobType, rolledBack, rolledBack), ct);
                Assert.Equal(EnqueueOutcome.Enqueued, result.Outcome);
                await tx.RollbackAsync(ct);
            }

            // Mit Commit: beides, in einer Transaktion.
            await using (var tx = await transaction.BeginAsync(ct))
            {
                await activities.RecordAsync(person, committed[..60], ActivitySource.Platform, pg.Clock.GetUtcNow(), ct);
                await queue.EnqueueAsync(new JobRequest(EffectJobHandler.JobType, committed, committed), ct);
                await tx.CommitAsync(ct);
            }
        }, Ct);

        Assert.Null(await JobRowAsync(rolledBack));
        Assert.Equal(0, await ActivityCountAsync(_a.Id, rolledBack[..60]));
        var row = await JobRowAsync(committed);
        Assert.NotNull(row);
        Assert.Equal(JobStatus.Queued, row.Value.Status);
        Assert.Equal(1, await ActivityCountAsync(_a.Id, committed[..60]));
    }

    [Fact]
    public async Task Gleicher_Idempotenzschluessel_erzeugt_keinen_zweiten_Job_und_Plattformjobs_haben_einen_eigenen_Namensraum()
    {
        var key = $"key-{Guid.NewGuid():N}";
        var (first, second) = await Scopes.RunAsync(TenantContext.ForTenant(_a.Id), async (sp, ct) =>
        {
            await using var tx = await sp.GetRequiredService<IContextTransaction>().BeginAsync(ct);
            var queue = sp.GetRequiredService<IJobQueue>();
            var a = await queue.EnqueueAsync(new JobRequest(EffectJobHandler.JobType, $"{_a.MemberA}#a", key), ct);
            var b = await queue.EnqueueAsync(new JobRequest(EffectJobHandler.JobType, $"{_a.MemberA}#b", key), ct);
            await tx.CommitAsync(ct);
            return (a, b);
        }, Ct);
        var other = await Scopes.RunAsync(TenantContext.ForTenant(_b.Id), async (sp, ct) =>
        {
            await using var tx = await sp.GetRequiredService<IContextTransaction>().BeginAsync(ct);
            var result = await sp.GetRequiredService<IJobQueue>().EnqueueAsync(new JobRequest(EffectJobHandler.JobType, $"{_b.MemberA}#c", key), ct);
            await tx.CommitAsync(ct);
            return result;
        }, Ct);

        Assert.Equal(EnqueueOutcome.Enqueued, first.Outcome);
        Assert.Equal(EnqueueOutcome.AlreadyQueued, second.Outcome);
        Assert.Null(second.JobId);
        Assert.Equal(EnqueueOutcome.Enqueued, other.Outcome);
    }

    [Fact]
    public async Task Worker_verarbeitet_den_Job_im_Kontext_seines_Tenants_und_bestaetigt_in_derselben_Transaktion()
    {
        var control = new TestJobControl();
        var reference = $"{_b.MemberA}#kontext-{Guid.NewGuid():N}";
        var jobId = await EnqueueAsync(_b.Id, reference);

        using var worker = WorkerHost.Create(pg, control, "w-kontext");
        await worker.StartAsync(Ct);
        await TestJobControl.WaitUntilAsync(() => control.Order.Any(o => o.Job == jobId), Wait, Ct);
        await TestJobControl.WaitUntilAsync(() => JobRowAsync(reference).Result?.Status == JobStatus.Succeeded, Wait, Ct);
        await worker.StopAsync(Ct);

        var (tenant, _, _) = control.Order.Single(o => o.Job == jobId);
        Assert.Equal(_b.Id, tenant);
        Assert.Equal(1, await ActivityCountAsync(_b.Id, EffectJobHandler.KindFor(jobId)));
        Assert.Equal(0, await ActivityCountAsync(_a.Id, EffectJobHandler.KindFor(jobId)));
        var row = await JobRowAsync(reference);
        Assert.Equal(1, row!.Value.Attempts);
        Assert.Equal("w-kontext", row.Value.WorkerId);
    }

    [Fact]
    public async Task Fehlversuche_warten_mit_wachsender_Wartezeit_bis_zum_Dead_Letter_und_Replay_ist_eine_Operator_Aktion()
    {
        var control = new TestJobControl();
        var reference = $"{_a.MemberA}#fehler-{Guid.NewGuid():N}";
        control.Fail(reference);
        var jobId = await EnqueueAsync(_a.Id, reference, maxAttempts: 3);

        using var worker = WorkerHost.Create(pg, control, "w-fehler");
        await worker.StartAsync(Ct);
        await TestJobControl.WaitUntilAsync(() => JobRowAsync(reference).Result?.Status == JobStatus.Dead, Wait, Ct);

        var dead = (await JobRowAsync(reference))!.Value;
        Assert.Equal(3, dead.Attempts);
        Assert.Equal(3, control.Entries(reference));
        Assert.StartsWith("InvalidOperationException", dead.LastError, StringComparison.Ordinal);
        // Wirkung des Handlers vor dem Fehler wurde mit jedem Versuch verworfen.
        Assert.Equal(0, await ActivityCountAsync(_a.Id, EffectJobHandler.KindFor(jobId)));

        // Replay nur im Plattformkontext (Operator), mit Begründung.
        await Scopes.RunAsync(TenantContext.ForTenant(_a.Id), async (sp, ct) =>
            await Assert.ThrowsAsync<TenantContextMissingException>(() => sp.GetRequiredService<IJobAdministration>().ReplayAsync(jobId, "Test", ct)), Ct);

        control.Fail(reference, false);
        var replayed = await Scopes.RunAsync(TenantContext.ForPlatform(), async (sp, ct) =>
        {
            var admin = sp.GetRequiredService<IJobAdministration>();
            Assert.Contains(await admin.ListDeadLettersAsync(ct), d => d.Id == jobId && d.TenantId == _a.Id);
            return await admin.ReplayAsync(jobId, "Ursache behoben (Test)", ct);
        }, Ct);
        Assert.True(replayed);

        await TestJobControl.WaitUntilAsync(() => JobRowAsync(reference).Result?.Status == JobStatus.Succeeded, Wait, Ct);
        await worker.StopAsync(Ct);
        Assert.Equal(1, await ActivityCountAsync(_a.Id, EffectJobHandler.KindFor(jobId)));
        Assert.Equal(1, (await JobRowAsync(reference))!.Value.Attempts);
    }

    [Fact]
    public async Task Geordnetes_Beenden_gibt_den_Lease_frei_und_der_Neustart_verarbeitet_genau_einmal()
    {
        var control = new TestJobControl();
        var reference = $"{_a.MemberB}#beenden-{Guid.NewGuid():N}";
        control.Block(reference);
        var jobId = await EnqueueAsync(_a.Id, reference);

        using (var first = WorkerHost.Create(pg, control, "w-stop-1"))
        {
            await first.StartAsync(Ct);
            await TestJobControl.WaitUntilAsync(() => control.Entries(reference) == 1, Wait, Ct);
            Assert.Equal(JobStatus.Leased, (await JobRowAsync(reference))!.Value.Status);
            await first.StopAsync(Ct);
        }

        var released = (await JobRowAsync(reference))!.Value;
        Assert.Equal(JobStatus.Queued, released.Status);
        Assert.Equal(0, released.Attempts);
        Assert.Equal(0, await ActivityCountAsync(_a.Id, EffectJobHandler.KindFor(jobId)));

        control.Release(reference);
        using var second = WorkerHost.Create(pg, control, "w-stop-2");
        await second.StartAsync(Ct);
        await TestJobControl.WaitUntilAsync(() => JobRowAsync(reference).Result?.Status == JobStatus.Succeeded, Wait, Ct);
        await second.StopAsync(Ct);

        Assert.Equal(2, control.Entries(reference));
        Assert.Equal(1, await ActivityCountAsync(_a.Id, EffectJobHandler.KindFor(jobId)));
        Assert.Equal("w-stop-2", (await JobRowAsync(reference))!.Value.WorkerId);
    }

    [Fact]
    public async Task Abgelaufener_Lease_eines_abgestuerzten_Workers_wird_erneut_beansprucht_und_genau_einmal_verarbeitet()
    {
        var control = new TestJobControl();
        var reference = $"{_a.MemberA}#absturz-{Guid.NewGuid():N}";
        var jobId = JobId.New();

        // Hinterlassenschaft eines abgestürzten Workers: beansprucht, Lease abgelaufen, keine Bestätigung.
        await using (var conn = new NpgsqlConnection(pg.AppConnectionString))
        {
            await conn.OpenAsync(Ct);
            await using var cmd = new NpgsqlCommand(
                "insert into platform.job (id, tenant_id, job_type, reference, idempotency_key, status, run_at, lease_until, attempts, max_attempts, worker_id, created_at) values (@id, @t, @type, @r, @r, 2, now() - interval '5 minutes', now() - interval '1 second', 1, 5, 'abgestuerzt', now() - interval '5 minutes')",
                conn);
            cmd.Parameters.AddWithValue("id", jobId.Value);
            cmd.Parameters.AddWithValue("t", _a.Id.Value);
            cmd.Parameters.AddWithValue("type", EffectJobHandler.JobType);
            cmd.Parameters.AddWithValue("r", reference);
            await cmd.ExecuteNonQueryAsync(Ct);
        }

        using var worker = WorkerHost.Create(pg, control, "w-nachfolger");
        await worker.StartAsync(Ct);
        await TestJobControl.WaitUntilAsync(() => JobRowAsync(reference).Result?.Status == JobStatus.Succeeded, Wait, Ct);
        await worker.StopAsync(Ct);

        var row = (await JobRowAsync(reference))!.Value;
        Assert.Equal("w-nachfolger", row.WorkerId);
        Assert.Equal(2, row.Attempts);
        Assert.Equal(1, control.Entries(reference));
        Assert.Equal(1, await ActivityCountAsync(_a.Id, EffectJobHandler.KindFor(jobId)));
    }

    [Fact]
    public async Task Verlorener_Lease_verwirft_die_Wirkung_des_langsamen_Workers_keine_doppelte_Wirkung()
    {
        var control = new TestJobControl();
        var reference = $"{_b.MemberA}#lease-{Guid.NewGuid():N}";
        control.Block(reference);
        var jobId = await EnqueueAsync(_b.Id, reference);
        Action<JobWorkerOptions> shortLease = o => o.LeaseDuration = TimeSpan.FromSeconds(1);

        using var slow = WorkerHost.Create(pg, control, "w-langsam", o =>
        {
            shortLease(o);
            o.MaxParallel = 1;
        });
        await slow.StartAsync(Ct);
        await TestJobControl.WaitUntilAsync(() => control.Entries(reference) == 1, Wait, Ct);

        // Der Lease läuft ab, während der langsame Worker noch in seiner Transaktion hängt; ein zweiter Worker übernimmt.
        using var fast = WorkerHost.Create(pg, control, "w-schnell", shortLease);
        await fast.StartAsync(Ct);
        await TestJobControl.WaitUntilAsync(() => control.Entries(reference) == 2, Wait, Ct);

        control.Release(reference);
        await TestJobControl.WaitUntilAsync(() => JobRowAsync(reference).Result?.Status == JobStatus.Succeeded, Wait, Ct);
        await Task.Delay(300, Ct);
        await slow.StopAsync(Ct);
        await fast.StopAsync(Ct);

        var row = (await JobRowAsync(reference))!.Value;
        Assert.Equal("w-schnell", row.WorkerId);
        Assert.Equal(2, row.Attempts);
        Assert.Equal(1, await ActivityCountAsync(_b.Id, EffectJobHandler.KindFor(jobId)));
    }

    [Fact]
    public async Task Fairness_zwei_Tenants_mit_ungleicher_Last_der_kleine_Tenant_wird_abwechselnd_bedient()
    {
        var control = new TestJobControl();
        var tag = Guid.NewGuid().ToString("N");
        var heavy = Enumerable.Range(0, 40).Select(i => $"{_a.MemberA}#fair-{tag}-{i:00}").ToList();
        var light = Enumerable.Range(0, 5).Select(i => $"{_b.MemberA}#fair-{tag}-{i:00}").ToList();
        foreach (var reference in heavy)
        {
            await EnqueueAsync(_a.Id, reference);
        }

        foreach (var reference in light)
        {
            await EnqueueAsync(_b.Id, reference);
        }

        using var worker = WorkerHost.Create(pg, control, "w-fair", o =>
        {
            o.MaxParallel = 1;
            o.MaxConcurrentPerTenant = 1;
        });
        await worker.StartAsync(Ct);
        await TestJobControl.WaitUntilAsync(() => control.Order.Count(o => o.Reference.Contains(tag, StringComparison.Ordinal)) == 45, TimeSpan.FromSeconds(60), Ct);
        await worker.StopAsync(Ct);

        var order = control.Order.Where(o => o.Reference.Contains(tag, StringComparison.Ordinal)).Select(o => o.Tenant).ToList();
        var lightPositions = order.Select((tenant, index) => (tenant, index)).Where(x => x.tenant == _b.Id).Select(x => x.index + 1).ToList();
        Assert.Equal(5, lightPositions.Count);
        Assert.True(lightPositions.Max() <= 10, $"Der kleine Tenant wurde nicht abwechselnd bedient: Positionen {string.Join(", ", lightPositions)}");
    }

    [Fact]
    public async Task Zeitgesteuerte_Aufgabe_laeuft_je_Faelligkeit_genau_einmal_trotz_zweier_Replikate()
    {
        var control = new TestJobControl();
        using var a = WorkerHost.Create(pg, control, "w-plan-a", o => o.MaxParallel = 1);
        using var b = WorkerHost.Create(pg, control, "w-plan-b", o => o.MaxParallel = 1);
        await a.StartAsync(Ct);
        await b.StartAsync(Ct);
        await Task.Delay(TimeSpan.FromSeconds(3.5), Ct);
        await a.StopAsync(Ct);
        await b.StopAsync(Ct);

        var runs = control.ScheduledRuns.OrderBy(r => r).ToList();
        Assert.InRange(runs.Count, 2, 4);
        for (var i = 1; i < runs.Count; i++)
        {
            Assert.True(runs[i] - runs[i - 1] >= TimeSpan.FromMilliseconds(900), $"Doppelausführung: Abstand {(runs[i] - runs[i - 1]).TotalMilliseconds} ms");
        }

        await using var conn = new NpgsqlConnection(pg.AppConnectionString);
        await conn.OpenAsync(Ct);
        await using var cmd = new NpgsqlCommand("select last_run_at is not null and last_error is null from platform.job_schedule where name = @n", conn);
        cmd.Parameters.AddWithValue("n", TickScheduledTask.Name);
        Assert.True((bool)(await cmd.ExecuteScalarAsync(Ct))!);
    }

    private async Task<JobId> EnqueueAsync(TenantId tenant, string reference, int? maxAttempts = null) =>
        await Scopes.RunAsync(TenantContext.ForTenant(tenant), async (sp, ct) =>
        {
            await using var tx = await sp.GetRequiredService<IContextTransaction>().BeginAsync(ct);
            var result = await sp.GetRequiredService<IJobQueue>().EnqueueAsync(new JobRequest(EffectJobHandler.JobType, reference, reference, MaxAttempts: maxAttempts), ct);
            await tx.CommitAsync(ct);
            return result.JobId!.Value;
        }, Ct);

    private async Task<(JobStatus Status, int Attempts, string? WorkerId, string? LastError)?> JobRowAsync(string reference)
    {
        await using var conn = new NpgsqlConnection(pg.AppConnectionString);
        await conn.OpenAsync(Ct);
        await using var cmd = new NpgsqlCommand("select status, attempts, worker_id, last_error from platform.job where reference = @r", conn);
        cmd.Parameters.AddWithValue("r", reference);
        await using var reader = await cmd.ExecuteReaderAsync(Ct);
        if (!await reader.ReadAsync(Ct))
        {
            return null;
        }

        return ((JobStatus)reader.GetInt16(0), reader.GetInt32(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3));
    }

    private Task<int> ActivityCountAsync(TenantId tenant, string kind) =>
        Scopes.RunAsync(TenantContext.ForTenant(tenant), async (sp, ct) =>
        {
            await using var tx = await sp.GetRequiredService<IContextTransaction>().BeginAsync(ct);
            return await sp.GetRequiredService<ProgressDbContext>().ActivityEvents.CountAsync(e => e.Kind == kind, ct);
        }, Ct);
}
