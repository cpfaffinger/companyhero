using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CompanyHero.Modules.Entitlements.Api;
using CompanyHero.Modules.Metering.Api;
using CompanyHero.Modules.Metering.Domain;
using CompanyHero.Modules.Organisation.Domain;
using CompanyHero.Platform.Tenancy;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyHero.Integration.Tests;

/// <summary>Metering 9.4 bis 9.6, 9.8, 9.12 (A-072 bis A-074): Beispielplan nachrechenbar, Proratierung, Testphase, Verbrauch nur als Tagesaggregate, Rollen ohne Kosten.</summary>
[Collection(PostgresTests.Name)]
public sealed class Stufe7BillingTests(PostgresFixture pg)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static decimal D(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);

    [Fact]
    public async Task Beispielplan_liefert_einen_Rechnungsentwurf_mit_einzeln_nachrechenbaren_Positionen_und_Rundung_je_Position()
    {
        var start = pg.Clock.GetUtcNow();
        try
        {
            pg.Clock.Set(Stufe7.MonthStart);
            var t = await pg.CreateScratchTenantWithMembersAsync($"Plan {Guid.NewGuid():N}");
            using (var warmup = pg.Client(t.Id, t.MemberA))
            {
                // Erster Kontakt am 1. September: die Voreinstellung M1 gilt für den vollen Monat.
                Assert.Equal(HttpStatusCode.OK, await Stufe7.StatusAsync(warmup, HttpMethod.Get, "/api/entitlements/me", null, Ct));
            }

            pg.Clock.Set(Stufe7.MonthStart.AddDays(1).AddHours(4));
            var challenge = await Stufe7.RunningChallengeAsync(pg, t, "Plan", Ct);
            using var a = await Stufe7.FreshClientAsync(pg, t.Id, t.MemberA, Ct);
            using var b = await Stufe7.FreshClientAsync(pg, t.Id, t.MemberB, Ct);
            // Zwei aktive Mitglieder, drei Teilnehmertage (A an zwei Tagen, B an einem), ein Kiosk-Gerät.
            await Stufe7.ContributeAsync(a, challenge, Stufe7.MonthStart.AddHours(2), Guid.CreateVersion7().ToString("D"), Ct);
            await Stufe7.ContributeAsync(a, challenge, Stufe7.MonthStart.AddDays(1).AddHours(2), Guid.CreateVersion7().ToString("D"), Ct);
            await Stufe7.ContributeAsync(b, challenge, Stufe7.MonthStart.AddHours(3), Guid.CreateVersion7().ToString("D"), Ct);
            await pg.IssueSessionAsync(t.Id, t.Admin, Ct);
            var (kiosk, _) = await KioskTests.RegisterKioskAsync(pg, t, Ct);
            kiosk.Dispose();

            pg.Clock.Set(Stufe7.AfterSealing);
            Assert.Equal(["2026-09"], await Stufe7.SealAsync(pg, t.Id, Ct));
            using var admin = await Stufe7.FreshClientAsync(pg, t.Id, t.Admin, Ct);
            var list = await Stufe7.GetAsync<List<InvoiceSummaryResponse>>(admin, "/api/billing/invoices", Ct);
            var draft = await Stufe7.GetAsync<InvoiceDraftResponse>(admin, $"/api/billing/invoices/{list.Single().InvoiceId}", Ct);

            Assert.Equal("2026-09", draft.Period);
            Assert.Equal("draft", draft.Status);
            Assert.Equal("EUR", draft.Currency);
            Assert.Equal("0.2000", draft.TaxRate);
            var flat = draft.Lines.Single(l => l.RuleKey == "m1.flat");
            Assert.Equal(("1.0000", "49.00", "9.80"), (flat.Quantity, flat.Net, flat.Tax));
            var active = draft.Lines.Single(l => l.RuleKey == "kern.active_member");
            Assert.Equal(("2.0000", "5.00"), (active.Quantity, active.Net));
            var days = draft.Lines.Single(l => l.RuleKey == "m1.participant_day");
            Assert.Equal(("3.0000", "0.60", "3 × 0.2"), (days.Quantity, days.Net, days.Calculation));
            var kioskLine = draft.Lines.Single(l => l.RuleKey == "kern.kiosk");
            Assert.Equal(("1.0000", "5.00"), (kioskLine.Quantity, kioskLine.Net));
            // Modifikatoren: Mindestbetrag 30 nicht wirksam (59,60), Pilotrabatt 10 % = 5,96; Steuer je Position, Rundung je Position.
            Assert.DoesNotContain(draft.Lines, l => l.Model == "min_max");
            var credit = draft.Lines.Single(l => l.Model == "credit");
            Assert.Equal("-5.96", credit.Net);
            Assert.Equal("53.64", draft.Net);
            Assert.Equal("10.73", draft.Tax);
            Assert.Equal("64.37", draft.Gross);
            var positions = draft.Lines.Where(l => l.Model != "revenue_share").ToList();
            Assert.Equal(D(draft.Net), positions.Sum(l => D(l.Net)));
            Assert.Equal(D(draft.Tax), positions.Sum(l => D(l.Tax)));
            Assert.All(positions, l => Assert.Equal(decimal.Round(D(l.Net) * 0.2m, 2, MidpointRounding.AwayFromZero), D(l.Tax)));
            // Provision der Ebene darüber (20 % der Nettosumme) steht als eigene Zeile, nicht in der Tenant-Summe.
            var share = draft.Lines.Single(l => l.Model == "revenue_share");
            Assert.Equal("10.73", share.Net);
            Assert.Equal("0.00", draft.WouldHaveBeen);
            // Dezimalwerte im Vertrag als Strings mit definierter Präzision (K13).
            Assert.All(draft.Lines, l => Assert.Matches(@"^-?\d+\.\d{4}$", l.Quantity));
            Assert.All(draft.Lines, l => Assert.Matches(@"^-?\d+\.\d{2}$", l.Net));
        }
        finally
        {
            pg.Clock.Set(start);
        }
    }

    [Fact]
    public async Task Buchung_am_16_eines_30_Tage_Monats_halbe_Flatrate_Kuendigung_am_10_voller_Monat_mit_Zugang_bis_Monatsende()
    {
        var start = pg.Clock.GetUtcNow();
        try
        {
            pg.Clock.Set(Stufe7.MonthStart);
            var t = await pg.CreateScratchTenantWithMembersAsync($"Prorata {Guid.NewGuid():N}");
            using (var warmup = pg.Client(t.Id, t.MemberA))
            {
                Assert.Equal(HttpStatusCode.OK, await Stufe7.StatusAsync(warmup, HttpMethod.Get, "/api/challenges", null, Ct));
            }

            // Kündigung am 10.: wirksam zum Monatsende in Wien (30.09. 22:00 UTC), voller Monat bewertet, Zugang bis dahin.
            pg.Clock.Set(new DateTimeOffset(2026, 9, 10, 9, 0, 0, TimeSpan.Zero));
            using var admin10 = await Stufe7.FreshClientAsync(pg, t.Id, t.Admin, Ct);
            using var cancel = await admin10.PostAsync(new Uri("/api/entitlements/modules/M1/cancel", UriKind.Relative), null, Ct);
            Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
            var cancellation = (await cancel.Content.ReadFromJsonAsync<CancellationResponse>(Stufe6.Json, Ct))!;
            Assert.Equal(["M1"], cancellation.Affected);
            Assert.Equal(new DateTimeOffset(2026, 9, 30, 22, 0, 0, TimeSpan.Zero), cancellation.ActiveUntil);

            pg.Clock.Set(new DateTimeOffset(2026, 9, 20, 9, 0, 0, TimeSpan.Zero));
            using var member20 = await Stufe7.FreshClientAsync(pg, t.Id, t.MemberA, Ct);
            Assert.Equal(HttpStatusCode.OK, await Stufe7.StatusAsync(member20, HttpMethod.Get, "/api/challenges", null, Ct));
            var modules = await Stufe7.GetAsync<List<ModuleStatusResponse>>(await Stufe7.FreshClientAsync(pg, t.Id, t.Admin, Ct), "/api/entitlements/modules", Ct);
            Assert.Equal(EntitlementStateDto.Expiring, modules.Single(m => m.Module == "M1").State);

            // Buchung am 16. eines Monats mit 30 Tagen: Flatrate zur Hälfte (15 von 30 Tagen).
            pg.Clock.Set(new DateTimeOffset(2026, 9, 16, 10, 0, 0, TimeSpan.Zero));
            using var admin16 = await Stufe7.FreshClientAsync(pg, t.Id, t.Admin, Ct);
            var simulation = await Stufe7.GetAsync<SimulationResponse>(admin16, "/api/billing/preview/simulate?module=M3", Ct);
            Assert.Equal(("14.50", "29.00"), (simulation.ThisMonth, simulation.NextMonth));
            using var book = await admin16.PostAsync(new Uri("/api/entitlements/modules/M3/book", UriKind.Relative), null, Ct);
            Assert.Equal(HttpStatusCode.OK, book.StatusCode);
            var months = await Stufe7.LedgerAsync(pg, t.Id, MeteringMetrics.TenantMonth, Ct);
            Assert.Equal(0.5m, months.Single(e => e.Module == "M3").Quantity);
            Assert.Equal(1m, months.Single(e => e.Module == "M1").Quantity);

            var preview = await Stufe7.GetAsync<CostPreviewResponse>(admin16, "/api/billing/preview", Ct);
            Assert.Equal("2026-09", preview.Period);
            Assert.Equal(("1.0000", "49.00"), (preview.Lines.Single(l => l.RuleKey == "m1.flat").Quantity, preview.Lines.Single(l => l.RuleKey == "m1.flat").Net));
            Assert.Equal(("0.5000", "14.50", "0.5 × 29"), (preview.Lines.Single(l => l.RuleKey == "m3.flat").Quantity, preview.Lines.Single(l => l.RuleKey == "m3.flat").Net, preview.Lines.Single(l => l.RuleKey == "m3.flat").Calculation));
            Assert.True(preview.Forecast.Estimate);
            Assert.Equal((16, 30), (preview.Forecast.ElapsedDays, preview.Forecast.DaysInMonth));
            Assert.Contains(preview.Definitions, d => d.Metric == MeteringMetrics.ActiveMonth && d.TextKey == "metrik.member_active_month" && d.Personal);

            // Nach aktiv_bis: Bereich weg, Folgemonat ohne M1, M3 voll.
            pg.Clock.Set(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero));
            using var memberOct = await Stufe7.FreshClientAsync(pg, t.Id, t.MemberA, Ct);
            Assert.Equal(HttpStatusCode.NotFound, await Stufe7.StatusAsync(memberOct, HttpMethod.Get, "/api/challenges", null, Ct));
            await Stufe6.RunTaskAsync(pg.Api.Services, "entitlements.transitions", Ct);
            var october = (await Stufe7.LedgerAsync(pg, t.Id, MeteringMetrics.TenantMonth, Ct)).Where(e => e.Period == "2026-10").ToList();
            Assert.Single(october);
            Assert.Equal(("M3", 1m), (october[0].Module, october[0].Quantity));
        }
        finally
        {
            pg.Clock.Set(start);
        }
    }

    [Fact]
    public async Task Testphase_erfasst_Ereignisse_mit_Betrag_null_zeigt_waeren_X_Euro_gewesen_und_geht_proratiert_in_aktiv_ueber()
    {
        var start = pg.Clock.GetUtcNow();
        try
        {
            pg.Clock.Set(new DateTimeOffset(2026, 9, 16, 8, 0, 0, TimeSpan.Zero));
            var t = await pg.CreateScratchTenantWithMembersAsync($"Test {Guid.NewGuid():N}");
            await Stufe7.WithoutModulesAsync(pg, t.Id, Ct);
            using var admin = pg.Client(t.Id, t.Admin);
            using var trial = await admin.PostAsync(new Uri("/api/entitlements/modules/M1/trial", UriKind.Relative), null, Ct);
            Assert.Equal(HttpStatusCode.OK, trial.StatusCode);
            var started = (await trial.Content.ReadFromJsonAsync<TrialResponse>(Stufe6.Json, Ct))!;
            Assert.Equal("started", started.Outcome);
            Assert.Equal(new DateTimeOffset(2026, 10, 16, 8, 0, 0, TimeSpan.Zero), started.TrialUntil);

            // Nutzung voll: Challenge und Beiträge; Ereignisse erfasst, nicht bewertet.
            var challenge = await Stufe7.RunningChallengeAsync(pg, t, "Testphase", Ct);
            using var member = pg.Client(t.Id, t.MemberA);
            await Stufe7.ContributeAsync(member, challenge, pg.Clock.GetUtcNow(), Guid.CreateVersion7().ToString("D"), Ct);
            var ledger = await Stufe7.LedgerAsync(pg, t.Id, null, Ct);
            Assert.All(ledger.Where(e => e.Module == "M1" && e.Metric != MeteringMetrics.TenantMonth), e => Assert.False(e.Rated));
            Assert.Contains(ledger, e => e.Metric == MeteringMetrics.TrialDay && e.Module == "M1");
            Assert.All(ledger.Where(e => e.Module == "Kern"), e => Assert.True(e.Rated));

            var preview = await Stufe7.GetAsync<CostPreviewResponse>(admin, "/api/billing/preview", Ct);
            var trialLine = preview.Lines.Single(l => l.RuleKey == "m1.participant_day");
            Assert.False(trialLine.Rated);
            Assert.Equal("0.00", trialLine.Net);
            Assert.Contains("Testphase", trialLine.Calculation, StringComparison.Ordinal);
            // M1 war vor der Testphase einen Tag als Voreinstellung aktiv (16.09.): 0,5 gebucht, 14/30 gegengebucht, bewertet bleibt ein Tag.
            Assert.Equal("0.0333", preview.Lines.Single(l => l.RuleKey == "m1.flat").Quantity);
            // „In der Testphase wären das X Euro gewesen“: ein Teilnehmertag × 0,20.
            Assert.Equal("0.20", preview.WouldHaveBeen);
            Assert.Equal(D(preview.Net), preview.Lines.Where(l => l.Model != "revenue_share").Sum(l => D(l.Net)));

            // Zweite Testphase desselben Moduls abgelehnt, auch nach Kündigung in der Testphase (kostenfrei zum Testende).
            using var cancel = await admin.PostAsync(new Uri("/api/entitlements/modules/M1/cancel", UriKind.Relative), null, Ct);
            Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
            Assert.Equal(started.TrialUntil, (await cancel.Content.ReadFromJsonAsync<CancellationResponse>(Stufe6.Json, Ct))!.ActiveUntil);
            using var revoke = await admin.PostAsync(new Uri("/api/entitlements/modules/M1/revoke-cancellation", UriKind.Relative), null, Ct);
            Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);

            // Übergang in aktiv am Testende (16.10.): Bewertung ab Testende, tagesgenau 16 von 31 Tagen.
            pg.Clock.Set(new DateTimeOffset(2026, 10, 16, 9, 0, 0, TimeSpan.Zero));
            await Stufe6.RunTaskAsync(pg.Api.Services, "entitlements.transitions", Ct);
            var months = await Stufe7.LedgerAsync(pg, t.Id, MeteringMetrics.TenantMonth, Ct);
            var activation = months.Single(e => e.Module == "M1" && e.Period == "2026-10");
            Assert.Equal(("2026-10", 0.5161m, true), (activation.Period, activation.Quantity, activation.Rated));
            // 30 Tage Testphase ab 16.09. 10:00 Wien berühren 31 Kalendertage (16.09. bis 16.10.); je Tag ein Testtag, nie bewertet.
            var trialDays = await Stufe7.LedgerAsync(pg, t.Id, MeteringMetrics.TrialDay, Ct);
            Assert.Equal(31, trialDays.Count);
            Assert.All(trialDays, e => Assert.False(e.Rated));
            using var adminLater = await Stufe7.FreshClientAsync(pg, t.Id, t.Admin, Ct);
            var modules = await Stufe7.GetAsync<List<ModuleStatusResponse>>(adminLater, "/api/entitlements/modules", Ct);
            Assert.Equal(EntitlementStateDto.Active, modules.Single(m => m.Module == "M1").State);
            Assert.False(modules.Single(m => m.Module == "M1").TrialAvailable);
            using var second = await adminLater.PostAsync(new Uri("/api/entitlements/modules/M1/trial", UriKind.Relative), null, Ct);
            Assert.Equal("already_active", (await second.Content.ReadFromJsonAsync<TrialResponse>(Stufe6.Json, Ct))!.Outcome);

            // Kündigung in der Testphase eines anderen Moduls: kostenfrei zum Testende, danach keine zweite Testphase.
            using var trialM3 = await adminLater.PostAsync(new Uri("/api/entitlements/modules/M3/trial", UriKind.Relative), null, Ct);
            Assert.Equal("started", (await trialM3.Content.ReadFromJsonAsync<TrialResponse>(Stufe6.Json, Ct))!.Outcome);
            using var cancelM3 = await adminLater.PostAsync(new Uri("/api/entitlements/modules/M3/cancel", UriKind.Relative), null, Ct);
            Assert.Equal(HttpStatusCode.OK, cancelM3.StatusCode);
            pg.Clock.Set(new DateTimeOffset(2026, 11, 16, 9, 0, 0, TimeSpan.Zero));
            await Stufe6.RunTaskAsync(pg.Api.Services, "entitlements.transitions", Ct);
            Assert.DoesNotContain(await Stufe7.LedgerAsync(pg, t.Id, MeteringMetrics.TenantMonth, Ct), e => e.Module == "M3");
            using var adminNov = await Stufe7.FreshClientAsync(pg, t.Id, t.Admin, Ct);
            using var secondM3 = await adminNov.PostAsync(new Uri("/api/entitlements/modules/M3/trial", UriKind.Relative), null, Ct);
            Assert.Equal("trial_used", (await secondM3.Content.ReadFromJsonAsync<TrialResponse>(Stufe6.Json, Ct))!.Outcome);
        }
        finally
        {
            pg.Clock.Set(start);
        }
    }

    [Fact]
    public async Task Verbrauchsdetail_nur_Tagesaggregate_personennahe_Metriken_nur_als_Summe_Einsichtsrolle_ohne_Betraege_Programm_Manager_ohne_Kosten()
    {
        var t = await pg.CreateScratchTenantWithMembersAsync($"Verbrauch {Guid.NewGuid():N}");
        var challenge = await Stufe7.RunningChallengeAsync(pg, t, "Verbrauch", Ct);
        using var member = pg.Client(t.Id, t.MemberA);
        using var manager = pg.Client(t.Id, t.Manager);
        using var admin = pg.Client(t.Id, t.Admin);
        var now = pg.Clock.GetUtcNow();
        await Stufe7.ContributeAsync(member, challenge, now, Guid.CreateVersion7().ToString("D"), Ct);
        await Stufe7.ContributeAsync(member, challenge, now.AddDays(-1), Guid.CreateVersion7().ToString("D"), Ct);
        var (kiosk, _) = await KioskTests.RegisterKioskAsync(pg, t, Ct);
        kiosk.Dispose();
        var period = TenantTimeZone.PeriodOf(now);

        var usage = await Stufe7.GetAsync<UsageResponse>(admin, $"/api/billing/usage?period={period}", Ct);
        var participant = usage.Metrics.Single(m => m.Metric == MeteringMetrics.ParticipantDay);
        Assert.True(participant.Personal);
        Assert.Null(participant.Days);
        Assert.Equal("2.0000", participant.Total);
        Assert.Null(usage.Metrics.Single(m => m.Metric == MeteringMetrics.ActiveMonth).Days);
        var kioskMonth = usage.Metrics.Single(m => m.Metric == MeteringMetrics.KioskDeviceMonth);
        Assert.False(kioskMonth.Personal);
        Assert.NotNull(kioskMonth.Days);
        Assert.Single(kioskMonth.Days!);
        var json = JsonSerializer.Serialize(usage);
        Assert.DoesNotContain("slot:", json, StringComparison.Ordinal);
        Assert.DoesNotContain(t.MemberA.Value.ToString("D"), json, StringComparison.OrdinalIgnoreCase);

        using var export = await admin.GetAsync(new Uri($"/api/billing/usage/export?period={period}", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        Assert.Equal("text/csv", export.Content.Headers.ContentType?.MediaType);
        var csv = await export.Content.ReadAsStringAsync(Ct);
        Assert.Contains($"{period};M1;challenge.participant_day;;2.0000", csv, StringComparison.Ordinal);
        Assert.DoesNotContain("slot:", csv, StringComparison.Ordinal);
        Assert.Contains(csv.Split('\n'), line => line.Contains("kiosk.device_month", StringComparison.Ordinal) && line.Split(';')[3].Length == 10);

        // Keine Route liefert Ledger-Einzelzeilen (A-073).
        var routes = pg.Api.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().Select(e => e.RoutePattern.RawText!).ToList();
        Assert.DoesNotContain(routes, r => r.Contains("ledger", StringComparison.OrdinalIgnoreCase));

        // Einsichtsrolle: verwendete Metriken ohne Beträge; Kostenvorschau, Rechnungen, Verbrauch verweigert.
        var insight = await Stufe6.AddPersonAsync(pg, t.Id, "Einsicht", Modules.Privacy.Domain.VisibilityLevel.Company, Ct, Role.Insight);
        using var insightClient = pg.Client(t.Id, insight);
        var metrics = await Stufe7.GetAsync<MetricsResponse>(insightClient, "/api/billing/metrics", Ct);
        Assert.Contains(metrics.Metrics, m => m.Metric == MeteringMetrics.ActiveMonth);
        var metricsJson = await (await insightClient.GetAsync(new Uri("/api/billing/metrics", UriKind.Relative), Ct)).Content.ReadAsStringAsync(Ct);
        Assert.DoesNotContain("\"net\"", metricsJson, StringComparison.Ordinal);
        Assert.DoesNotContain("amount", metricsJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("49", metricsJson, StringComparison.Ordinal);

        var matrix = new (string Name, HttpClient Client, HttpMethod Method, string Path, HttpStatusCode Expected)[]
        {
            ("Einsichtsrolle: Kostenvorschau", insightClient, HttpMethod.Get, "/api/billing/preview", HttpStatusCode.Forbidden),
            ("Einsichtsrolle: Rechnungen", insightClient, HttpMethod.Get, "/api/billing/invoices", HttpStatusCode.Forbidden),
            ("Einsichtsrolle: Verbrauch", insightClient, HttpMethod.Get, $"/api/billing/usage?period={period}", HttpStatusCode.Forbidden),
            ("Einsichtsrolle: Module lesen", insightClient, HttpMethod.Get, "/api/entitlements/modules", HttpStatusCode.OK),
            ("Einsichtsrolle: Historie", insightClient, HttpMethod.Get, "/api/entitlements/history", HttpStatusCode.OK),
            ("Einsichtsrolle: buchen", insightClient, HttpMethod.Post, "/api/entitlements/modules/M3/book", HttpStatusCode.Forbidden),
            ("Programm-Manager: Kostenvorschau", manager, HttpMethod.Get, "/api/billing/preview", HttpStatusCode.Forbidden),
            ("Programm-Manager: Simulation", manager, HttpMethod.Get, "/api/billing/preview/simulate?module=M3", HttpStatusCode.Forbidden),
            ("Programm-Manager: Verbrauch", manager, HttpMethod.Get, $"/api/billing/usage?period={period}", HttpStatusCode.Forbidden),
            ("Programm-Manager: Export", manager, HttpMethod.Get, $"/api/billing/usage/export?period={period}", HttpStatusCode.Forbidden),
            ("Programm-Manager: Rechnungen", manager, HttpMethod.Get, "/api/billing/invoices", HttpStatusCode.Forbidden),
            ("Programm-Manager: Metriken", manager, HttpMethod.Get, "/api/billing/metrics", HttpStatusCode.Forbidden),
            ("Programm-Manager: Module", manager, HttpMethod.Get, "/api/entitlements/modules", HttpStatusCode.Forbidden),
            ("Programm-Manager: buchen", manager, HttpMethod.Post, "/api/entitlements/modules/M3/book", HttpStatusCode.Forbidden),
            ("Mitglied: Kostenvorschau", member, HttpMethod.Get, "/api/billing/preview", HttpStatusCode.Forbidden),
            ("Mitglied: Verbrauch", member, HttpMethod.Get, $"/api/billing/usage?period={period}", HttpStatusCode.Forbidden),
            ("Mitglied: Module", member, HttpMethod.Get, "/api/entitlements/modules", HttpStatusCode.Forbidden),
            ("Mitglied: Navigation", member, HttpMethod.Get, "/api/entitlements/me", HttpStatusCode.OK),
            ("Mitglied: kündigen", member, HttpMethod.Post, "/api/entitlements/modules/M1/cancel", HttpStatusCode.Forbidden),
            ("Tenant-Admin: Kostenvorschau", admin, HttpMethod.Get, "/api/billing/preview", HttpStatusCode.OK),
            ("Tenant-Admin: Rechnungen", admin, HttpMethod.Get, "/api/billing/invoices", HttpStatusCode.OK),
            ("Tenant-Admin: Perioden", admin, HttpMethod.Get, "/api/billing/periods", HttpStatusCode.OK),
            ("Tenant-Admin: Metriken", admin, HttpMethod.Get, "/api/billing/metrics", HttpStatusCode.OK),
            ("Tenant-Admin: Module", admin, HttpMethod.Get, "/api/entitlements/modules", HttpStatusCode.OK),
            ("Tenant-Admin: Export Challenges", admin, HttpMethod.Get, "/api/challenges/export", HttpStatusCode.OK),
            ("Programm-Manager: Export Challenges", manager, HttpMethod.Get, "/api/challenges/export", HttpStatusCode.Forbidden),
        };
        var problems = new List<string>();
        foreach (var (name, client, method, path, expected) in matrix)
        {
            var status = await Stufe7.StatusAsync(client, method, path, null, Ct);
            if (status != expected)
            {
                problems.Add($"{name}: {(int)status} statt {(int)expected}");
            }
        }

        // Operator (Plattformkontext) und Kiosk-Gerätesitzung erreichen die Tenant-Verwaltung nicht.
        using var device = (await KioskTests.RegisterKioskAsync(pg, t, Ct)).Kiosk;
        using var kioskPreview = await device.GetAsync("/api/billing/preview", Ct);
        if (kioskPreview.StatusCode != HttpStatusCode.Forbidden)
        {
            problems.Add($"Kiosk-Gerät: Kostenvorschau {(int)kioskPreview.StatusCode} statt 403");
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }
}
