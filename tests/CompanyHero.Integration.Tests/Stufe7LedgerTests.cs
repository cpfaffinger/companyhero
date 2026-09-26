using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using CompanyHero.Modules.Challenges.Application;
using CompanyHero.Modules.Metering.Api;
using CompanyHero.Modules.Metering.Domain;
using CompanyHero.Modules.Notifications.Api;
using CompanyHero.Modules.Notifications.Application;
using CompanyHero.Modules.Progress.Application;
using CompanyHero.Modules.Progress.Domain;
using CompanyHero.Platform.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyHero.Integration.Tests;

/// <summary>Metering 9.1, 9.7 (A-069, A-070): genau ein Ledger-Ereignis je Fachereignis, Gegenbuchung, Summen je Periode, Nachlauf nach Versiegelung, alle Metriken der Stufen 4 und 6.</summary>
[Collection(PostgresTests.Name)]
public sealed class Stufe7LedgerTests(PostgresFixture pg) : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ScratchTenant _t = null!;

    public async ValueTask InitializeAsync() => _t = await pg.CreateScratchTenantWithMembersAsync($"Ledger {Guid.NewGuid():N}");

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Beitrag_zweimal_uebertragen_ein_Ledger_Ereignis_Korrektur_als_Gegenbuchung_Summen_je_Periode_stimmen()
    {
        var challenge = await Stufe7.RunningChallengeAsync(pg, _t, "Ledger", Ct);
        using var member = pg.Client(_t.Id, _t.MemberA);
        using var admin = pg.Client(_t.Id, _t.Admin);
        var now = pg.Clock.GetUtcNow();
        var key = Guid.CreateVersion7().ToString("D");

        var contributionId = await Stufe7.ContributeAsync(member, challenge, now, key, Ct);
        Assert.Equal(contributionId, await Stufe7.ContributeAsync(member, challenge, now, key, Ct, HttpStatusCode.OK));

        var ledger = await Stufe7.LedgerAsync(pg, _t.Id, null, Ct);
        var participantDays = ledger.Where(e => e.Metric == MeteringMetrics.ParticipantDay).ToList();
        Assert.Single(participantDays);
        Assert.Equal(1m, participantDays[0].Quantity);
        Assert.StartsWith(contributionId, participantDays[0].IdempotencyKey, StringComparison.Ordinal);
        Assert.Single(ledger, e => e.Metric == MeteringMetrics.ActiveMonth);
        Assert.Single(ledger, e => e.Metric == MeteringMetrics.ActiveDay);
        Assert.All(ledger, e => Assert.True(e.Rated));
        Assert.All(ledger, e => Assert.False(e.Late));

        // Korrektur: Gegenbuchung mit gleicher Menge und negativem Vorzeichen, Verweis auf das Ursprungsereignis (Metering 3.3).
        using var reversed = await member.PostAsync(new Uri($"/api/challenges/{challenge:D}/contributions/{contributionId}/reverse", UriKind.Relative), null, Ct);
        Assert.Equal(HttpStatusCode.OK, reversed.StatusCode);
        var after = await Stufe7.LedgerAsync(pg, _t.Id, MeteringMetrics.ParticipantDay, Ct);
        Assert.Equal(2, after.Count);
        var counter = after.Single(e => e.Quantity < 0m);
        Assert.Equal(-1m, counter.Quantity);
        Assert.Equal(participantDays[0].Id, counter.ReversalOf);
        Assert.Equal(0m, after.Sum(e => e.Quantity));

        // Summen je Periode: die Tagesaggregate der Verwaltung entsprechen den Ledger-Summen je Metrik.
        var period = TenantTimeZone.PeriodOf(now);
        var usage = await Stufe7.GetAsync<UsageResponse>(admin, $"/api/billing/usage?period={period}", Ct);
        var all = await Stufe7.LedgerAsync(pg, _t.Id, null, Ct);
        foreach (var metric in usage.Metrics)
        {
            var expected = all.Where(e => e.Metric == metric.Metric && e.Module == metric.Module && e.Period == period).Sum(e => e.Quantity);
            Assert.Equal(expected.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture), metric.Total);
        }

        Assert.Equal("0.0000", usage.Metrics.Single(m => m.Metric == MeteringMetrics.ParticipantDay).Total);
        Assert.Equal("1.0000", usage.Metrics.Single(m => m.Metric == MeteringMetrics.ActiveMonth).Total);
        Assert.Equal(all.Where(e => e.Period == period).GroupBy(e => (e.Module, e.Metric)).Count(), usage.Metrics.Count);
    }

    [Fact]
    public async Task Alle_Fachereignisse_der_Stufen_4_und_6_stehen_ohne_Personenbezug_im_Ledger()
    {
        using var admin = pg.Client(_t.Id, _t.Admin);
        using var member = pg.Client(_t.Id, _t.MemberA);
        using var manager = pg.Client(_t.Id, _t.Manager);

        // Zugang: Beitritt (member.joined) und Kiosk-Gerät (kiosk.device_month).
        var joinCode = await AccessJoinTests.CreateJoinCodeAsync(pg, _t, Ct);
        var (kiosk, _) = await KioskTests.RegisterKioskAsync(pg, _t, Ct);
        using (kiosk)
        {
            await kiosk.PostJsonAsync<Modules.Identity.Api.JoinResponse>($"/api/join/{joinCode}/kiosk", new Modules.Identity.Api.KioskJoinRequest("Ledger Kiosk", Modules.Identity.Api.VisibilityDto.Company, "7391", null), Ct, HttpStatusCode.Created);
        }

        // Fachpfad: Beitrag (participant_day, active_day, active_month) und Push (notification.sent) über Challenge-Start.
        await Stufe6.QuietHoursAwayAsync(admin, Ct);
        using var key = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        using var subscription = await member.PostAsJsonAsync("/api/me/notifications/subscriptions", new PushSubscriptionRequest($"https://push.test/ledger/{Guid.NewGuid():N}", System.Buffers.Text.Base64Url.EncodeToString(WebPushCrypto.ExportRaw(key.PublicKey)), System.Buffers.Text.Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16)), "Handy"), Ct);
        Assert.Equal(HttpStatusCode.Created, subscription.StatusCode);
        var challenge = await Stufe6.PlanChallengeAsync(manager, pg.Clock.GetUtcNow(), "Ledger Push", 20m, Ct);
        var control = new TestJobControl { SubscriberPerson = _t.MemberA };
        using var worker = WorkerHost.Create(pg, control, "w-ledger");
        await worker.StartAsync(Ct);
        try
        {
            await Stufe6.WaitUntilRunningAsync(worker, member, challenge, Ct);
            await Stufe7.ContributeAsync(member, challenge, pg.Clock.GetUtcNow(), Guid.CreateVersion7().ToString("D"), Ct);
            await Stufe6.WaitForQueueAsync(pg, _t.Id, Ct);
        }
        finally
        {
            await worker.StopAsync(Ct);
        }

        var ledger = await Stufe7.LedgerAsync(pg, _t.Id, null, Ct);
        var metrics = ledger.Select(e => e.Metric).ToHashSet(StringComparer.Ordinal);
        Assert.Superset(new HashSet<string> { MeteringMetrics.Joined, MeteringMetrics.KioskDeviceMonth, MeteringMetrics.ParticipantDay, MeteringMetrics.ActiveDay, MeteringMetrics.ActiveMonth, MeteringMetrics.NotificationSent, MeteringMetrics.TenantMonth }, metrics);
        Assert.Contains(ledger, e => e.Metric == MeteringMetrics.TenantMonth && e.Module == "M1");
        Assert.All(ledger.Where(e => e.Metric == MeteringMetrics.NotificationSent), e => Assert.Equal("Kern", e.Module));

        // Kein Ereignis trägt eine Personenkennung, einen Anzeigenamen oder eine E-Mail (Metering 2.1, A-070).
        var persons = new[] { _t.Admin, _t.Manager, _t.MemberA, _t.MemberB }.Select(p => p.Value.ToString("D")).ToList();
        foreach (var entry in ledger)
        {
            Assert.All(persons, p => Assert.DoesNotContain(p, entry.SubjectRef + entry.IdempotencyKey, StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain("@", entry.SubjectRef, StringComparison.Ordinal);
            Assert.DoesNotContain("Ledger Kiosk", entry.SubjectRef, StringComparison.Ordinal);
        }

        Assert.All(ledger.Where(e => e.Metric is MeteringMetrics.ActiveMonth or MeteringMetrics.ActiveDay), e => Assert.StartsWith("slot:", e.SubjectRef, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Ereignis_nach_Versiegelung_steht_in_der_offenen_Periode_mit_Nachlauf_die_versiegelte_Periode_bleibt_unveraendert()
    {
        var start = pg.Clock.GetUtcNow();
        try
        {
            pg.Clock.Set(Stufe7.MonthStart.AddDays(20));
            var challenge = await Stufe7.RunningChallengeAsync(pg, _t, "Nachlauf", Ct);
            using var member = await Stufe7.FreshClientAsync(pg, _t.Id, _t.MemberA, Ct);
            await Stufe7.ContributeAsync(member, challenge, pg.Clock.GetUtcNow(), Guid.CreateVersion7().ToString("D"), Ct);

            // Versiegelung über den zeitgesteuerten Lauf (Metering 6.1 Nr. 1) am 3. Oktober nach 03:00 Wien.
            pg.Clock.Set(Stufe7.AfterSealing);
            await Stufe6.RunTaskAsync(pg.Api.Services, "metering.seal", Ct);
            var periods = await Stufe7.PeriodsAsync(pg, _t.Id, Ct);
            Assert.NotNull(periods.Single(p => p.Period == "2026-09").SealedAt);
            Assert.Null(periods.Single(p => p.Period == "2026-10").SealedAt);
            using var admin = await Stufe7.FreshClientAsync(pg, _t.Id, _t.Admin, Ct);
            var septemberBefore = await Stufe7.GetAsync<UsageResponse>(admin, "/api/billing/usage?period=2026-09", Ct);
            Assert.True(septemberBefore.Sealed);
            var invoices = await Stufe7.GetAsync<List<InvoiceSummaryResponse>>(admin, "/api/billing/invoices", Ct);
            Assert.Single(invoices, i => i.Period == "2026-09");

            // Nachläufer: Handlung mit fachlichem Zeitpunkt im September trifft nach der Versiegelung ein (Offline-Nachfrist überschritten).
            var lateAt = new DateTimeOffset(2026, 9, 28, 10, 0, 0, TimeSpan.Zero);
            await Stufe6.Scopes(pg).RunAsync(TenantContext.ForTenant(_t.Id), (sp, c) => sp.GetRequiredService<IActivityRecorder>().RecordAsync(_t.MemberB, ActivityKinds.CheckIn, ActivitySource.Self, lateAt, c), Ct);

            var late = (await Stufe7.LedgerAsync(pg, _t.Id, MeteringMetrics.ActiveMonth, Ct)).Single(e => e.OccurredAt == lateAt);
            Assert.True(late.Late);
            Assert.Equal("2026-10", late.Period);
            Assert.Equal(lateAt, late.OccurredAt);

            var septemberAfter = await Stufe7.GetAsync<UsageResponse>(admin, "/api/billing/usage?period=2026-09", Ct);
            Assert.Equal(System.Text.Json.JsonSerializer.Serialize(septemberBefore), System.Text.Json.JsonSerializer.Serialize(septemberAfter));
            var october = await Stufe7.GetAsync<UsageResponse>(admin, "/api/billing/usage?period=2026-10", Ct);
            Assert.Equal("1.0000", october.Metrics.Single(m => m.Metric == MeteringMetrics.ActiveMonth).Total);
        }
        finally
        {
            pg.Clock.Set(start);
        }
    }
}
