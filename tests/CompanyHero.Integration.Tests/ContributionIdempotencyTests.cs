using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CompanyHero.Modules.Challenges.Api;
using CompanyHero.Modules.Challenges.Application;
using CompanyHero.Modules.Challenges.Domain;
using CompanyHero.Modules.Challenges.Infrastructure;
using CompanyHero.Modules.Metering.Application;
using CompanyHero.Modules.Organisation.Domain;
using CompanyHero.Modules.Progress.Application;
using CompanyHero.Platform.Data;
using CompanyHero.Platform.Jobs;
using CompanyHero.Platform.Metering;
using CompanyHero.Platform.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace CompanyHero.Integration.Tests;

/// <summary>
/// Backend 11.4, Challenges 9.3, Domänenkarte 7, A-005, A-009: Ein Offline-Beitrag mit Client-Idempotenzschlüssel und ein
/// Kiosk-Beitrag mit Vorgangskennung werden mehrfach übertragen: genau ein fachlicher Beitrag, ein Aktivitätsereignis,
/// ein Metering-Ereignis (korrekte Verbrauchsbuchung). Beide Regime teilen einen Namensraum je Tenant und Person.
/// </summary>
[Collection(PostgresTests.Name)]
public sealed class ContributionIdempotencyTests(PostgresFixture pg) : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ScratchTenant _w = null!;
    private ScratchTenant _h = null!;

    public async ValueTask InitializeAsync()
    {
        _w = await pg.CreateScratchTenantWithMembersAsync($"Beitrag W {Guid.NewGuid():N}");
        _h = await pg.CreateScratchTenantWithMembersAsync($"Beitrag H {Guid.NewGuid():N}");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private ITenantScopeFactory Scopes => pg.Api.Services.GetRequiredService<ITenantScopeFactory>();

    [Fact]
    public async Task Offline_Beitrag_mit_Idempotenzschluessel_mehrfach_uebertragen_ergibt_genau_einen_Beitrag_ein_Aktivitaets_und_ein_Metering_Ereignis()
    {
        var challenge = await CreateChallengeAsync(ChallengeMetric.Checkmark);
        var person = _w.MemberA;
        using var client = Client(_w.Id, person);
        var key = Guid.CreateVersion7().ToString("D");
        var recordedAt = pg.Clock.GetUtcNow().AddMinutes(-10);
        var request = new ContributionRequest("1", recordedAt, "mobile", key, null);

        // Erste Übertragung, verlorene Antwort, Wiederholungen nacheinander und parallel (Synchronisierung nach Offline-Phase).
        using var first = await client.PostAsJsonAsync(Contributions(challenge), request, Ct);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var created = await first.Content.ReadFromJsonAsync<ContributionResponse>(Ct);

        using var second = await client.PostAsJsonAsync(Contributions(challenge), request, Ct);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var repeated = await second.Content.ReadFromJsonAsync<ContributionResponse>(Ct);
        Assert.Equal(created!.ContributionId, repeated!.ContributionId);
        Assert.Equal("already_recorded", repeated.Outcome);

        var parallel = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => client.PostAsJsonAsync(Contributions(challenge), request, Ct)));
        Assert.All(parallel, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        foreach (var r in parallel)
        {
            r.Dispose();
        }

        var counts = await CountsAsync(_w.Id, challenge, person);
        Assert.Equal((1, 1, 1, 1), counts);
    }

    [Fact]
    public async Task Kiosk_Beitrag_mit_Vorgangskennung_mehrfach_uebertragen_ergibt_genau_einen_Beitrag_und_eine_Verbrauchsbuchung()
    {
        var challenge = await CreateChallengeAsync(ChallengeMetric.Count);
        var person = _w.MemberB;
        // Kiosk-Beiträge kommen aus der Kiosk-Personensitzung innerhalb der Gerätesitzung (A-005, A-018).
        using var client = pg.KioskClient(await pg.KioskSessionAsync(_w.Id, person, Ct));

        // Vor dem Absenden reserviert das Backend eine Vorgangskennung (A-005).
        using var reserve = await client.PostAsync(Operations(challenge), null, Ct);
        Assert.Equal(HttpStatusCode.Created, reserve.StatusCode);
        var operation = (await reserve.Content.ReadFromJsonAsync<OperationResponse>(Ct))!.OperationId;

        var request = new ContributionRequest("2.5", pg.Clock.GetUtcNow(), "kiosk", null, operation);
        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => client.PostAsJsonAsync(Contributions(challenge), request, Ct)));
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(4, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in responses)
        {
            ids.Add((await r.Content.ReadFromJsonAsync<ContributionResponse>(Ct))!.ContributionId);
            r.Dispose();
        }

        Assert.Single(ids);
        Assert.Equal((1, 1, 1, 1), await CountsAsync(_w.Id, challenge, person));

        // Nach erneuter Anmeldung ist der Vorgang nicht mehr offen (A-005 Wiederaufnahme).
        var open = await Scopes.RunAsync(TenantContext.ForPerson(_w.Id, person, [Role.Member]), (sp, ct) =>
            sp.GetRequiredService<IContributionService>().ListOpenOperationsAsync(ct), Ct);
        Assert.DoesNotContain(operation, open);
    }

    [Fact]
    public async Task Gleiche_Kennung_mit_abweichendem_Inhalt_wird_in_beiden_Regimen_abgelehnt()
    {
        var challenge = await CreateChallengeAsync(ChallengeMetric.Count, _h);
        using var client = Client(_h.Id, _h.MemberA);
        var recordedAt = pg.Clock.GetUtcNow();

        var key = Guid.CreateVersion7().ToString("D");
        using var mobile = await client.PostAsJsonAsync(Contributions(challenge), new ContributionRequest("3", recordedAt, "mobile", key, null), Ct);
        Assert.Equal(HttpStatusCode.Created, mobile.StatusCode);
        using var mobileChanged = await client.PostAsJsonAsync(Contributions(challenge), new ContributionRequest("4", recordedAt, "mobile", key, null), Ct);
        Assert.Equal(HttpStatusCode.Conflict, mobileChanged.StatusCode);

        using var kioskClient = pg.KioskClient(await pg.KioskSessionAsync(_h.Id, _h.MemberA, Ct));
        using var reserve = await kioskClient.PostAsync(Operations(challenge), null, Ct);
        var operation = (await reserve.Content.ReadFromJsonAsync<OperationResponse>(Ct))!.OperationId;
        using var kiosk = await kioskClient.PostAsJsonAsync(Contributions(challenge), new ContributionRequest("3", recordedAt, "kiosk", null, operation), Ct);
        Assert.Equal(HttpStatusCode.Created, kiosk.StatusCode);
        using var kioskChanged = await kioskClient.PostAsJsonAsync(Contributions(challenge), new ContributionRequest("3", recordedAt.AddMinutes(1), "kiosk", null, operation), Ct);
        Assert.Equal(HttpStatusCode.Conflict, kioskChanged.StatusCode);

        Assert.Equal((2, 2, 1, 2), await CountsAsync(_h.Id, challenge, _h.MemberA));
    }

    [Fact]
    public async Task Vorgangskennung_ist_an_Tenant_und_Person_gebunden_und_ein_abgebrochener_Vorgang_nimmt_nichts_mehr_an()
    {
        var challenge = await CreateChallengeAsync(ChallengeMetric.Checkmark);
        using var cem = pg.KioskClient(await pg.KioskSessionAsync(_w.Id, _w.MemberB, Ct));
        using var bea = pg.KioskClient(await pg.KioskSessionAsync(_w.Id, _w.MemberA, Ct));

        using var reserve = await cem.PostAsync(Operations(challenge), null, Ct);
        var operation = (await reserve.Content.ReadFromJsonAsync<OperationResponse>(Ct))!.OperationId;
        var request = new ContributionRequest("1", pg.Clock.GetUtcNow(), "kiosk", null, operation);

        // Andere Person: Kennung unbekannt.
        using var foreign = await bea.PostAsJsonAsync(Contributions(challenge), request, Ct);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);

        // Terminal abgebrochen: verspäteter Request wird nicht mehr angenommen.
        var aborted = await Scopes.RunAsync(TenantContext.ForPerson(_w.Id, _w.MemberB, [Role.Member]), (sp, ct) =>
            sp.GetRequiredService<IContributionService>().AbortOperationAsync(operation, ct), Ct);
        Assert.True(aborted);
        using var late = await cem.PostAsJsonAsync(Contributions(challenge), request, Ct);
        Assert.Equal(HttpStatusCode.Gone, late.StatusCode);

        Assert.Equal((0, 0, 0, 0), await CountsAsync(_w.Id, challenge, _w.MemberB));
    }

    [Fact]
    public async Task Vorgangskennung_und_Client_Schluessel_liegen_in_einem_Namensraum_je_Tenant_und_Person()
    {
        var challenge = await CreateChallengeAsync(ChallengeMetric.Checkmark);
        using var client = Client(_w.Id, _w.MemberA);
        using var kioskClient = pg.KioskClient(await pg.KioskSessionAsync(_w.Id, _w.MemberA, Ct));
        using var reserve = await kioskClient.PostAsync(Operations(challenge), null, Ct);
        var operation = (await reserve.Content.ReadFromJsonAsync<OperationResponse>(Ct))!.OperationId;

        // Dieselbe Kennung als Client-Schlüssel eines Handy-Beitrags: der Namensraum ist belegt.
        using var asClientKey = await client.PostAsJsonAsync(Contributions(challenge), new ContributionRequest("1", pg.Clock.GetUtcNow(), "mobile", operation, null), Ct);
        Assert.Equal(HttpStatusCode.Conflict, asClientKey.StatusCode);

        // Regime und Kanal müssen zusammenpassen (Vertrag A-009); kein clientseitiger Schlüssel am Kiosk.
        using var kioskWithClientKey = await kioskClient.PostAsJsonAsync(Contributions(challenge), new ContributionRequest("1", pg.Clock.GetUtcNow(), "kiosk", Guid.CreateVersion7().ToString("D"), null), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, kioskWithClientKey.StatusCode);

        // Kanal und Sitzung gehören zusammen (A-005): Mitgliedssitzung reserviert keine Vorgangskennung und sendet keinen Kiosk-Kanal; die Kiosk-Sitzung keinen Handy-Kanal.
        using var memberReserve = await client.PostAsync(Operations(challenge), null, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, memberReserve.StatusCode);
        using var memberKioskChannel = await client.PostAsJsonAsync(Contributions(challenge), new ContributionRequest("1", pg.Clock.GetUtcNow(), "kiosk", null, operation), Ct);
        Assert.Equal(HttpStatusCode.Forbidden, memberKioskChannel.StatusCode);
        using var kioskMobileChannel = await kioskClient.PostAsJsonAsync(Contributions(challenge), new ContributionRequest("1", pg.Clock.GetUtcNow(), "mobile", Guid.CreateVersion7().ToString("D"), null), Ct);
        Assert.Equal(HttpStatusCode.Forbidden, kioskMobileChannel.StatusCode);
        using var notSortable = await client.PostAsJsonAsync(Contributions(challenge), new ContributionRequest("1", pg.Clock.GetUtcNow(), "mobile", Guid.NewGuid().ToString("D"), null), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, notSortable.StatusCode);
    }

    [Fact]
    public async Task Beitrag_und_Folgen_entstehen_in_einer_Transaktion_und_der_Job_berechnet_den_Kollektivstand()
    {
        var challenge = await CreateChallengeAsync(ChallengeMetric.Count);
        using var bea = Client(_w.Id, _w.MemberA);
        using var cem = Client(_w.Id, _w.MemberB);
        var day = pg.Clock.GetUtcNow();

        using var b1 = await bea.PostAsJsonAsync(Contributions(challenge), new ContributionRequest("1.5", day, "mobile", Guid.CreateVersion7().ToString("D"), null), Ct);
        using var b2 = await bea.PostAsJsonAsync(Contributions(challenge), new ContributionRequest("2", day.AddMinutes(-5), "mobile", Guid.CreateVersion7().ToString("D"), null), Ct);
        using var c1 = await cem.PostAsJsonAsync(Contributions(challenge), new ContributionRequest("0.5", day, "mobile", Guid.CreateVersion7().ToString("D"), null), Ct);
        Assert.All(new[] { b1, b2, c1 }, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));

        // Zweiter Beitrag derselben Person am selben Tag: kein zweiter Teilnehmertag (Challenges 8 ↔ Metering).
        Assert.Equal((2, 2, 1, 2), await CountsAsync(_w.Id, challenge, _w.MemberA));
        var jobs = await QueuedRecalculationsAsync(challenge);
        Assert.Equal(3, jobs);

        var control = new TestJobControl { SubscriberPerson = _w.MemberA };
        using var worker = WorkerHost.Create(pg, control, "w-kollektiv");
        await worker.StartAsync(Ct);
        await TestJobControl.WaitUntilAsync(
            () => QueuedRecalculationsAsync(challenge).Result == 0 && SubscriberEffectsAsync(challenge).Result == 3,
            TimeSpan.FromSeconds(20),
            Ct);
        await worker.StopAsync(Ct);

        // Unter fünf Beitragenden liefert die API nur den Prozentwert (A-023); der berechnete Stand ist über die Anwendungsfunktion sichtbar.
        var collective = await bea.GetFromJsonAsync<CollectiveResponse>(Collective(challenge), Ct);
        Assert.Null(collective!.Total);
        Assert.Null(collective.ContributionCount);
        Assert.Null(collective.ContributorCount);
        var computed = await Scopes.RunAsync(TenantContext.ForPerson(_w.Id, _w.MemberA, [Role.Member]), (sp, ct) => sp.GetRequiredService<IChallengeCatalog>().GetCollectiveAsync(challenge, ct), Ct);
        Assert.Equal(4m, computed!.Total);
        Assert.Equal(3, computed.ContributionCount);
        Assert.Equal(2, computed.ContributorCount);

        // Abonnent aus einem fremden Modul hat je Ereignis genau einen Job erhalten und das Ereignis über die Schnittstelle gelesen.
        Assert.Equal(3, await SubscriberEffectsAsync(challenge));
    }

    [Fact]
    public async Task Metering_Emission_ausserhalb_der_Transaktion_der_fachlichen_Aenderung_wird_abgelehnt()
    {
        await Scopes.RunAsync(TenantContext.ForTenant(_w.Id), async (sp, ct) =>
        {
            var emitter = sp.GetRequiredService<IMeteringEmitter>();
            var emission = new MeteringEmission("M1", "challenge.participant_day", Guid.NewGuid().ToString("D"), 1m, MeteringSource.Self, pg.Clock.GetUtcNow(), $"test:{Guid.NewGuid():N}");
            await Assert.ThrowsAsync<InvalidOperationException>(() => emitter.EmitAsync(emission, ct));

            await using var tx = await sp.GetRequiredService<IContextTransaction>().BeginAsync(ct);
            Assert.Equal(EmissionOutcome.Recorded, await emitter.EmitAsync(emission, ct));
            Assert.Equal(EmissionOutcome.Duplicate, await emitter.EmitAsync(emission, ct));
            await tx.RollbackAsync(ct);
        }, Ct);
    }

    [Fact]
    public async Task Fachregeln_der_Erfassung_gelten_am_Endpunkt()
    {
        var challenge = await CreateChallengeAsync(ChallengeMetric.Checkmark);
        using var client = Client(_w.Id, _w.MemberA);

        using var future = await client.PostAsJsonAsync(Contributions(challenge), new ContributionRequest("1", pg.Clock.GetUtcNow().AddHours(1), "mobile", Guid.CreateVersion7().ToString("D"), null), Ct);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, future.StatusCode);
        using var tooOld = await client.PostAsJsonAsync(Contributions(challenge), new ContributionRequest("1", pg.Clock.GetUtcNow().AddDays(-4), "mobile", Guid.CreateVersion7().ToString("D"), null), Ct);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, tooOld.StatusCode);
        using var wrongValue = await client.PostAsJsonAsync(Contributions(challenge), new ContributionRequest("2", pg.Clock.GetUtcNow(), "mobile", Guid.CreateVersion7().ToString("D"), null), Ct);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, wrongValue.StatusCode);
        using var foreignTenant = await Client(_h.Id, _h.MemberA).PostAsJsonAsync(Contributions(challenge), new ContributionRequest("1", pg.Clock.GetUtcNow(), "mobile", Guid.CreateVersion7().ToString("D"), null), Ct);
        Assert.Equal(HttpStatusCode.NotFound, foreignTenant.StatusCode);

        Assert.Equal((0, 0, 0, 0), await CountsAsync(_w.Id, challenge, _w.MemberA));
    }

    private Task<Guid> CreateChallengeAsync(ChallengeMetric metric, ScratchTenant? tenant = null) =>
        Scopes.RunAsync(TenantContext.ForPerson((tenant ?? _w).Id, (tenant ?? _w).Manager, [Role.Member, Role.ProgrammeManager]), (sp, ct) =>
            sp.GetRequiredService<IChallengeCatalog>().StartRunningAsync($"Schritte {Guid.NewGuid():N}", metric, 100m, pg.Clock.GetUtcNow().AddDays(-5), pg.Clock.GetUtcNow().AddDays(7), ct), Ct)
        .ContinueWith(t => t.Result, TaskScheduler.Default);

    /// <summary>(Beiträge, Aktivitätsereignisse „challenge_contribution“, Metering-Ereignisse Teilnehmertag, Fachereignisse) der Person in der Challenge.</summary>
    private async Task<(int Contributions, int Activities, int Metering, int Events)> CountsAsync(TenantId tenant, Guid challenge, PersonId person)
    {
        var (contributions, events) = await Scopes.RunAsync(TenantContext.ForTenant(tenant), async (sp, ct) =>
        {
            await using var tx = await sp.GetRequiredService<IContextTransaction>().BeginAsync(ct);
            var db = sp.GetRequiredService<ChallengesDbContext>();
            var contributionIds = await db.Contributions.Where(c => c.ChallengeId == challenge && c.PersonId == person).Select(c => c.Id).ToListAsync(ct);
            var events = await db.Events.Where(e => e.CausedBy == person).ToListAsync(ct);
            return (contributionIds.Count, events.Count(e => contributionIds.Contains(e.ReadContributionRecorded().ContributionId)));
        }, Ct);

        var activities = await Scopes.RunAsync(TenantContext.ForPerson(tenant, person, [Role.Member]), async (sp, ct) =>
        {
            var own = await sp.GetRequiredService<IPersonalActivityQuery>().GetForPersonAsync(person, ct);
            return own!.Count(a => a.Kind == ContributionConstants.ActivityKind && a.OccurredAt >= pg.Clock.GetUtcNow().AddDays(-6));
        }, Ct);

        var metering = await Scopes.RunAsync(TenantContext.ForTenant(tenant), async (sp, ct) =>
        {
            var ledger = await sp.GetRequiredService<IMeteringLedger>().ListAsync(ContributionConstants.ParticipantDayMetric, ct);
            return ledger.Count(e => e.SubjectRef == challenge.ToString("D") && e.IdempotencyKey.EndsWith(ContributionConstants.ParticipantDayMetric, StringComparison.Ordinal) && e.Quantity == 1m);
        }, Ct);

        // Metering-Ereignisse tragen keinen Personenbezug; die Person wird über ihre Fachereignisse zugeordnet.
        var meteringForPerson = await Scopes.RunAsync(TenantContext.ForTenant(tenant), async (sp, ct) =>
        {
            await using var tx = await sp.GetRequiredService<IContextTransaction>().BeginAsync(ct);
            var eventIds = await sp.GetRequiredService<ChallengesDbContext>().Events.Where(e => e.CausedBy == person).Select(e => e.Id).ToListAsync(ct);
            var ledger = await sp.GetRequiredService<IMeteringLedger>().ListAsync(ContributionConstants.ParticipantDayMetric, ct);
            return ledger.Count(e => eventIds.Any(id => e.IdempotencyKey.StartsWith(id.ToString("D"), StringComparison.Ordinal)));
        }, Ct);

        Assert.True(metering >= meteringForPerson);
        return (contributions, activities, meteringForPerson, events);
    }

    private Task<int> SubscriberEffectsAsync(Guid challenge) =>
        Scopes.RunAsync(TenantContext.ForTenant(_w.Id), async (sp, ct) =>
        {
            await using var tx = await sp.GetRequiredService<IContextTransaction>().BeginAsync(ct);
            var contributionIds = await sp.GetRequiredService<ChallengesDbContext>().Contributions.Where(c => c.ChallengeId == challenge).Select(c => c.Id).ToListAsync(ct);
            var kinds = contributionIds.Select(id => $"{SubscriberJobHandler.Kind}:{id:D}"[..Math.Min(60, SubscriberJobHandler.Kind.Length + 37)]).ToList();
            return await sp.GetRequiredService<Modules.Progress.Infrastructure.ProgressDbContext>().ActivityEvents.CountAsync(e => kinds.Contains(e.Kind), ct);
        }, Ct);

    private async Task<int> QueuedRecalculationsAsync(Guid challenge)
    {
        await using var conn = new NpgsqlConnection(pg.AppConnectionString);
        await conn.OpenAsync(Ct);
        await using var cmd = new NpgsqlCommand("select count(*)::int from platform.job where job_type = @t and reference = @r and status in (1, 2)", conn);
        cmd.Parameters.AddWithValue("t", "challenges.collective.recalculate");
        cmd.Parameters.AddWithValue("r", challenge.ToString("D"));
        return (int)(await cmd.ExecuteScalarAsync(Ct))!;
    }

    private HttpClient Client(TenantId tenant, PersonId person) => pg.Client(tenant, person);

    private static Uri Contributions(Guid challenge) => new($"/api/challenges/{challenge:D}/contributions", UriKind.Relative);

    private static Uri Operations(Guid challenge) => new($"/api/challenges/{challenge:D}/contribution-operations", UriKind.Relative);

    private static Uri Collective(Guid challenge) => new($"/api/challenges/{challenge:D}/collective", UriKind.Relative);
}
