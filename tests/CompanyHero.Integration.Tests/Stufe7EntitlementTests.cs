using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CompanyHero.Modules.Challenges.Api;
using CompanyHero.Modules.Entitlements.Api;
using CompanyHero.Modules.Identity.Api;
using CompanyHero.Modules.Metering.Domain;
using CompanyHero.Modules.Progress.Api;
using CompanyHero.Platform.Entitlements;

namespace CompanyHero.Integration.Tests;

/// <summary>Entitlements 8.1 bis 8.5, 8.8, 8.10 (A-064 bis A-068): unsichtbare Module, Bündelvorschlag, Kaskade, Rücknahme, Exportfrist, Grenzwert, Wirkung beim nächsten Request.</summary>
[Collection(PostgresTests.Name)]
public sealed class Stufe7EntitlementTests(PostgresFixture pg)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Tenant_nur_mit_Kern_zwei_Bereiche_keine_Hinweise_auf_Module_Modulendpunkt_nicht_gefunden()
    {
        var t = await pg.CreateScratchTenantWithMembersAsync($"Kern {Guid.NewGuid():N}");
        await Stufe7.WithoutModulesAsync(pg, t.Id, Ct);
        using var member = pg.Client(t.Id, t.MemberA);
        using var admin = pg.Client(t.Id, t.Admin);
        using var manager = pg.Client(t.Id, t.Manager);

        var navigation = await Stufe7.GetAsync<NavigationResponse>(member, "/api/entitlements/me", Ct);
        Assert.Equal(["start", "ich"], navigation.Areas);
        Assert.Empty(navigation.Modules);
        var raw = await (await member.GetAsync(new Uri("/api/entitlements/me", UriKind.Relative), Ct)).Content.ReadAsStringAsync(Ct);
        Assert.DoesNotContain("M1", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("lock", raw, StringComparison.OrdinalIgnoreCase);

        // API-Aufrufe des Moduls liefern „nicht gefunden“, nie „verboten“ (A-067), für Mitglied, Programm-Manager, Tenant-Admin und Kiosk.
        Assert.Equal(HttpStatusCode.NotFound, await Stufe7.StatusAsync(member, HttpMethod.Get, "/api/challenges", null, Ct));
        Assert.Equal(HttpStatusCode.NotFound, await Stufe7.StatusAsync(manager, HttpMethod.Post, "/api/challenges/kickoff", null, Ct));
        Assert.Equal(HttpStatusCode.NotFound, await Stufe7.StatusAsync(manager, HttpMethod.Get, "/api/challenges/manage", null, Ct));
        // Der Tenant-Export bleibt in den 90 Tagen nach der Deaktivierung lesbar (Entitlements 4.3), alles andere ist „nicht gefunden“.
        Assert.Equal(HttpStatusCode.OK, await Stufe7.StatusAsync(admin, HttpMethod.Get, "/api/challenges/export", null, Ct));
        Assert.Equal(HttpStatusCode.NotFound, await Stufe7.StatusAsync(admin, HttpMethod.Post, "/api/challenges", new ChallengeDraftRequest("x", null, ChallengeMetricDto.Checkmark, "1", pg.Clock.GetUtcNow(), pg.Clock.GetUtcNow().AddDays(7), ChallengeVisibilityDto.Company), Ct));
        using var kiosk = (await KioskTests.RegisterKioskAsync(pg, t, Ct)).Kiosk;
        using var kioskCards = await kiosk.GetAsync("/api/challenges", Ct);
        Assert.Equal(HttpStatusCode.NotFound, kioskCards.StatusCode);
        // Der Kern bleibt: Feed, Fortschritt, Marke.
        Assert.Equal(HttpStatusCode.OK, await Stufe7.StatusAsync(member, HttpMethod.Get, "/api/feed", null, Ct));
        Assert.Equal(HttpStatusCode.OK, await Stufe7.StatusAsync(member, HttpMethod.Get, "/api/me/progress", null, Ct));
        // Der Katalog der Verwaltung kennt das Modul mit Datenschutzhinweis (Entitlements 4.2 Nr. 1), inaktiv.
        var catalog = await Stufe7.GetAsync<List<ModuleStatusResponse>>(admin, "/api/entitlements/modules", Ct);
        Assert.Equal(12, catalog.Count);
        Assert.Equal(EntitlementStateDto.Inactive, catalog.Single(m => m.Module == "M1").State);
        Assert.Equal("modul.m1.datenschutz", catalog.Single(m => m.Module == "M1").PrivacyHintKey);
    }

    [Fact]
    public async Task Buchung_von_M2_ohne_M1_ergibt_Buendelvorschlag_Buchung_beider_erweitert_Navigation_beim_naechsten_Request_und_erzeugt_zwei_Metering_Ereignisse()
    {
        var t = await pg.CreateScratchTenantWithMembersAsync($"Bündel {Guid.NewGuid():N}");
        await Stufe7.WithoutModulesAsync(pg, t.Id, Ct);
        using var admin = pg.Client(t.Id, t.Admin);
        using var member = pg.Client(t.Id, t.MemberA);
        var before = (await Stufe7.LedgerAsync(pg, t.Id, MeteringMetrics.TenantMonth, Ct)).Count;

        using var m2 = await admin.PostAsync(new Uri("/api/entitlements/modules/M2/book", UriKind.Relative), null, Ct);
        Assert.Equal(HttpStatusCode.OK, m2.StatusCode);
        var suggested = (await m2.Content.ReadFromJsonAsync<BookingResponse>(Stufe6.Json, Ct))!;
        Assert.Equal("bundle_suggested", suggested.Outcome);
        Assert.Equal(["M1", "M2"], suggested.Suggestion);
        Assert.Empty(suggested.Booked);
        Assert.Equal(["start", "ich"], (await Stufe7.GetAsync<NavigationResponse>(member, "/api/entitlements/me", Ct)).Areas);
        Assert.Equal(before, (await Stufe7.LedgerAsync(pg, t.Id, MeteringMetrics.TenantMonth, Ct)).Count);

        using var bundle = await admin.PostAsJsonAsync("/api/entitlements/bundles/book", new BundleRequest(suggested.Suggestion), Ct);
        Assert.Equal(HttpStatusCode.OK, bundle.StatusCode);
        var booked = (await bundle.Content.ReadFromJsonAsync<BookingResponse>(Stufe6.Json, Ct))!;
        Assert.Equal("booked", booked.Outcome);
        Assert.Equal(["M1", "M2"], booked.Booked);

        // Wirkung spätestens beim nächsten Request (Entitlements 8.10), nicht erst nach 60 Sekunden.
        var navigation = await Stufe7.GetAsync<NavigationResponse>(member, "/api/entitlements/me", Ct);
        Assert.Equal(["start", "challenges", "ich"], navigation.Areas);
        Assert.Equal(["M1", "M2"], navigation.Modules);
        Assert.Equal(HttpStatusCode.OK, await Stufe7.StatusAsync(member, HttpMethod.Get, "/api/challenges", null, Ct));

        var months = (await Stufe7.LedgerAsync(pg, t.Id, MeteringMetrics.TenantMonth, Ct)).Skip(before).ToList();
        Assert.Equal(2, months.Count);
        Assert.Equal(["M1", "M2"], months.Select(e => e.Module).Order().ToList());
        Assert.All(months, e => Assert.Equal("module:" + e.Module, e.SubjectRef));

        // Historie append-only mit Rolle und Grund (Entitlements 3.1).
        var history = await Stufe7.GetAsync<List<EntitlementHistoryResponse>>(admin, "/api/entitlements/history", Ct);
        Assert.Contains(history, h => h.Module == "M1" && h.Reason == "preset");
        Assert.Contains(history, h => h.Module == "M1" && h.Reason.StartsWith("forced:", StringComparison.Ordinal));
        Assert.Contains(history, h => h.Module == "M2" && h.Reason == "booked" && h.ActorRoles.Contains("tenant_admin", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Kuendigung_von_M1_mit_aktivem_M2_laesst_beide_auslaufen_Ruecknahme_stellt_beide_wieder_her_nach_aktiv_bis_Bereich_weg_Export_90_Tage_Abzeichen_bleiben()
    {
        var start = pg.Clock.GetUtcNow();
        try
        {
            pg.Clock.Set(Stufe7.MonthStart.AddDays(4));
            var t = await pg.CreateScratchTenantWithMembersAsync($"Kaskade {Guid.NewGuid():N}");
            using var admin = pg.Client(t.Id, t.Admin);
            using var member = pg.Client(t.Id, t.MemberA);
            using var m2 = await admin.PostAsync(new Uri("/api/entitlements/modules/M2/book", UriKind.Relative), null, Ct);
            Assert.Equal("booked", (await m2.Content.ReadFromJsonAsync<BookingResponse>(Stufe6.Json, Ct))!.Outcome);

            // Ein verdientes Abzeichen vor der Kündigung (Fortschritt ist Kern).
            using var checkIn = await member.PostAsJsonAsync("/api/me/check-in", new CheckInRequest([CheckInTileDto.Moved]), Ct);
            Assert.Equal(HttpStatusCode.Created, checkIn.StatusCode);
            var progress = await Stufe7.GetAsync<PersonalProgressResponse>(member, "/api/me/progress", Ct);
            Assert.NotEmpty(progress.Earned);
            var challenge = await Stufe7.RunningChallengeAsync(pg, t, "Kaskade", Ct);
            await Stufe7.ContributeAsync(member, challenge, pg.Clock.GetUtcNow(), Guid.CreateVersion7().ToString("D"), Ct);

            using var cancel = await admin.PostAsync(new Uri("/api/entitlements/modules/M1/cancel", UriKind.Relative), null, Ct);
            var cancellation = (await cancel.Content.ReadFromJsonAsync<CancellationResponse>(Stufe6.Json, Ct))!;
            Assert.Equal(["M1", "M2"], cancellation.Affected);
            var activeUntil = new DateTimeOffset(2026, 9, 30, 22, 0, 0, TimeSpan.Zero);
            Assert.Equal(activeUntil, cancellation.ActiveUntil);
            var modules = await Stufe7.GetAsync<List<ModuleStatusResponse>>(admin, "/api/entitlements/modules", Ct);
            Assert.All(new[] { "M1", "M2" }, m => Assert.Equal(EntitlementStateDto.Expiring, modules.Single(x => x.Module == m).State));
            Assert.All(new[] { "M1", "M2" }, m => Assert.Equal(activeUntil, modules.Single(x => x.Module == m).ActiveUntil));
            // Volle Nutzung bis zum Termin.
            Assert.Equal(HttpStatusCode.OK, await Stufe7.StatusAsync(member, HttpMethod.Get, "/api/challenges", null, Ct));

            using var revoke = await admin.PostAsync(new Uri("/api/entitlements/modules/M1/revoke-cancellation", UriKind.Relative), null, Ct);
            Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);
            Assert.Equal(["M1", "M2"], (await revoke.Content.ReadFromJsonAsync<CancellationResponse>(Stufe6.Json, Ct))!.Affected);
            modules = await Stufe7.GetAsync<List<ModuleStatusResponse>>(admin, "/api/entitlements/modules", Ct);
            Assert.All(new[] { "M1", "M2" }, m => Assert.Equal(EntitlementStateDto.Active, modules.Single(x => x.Module == m).State));
            Assert.All(new[] { "M1", "M2" }, m => Assert.Null(modules.Single(x => x.Module == m).ActiveUntil));

            // Erneut kündigen; nach aktiv_bis: Bereich weg, Modulendpunkte „nicht gefunden“, Export 90 Tage, Abzeichen bleiben.
            using var cancelAgain = await admin.PostAsync(new Uri("/api/entitlements/modules/M1/cancel", UriKind.Relative), null, Ct);
            Assert.Equal(HttpStatusCode.OK, cancelAgain.StatusCode);
            pg.Clock.Set(activeUntil.AddHours(3));
            using var memberOct = await Stufe7.FreshClientAsync(pg, t.Id, t.MemberA, Ct);
            using var adminOct = await Stufe7.FreshClientAsync(pg, t.Id, t.Admin, Ct);
            Assert.Equal(["start", "ich"], (await Stufe7.GetAsync<NavigationResponse>(memberOct, "/api/entitlements/me", Ct)).Areas);
            Assert.Equal(HttpStatusCode.NotFound, await Stufe7.StatusAsync(memberOct, HttpMethod.Get, "/api/challenges", null, Ct));
            Assert.Equal(HttpStatusCode.NotFound, await Stufe7.StatusAsync(memberOct, HttpMethod.Get, $"/api/challenges/{challenge:D}", null, Ct));
            var export = await Stufe7.GetAsync<ChallengeExportResponse>(adminOct, "/api/challenges/export", Ct);
            Assert.Contains(export.Challenges, c => c.ChallengeId == challenge.ToString("D"));
            var progressLater = await Stufe7.GetAsync<PersonalProgressResponse>(memberOct, "/api/me/progress", Ct);
            Assert.Equal(progress.Earned.Select(b => b.Key).ToList(), progressLater.Earned.Select(b => b.Key).ToList());

            pg.Clock.Set(activeUntil.AddDays(91));
            using var adminLater = await Stufe7.FreshClientAsync(pg, t.Id, t.Admin, Ct);
            Assert.Equal(HttpStatusCode.NotFound, await Stufe7.StatusAsync(adminLater, HttpMethod.Get, "/api/challenges/export", null, Ct));
        }
        finally
        {
            pg.Clock.Set(start);
        }
    }

    [Fact]
    public async Task Grenzwert_Kiosk_Geraete_blockiert_mit_Hinweis_ohne_Metering_Wirkung_Operator_erhoeht()
    {
        var t = await pg.CreateScratchTenantWithMembersAsync($"Grenze {Guid.NewGuid():N}");
        await Stufe7.SetLimitAsync(pg, t.Id, TenantLimitNames.KioskDevices, 1, Ct);
        using var admin = pg.Client(t.Id, t.Admin);
        var kioskEvents = (await Stufe7.LedgerAsync(pg, t.Id, MeteringMetrics.KioskDeviceMonth, Ct)).Count;

        using var first = await admin.PostAsJsonAsync("/api/access/kiosk-devices", new CreateKioskDeviceRequest("Halle 1"), Ct);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        using var second = await admin.PostAsJsonAsync("/api/access/kiosk-devices", new CreateKioskDeviceRequest("Halle 2"), Ct);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, second.StatusCode);
        Assert.Equal("kiosk_device_limit", JsonDocument.Parse(await second.Content.ReadAsStringAsync(Ct)).RootElement.GetProperty("detail").GetString());
        Assert.Equal(kioskEvents, (await Stufe7.LedgerAsync(pg, t.Id, MeteringMetrics.KioskDeviceMonth, Ct)).Count);

        await Stufe7.SetLimitAsync(pg, t.Id, TenantLimitNames.KioskDevices, 2, Ct);
        using var third = await admin.PostAsJsonAsync("/api/access/kiosk-devices", new CreateKioskDeviceRequest("Halle 2"), Ct);
        Assert.Equal(HttpStatusCode.Created, third.StatusCode);
    }
}
