using System.Net;
using System.Net.Http.Json;
using CompanyHero.Modules.Metering.Domain;
using CompanyHero.Modules.Progress.Api;
using CompanyHero.Modules.Progress.Application;
using CompanyHero.Modules.Progress.Domain;
using CompanyHero.Platform.Metering;
using CompanyHero.Platform.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyHero.Integration.Tests;

/// <summary>Metering 9.2, 9.3 (A-069, A-071): ein aktives Mitglied je Person und Monat, Slots je Periode nicht verkettbar, Salzvernichtung ohne Rückrechnung; Öffnen, Login, Push, automatischer Wert und Kommentar zählen nicht.</summary>
[Collection(PostgresTests.Name)]
public sealed class Stufe7ActiveMemberTests(PostgresFixture pg) : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ScratchTenant _t = null!;

    public async ValueTask InitializeAsync() => _t = await pg.CreateScratchTenantWithMembersAsync($"Aktiv {Guid.NewGuid():N}");

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private Task<Guid> RecordAsync(PersonId person, string kind, ActivitySource source, DateTimeOffset at) =>
        Stufe6.Scopes(pg).RunAsync(TenantContext.ForTenant(_t.Id), (sp, c) => sp.GetRequiredService<IActivityRecorder>().RecordAsync(person, kind, source, at, c), Ct);

    private Task<string> SlotAsync(PersonId person, DateTimeOffset at) =>
        Stufe6.Scopes(pg).RunAsync(TenantContext.ForTenant(_t.Id), (sp, c) => sp.GetRequiredService<IMeteringSlots>().SlotForAsync(person, at, c), Ct);

    [Fact]
    public async Task Zehn_Handlungen_ergeben_ein_active_month_im_Folgemonat_ein_neuer_Slot_nach_Salzvernichtung_keine_Rueckrechnung()
    {
        var start = pg.Clock.GetUtcNow();
        try
        {
            pg.Clock.Set(Stufe7.MonthStart.AddDays(10));
            var person = _t.MemberA;
            for (var i = 0; i < 10; i++)
            {
                await RecordAsync(person, i % 2 == 0 ? ActivityKinds.CheckIn : ActivityKinds.FeedPost, ActivitySource.Self, pg.Clock.GetUtcNow().AddMinutes(i));
            }

            var september = await Stufe7.LedgerAsync(pg, _t.Id, MeteringMetrics.ActiveMonth, Ct);
            Assert.Single(september);
            Assert.Equal("2026-09", september[0].Period);
            var septemberSlot = september[0].SubjectRef;
            Assert.StartsWith("slot:", septemberSlot, StringComparison.Ordinal);
            Assert.Equal(septemberSlot, await SlotAsync(person, pg.Clock.GetUtcNow()));
            Assert.Single(await Stufe7.LedgerAsync(pg, _t.Id, MeteringMetrics.ActiveDay, Ct));

            // Folgemonat: Versiegelung des Septembers, dann eine Handlung im Oktober mit neuem, nicht verkettbarem Slot.
            pg.Clock.Set(Stufe7.AfterSealing);
            Assert.Equal(["2026-09"], await Stufe7.SealAsync(pg, _t.Id, Ct));
            await RecordAsync(person, ActivityKinds.CheckIn, ActivitySource.Self, pg.Clock.GetUtcNow());
            var all = await Stufe7.LedgerAsync(pg, _t.Id, MeteringMetrics.ActiveMonth, Ct);
            Assert.Equal(2, all.Count);
            var octoberSlot = all.Single(e => e.Period == "2026-10").SubjectRef;
            Assert.NotEqual(septemberSlot, octoberSlot);
            // Für die versiegelte Periode gibt es keinen Slot mehr: der Bezug einer Nachlauf-Handlung ist der Slot der offenen Periode.
            Assert.Equal(octoberSlot, await SlotAsync(person, new DateTimeOffset(2026, 9, 20, 10, 0, 0, TimeSpan.Zero)));

            // Sieben Tage nach der Versiegelung wird das Salz vernichtet; danach ist keine Rückrechnung mehr möglich (Metering 2.3).
            pg.Clock.Set(Stufe7.AfterSealing.AddDays(6));
            Assert.Empty(await Stufe7.DestroySaltsAsync(pg, _t.Id, Ct));
            pg.Clock.Set(Stufe7.AfterSealing.AddDays(7));
            Assert.Equal(["2026-09"], await Stufe7.DestroySaltsAsync(pg, _t.Id, Ct));
            var periods = await Stufe7.PeriodsAsync(pg, _t.Id, Ct);
            Assert.False(periods.Single(p => p.Period == "2026-09").SaltAvailable);
            Assert.NotNull(periods.Single(p => p.Period == "2026-09").SaltDestroyedAt);
            Assert.True(periods.Single(p => p.Period == "2026-10").SaltAvailable);
            // Der September-Slot ist im Ledger erhalten, aber ohne Salz für keine Person berechenbar; die offene Periode bleibt zählbar.
            Assert.Equal(septemberSlot, (await Stufe7.LedgerAsync(pg, _t.Id, MeteringMetrics.ActiveMonth, Ct)).Single(e => e.Period == "2026-09").SubjectRef);
            Assert.Equal(octoberSlot, await SlotAsync(person, pg.Clock.GetUtcNow()));
        }
        finally
        {
            pg.Clock.Set(start);
        }
    }

    [Fact]
    public async Task Oeffnen_Login_Push_automatischer_Tageswert_und_Kommentar_erzeugen_kein_aktives_Mitglied()
    {
        var person = _t.MemberB;
        // Login (neue Sitzung) und Öffnen der App (Feed, Fortschritt, Sitzung).
        using var client = await Stufe7.FreshClientAsync(pg, _t.Id, person, Ct);
        foreach (var path in new[] { "/api/auth/session", "/api/feed", "/api/me/progress", "/api/entitlements/me", "/api/challenges" })
        {
            using var response = await client.GetAsync(new Uri(path, UriKind.Relative), Ct);
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{path}: {response.StatusCode}");
        }

        // Empfangene Benachrichtigung (Push über die Zustellpipeline eines Fachereignisses) und automatischer Tageswert, Kommentar.
        await Stufe6.Scopes(pg).RunAsync(TenantContext.ForTenant(_t.Id), async (sp, c) =>
        {
            var metering = sp.GetRequiredService<IMeteringEmitter>();
            await using var tx = await sp.GetRequiredService<Platform.Data.IContextTransaction>().BeginAsync(c);
            await metering.EmitAsync(new MeteringEmission("Kern", MeteringMetrics.NotificationSent, "push:challenge", 1m, MeteringSource.Automatic, pg.Clock.GetUtcNow(), $"ns:{Guid.NewGuid():D}"), c);
            await tx.CommitAsync(c);
        }, Ct);
        await RecordAsync(person, "wearable_day", ActivitySource.Automatic, pg.Clock.GetUtcNow());
        await RecordAsync(person, ActivityKinds.Comment, ActivitySource.Self, pg.Clock.GetUtcNow());

        var ledger = await Stufe7.LedgerAsync(pg, _t.Id, null, Ct);
        Assert.DoesNotContain(ledger, e => e.Metric == MeteringMetrics.ActiveMonth);
        Assert.DoesNotContain(ledger, e => e.Metric == MeteringMetrics.ActiveDay);
        Assert.Contains(ledger, e => e.Metric == MeteringMetrics.NotificationSent);

        // Erst die gewertete Handlung (Check-in) macht die Person zum aktiven Mitglied: genau ein Ereignis.
        using var checkIn = await client.PostAsJsonAsync("/api/me/check-in", new CheckInRequest([CheckInTileDto.Moved]), Ct);
        Assert.Equal(HttpStatusCode.Created, checkIn.StatusCode);
        Assert.Single(await Stufe7.LedgerAsync(pg, _t.Id, MeteringMetrics.ActiveMonth, Ct));
    }
}
