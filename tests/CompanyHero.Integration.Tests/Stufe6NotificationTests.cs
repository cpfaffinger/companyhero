using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CompanyHero.Modules.Identity.Api;
using CompanyHero.Modules.Notifications.Api;
using CompanyHero.Modules.Notifications.Application;
using CompanyHero.Modules.Notifications.Domain;
using CompanyHero.Modules.Notifications.Infrastructure;
using CompanyHero.Platform.Data;
using CompanyHero.Platform.Jobs;
using CompanyHero.Platform.Messaging;
using CompanyHero.Platform.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyHero.Integration.Tests;

/// <summary>Benachrichtigungen 2 bis 8 (A-058 bis A-063): Web Push mit VAPID und verschlüsselter Nutzlast, Kontingent, Ruhezeit, Tenant-Schalter, E-Mail mit Abmeldung, Aushang, Deduplikation.</summary>
[Collection(PostgresTests.Name)]
public sealed class Stufe6NotificationTests(PostgresFixture pg) : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ScratchTenant _t = null!;

    public async ValueTask InitializeAsync() => _t = await pg.CreateScratchTenantWithMembersAsync($"Push {Guid.NewGuid():N}");

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private sealed record Device(ECDiffieHellman Key, string Auth, string Endpoint) : IDisposable
    {
        public void Dispose() => Key.Dispose();
    }

    private static Device NewDevice()
    {
        var key = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        return new Device(key, System.Buffers.Text.Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16)), $"https://push.test/send/{Guid.NewGuid():N}");
    }

    private static async Task<PushSubscriptionResponse> SubscribeAsync(HttpClient client, Device device, CancellationToken ct)
    {
        using var response = await client.PostAsJsonAsync("/api/me/notifications/subscriptions", new PushSubscriptionRequest(device.Endpoint, System.Buffers.Text.Base64Url.EncodeToString(WebPushCrypto.ExportRaw(device.Key.PublicKey)), device.Auth, "Handy"), ct);
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"{response.StatusCode}: {await response.Content.ReadAsStringAsync(ct)}");
        return (await response.Content.ReadFromJsonAsync<PushSubscriptionResponse>(Stufe6.Json, ct))!;
    }

    private Task<T> InNotificationsAsync<T>(Func<NotificationsDbContext, CancellationToken, Task<T>> query) =>
        Stufe6.Scopes(pg).RunAsync(TenantContext.ForTenant(_t.Id), async (sp, ct) =>
        {
            var db = sp.GetRequiredService<NotificationsDbContext>();
            await using var tx = await sp.GetRequiredService<IContextTransaction>().BeginAsync(ct);
            var result = await query(db, ct);
            await tx.CommitAsync(ct);
            return result;
        }, Ct);

    [Fact]
    public async Task Push_Abonnement_verschluesselte_Nutzlast_nur_mit_Referenz_und_410_loescht_das_Abonnement()
    {
        using var a = pg.Client(_t.Id, _t.MemberA);
        using var b = pg.Client(_t.Id, _t.MemberB);
        using var manager = pg.Client(_t.Id, _t.Manager);

        var vapid = await Stufe6.GetAsync<VapidResponse>(a, "/api/notifications/vapid", Ct);
        Assert.True(vapid.Available);
        Assert.Equal(87, vapid.PublicKey!.Length);

        using var admin = pg.Client(_t.Id, _t.Admin);
        await Stufe6.QuietHoursAwayAsync(admin, Ct);
        using var device = NewDevice();
        var subscription = await SubscribeAsync(a, device, Ct);
        Assert.Equal("Handy", subscription.DeviceLabel);
        // Erneute Meldung desselben Endpunkts (pushsubscriptionchange) erzeugt kein zweites Abonnement; der Kiosk abonniert nie.
        await SubscribeAsync(a, device, Ct);
        Assert.Single(await Stufe6.GetAsync<List<PushSubscriptionResponse>>(a, "/api/me/notifications/subscriptions", Ct));
        using var kiosk = pg.KioskClient(await pg.KioskSessionAsync(_t.Id, _t.MemberA, Ct));
        using var kioskSubscribe = await kiosk.PostAsJsonAsync("/api/me/notifications/subscriptions", new PushSubscriptionRequest("https://push.test/kiosk", "AAAA", "BBBB", "Kiosk"), Ct);
        Assert.Equal(HttpStatusCode.Forbidden, kioskSubscribe.StatusCode);

        var challenge = await Stufe6.PlanChallengeAsync(manager, pg.Clock.GetUtcNow(), "Push-Challenge", 4m, Ct);
        var control = new TestJobControl { SubscriberPerson = _t.MemberA };
        using var worker = WorkerHost.Create(pg, control, "w-push");
        await worker.StartAsync(Ct);
        try
        {
            await Stufe6.WaitUntilRunningAsync(worker, a, challenge, Ct);
            await Stufe6.WaitForQueueAsync(pg, _t.Id, Ct);

            // In-App-Eintrag für jedes Mitglied; Push nur an das Abonnement von A, mit Nutzlast { id, kategorie, ziel } (Benachrichtigungen 3.1).
            var entries = await Stufe6.GetAsync<List<NotificationResponse>>(a, "/api/notifications?unreadOnly=true", Ct);
            var started = Assert.Single(entries, e => e.TextKey == "benachrichtigung.challengeGestartet");
            Assert.Contains(await Stufe6.GetAsync<List<NotificationResponse>>(b, "/api/notifications", Ct), e => e.TextKey == "benachrichtigung.challengeGestartet");
            var message = Assert.Single(pg.Push.Sent, m => m.Endpoint.ToString() == device.Endpoint);
            Assert.StartsWith("vapid t=", message.Authorization, StringComparison.Ordinal);
            Assert.Contains(", k=" + vapid.PublicKey, message.Authorization, StringComparison.Ordinal);
            Assert.Equal(86400, message.TimeToLiveSeconds);
            Assert.False(string.IsNullOrEmpty(message.Topic));
            var payload = JsonDocument.Parse(WebPushCrypto.Decrypt(message.Body, device.Key, device.Auth)).RootElement;
            Assert.Equal(["id", "kategorie", "ziel"], payload.EnumerateObject().Select(p => p.Name).Order().ToList());
            Assert.Equal(started.Id, payload.GetProperty("id").GetString());
            Assert.Equal("challenge", payload.GetProperty("kategorie").GetString());
            Assert.Equal("/challenges/" + challenge.ToString("D"), payload.GetProperty("ziel").GetString());
            Assert.DoesNotContain("Mitglied", Encoding.Latin1.GetString(message.Body), StringComparison.Ordinal);

            using var read = await a.PostAsync(new Uri($"/api/notifications/{started.Id}/read", UriKind.Relative), null, Ct);
            Assert.Equal(HttpStatusCode.NoContent, read.StatusCode);
            Assert.DoesNotContain(await Stufe6.GetAsync<List<NotificationResponse>>(a, "/api/notifications?unreadOnly=true", Ct), e => e.Id == started.Id);

            // 410 vom Push-Dienst: das Abonnement wird gelöscht (Benachrichtigungen 3.1).
            pg.Push.Respond(device.Endpoint, 410);
            var now = pg.Clock.GetUtcNow();
            await Stufe6.ContributeAsync(a, challenge, now, Ct);
            await Stufe6.WaitForQueueAsync(pg, _t.Id, Ct);
            Assert.Contains(await Stufe6.GetAsync<List<NotificationResponse>>(a, "/api/notifications", Ct), e => e.TextKey == "benachrichtigung.meilenstein" && e.Params["prozent"] == "25");
            Assert.Empty(await Stufe6.GetAsync<List<PushSubscriptionResponse>>(a, "/api/me/notifications/subscriptions", Ct));
        }
        finally
        {
            await worker.StopAsync(Ct);
        }
    }

    [Fact]
    public async Task Kontingent_drei_Push_je_Tag_Tenant_Schalter_pausiert_und_wiederholter_Job_liefert_nicht_doppelt()
    {
        using var a = pg.Client(_t.Id, _t.MemberA);
        using var b = pg.Client(_t.Id, _t.MemberB);
        using var admin = pg.Client(_t.Id, _t.Admin);
        using var manager = pg.Client(_t.Id, _t.Manager);
        await Stufe6.QuietHoursAwayAsync(admin, Ct);
        using var device = NewDevice();
        await SubscribeAsync(a, device, Ct);

        var challenge = await Stufe6.PlanChallengeAsync(manager, pg.Clock.GetUtcNow(), "Kontingent", 4m, Ct);
        var control = new TestJobControl { SubscriberPerson = _t.MemberA };
        using var worker = WorkerHost.Create(pg, control, "w-quota");
        await worker.StartAsync(Ct);
        try
        {
            await Stufe6.WaitUntilRunningAsync(worker, a, challenge, Ct);
            // Start + Meilensteine 25, 50, 75, 100: fünf Ereignisse, höchstens drei Push, alle fünf In-App (Benachrichtigungen 4.2).
            var now = pg.Clock.GetUtcNow();
            for (var i = 0; i < 4; i++)
            {
                await Stufe6.ContributeAsync(i % 2 == 0 ? a : b, challenge, now.AddMinutes(-i), Ct);
                await Stufe6.WaitForQueueAsync(pg, _t.Id, Ct);
            }

            var entries = await Stufe6.GetAsync<List<NotificationResponse>>(a, "/api/notifications", Ct);
            Assert.Equal(5, entries.Count(e => e.Category == NotificationCategoryDto.Challenge));
            var deliveries = await InNotificationsAsync((db, ct) => db.Deliveries.Where(d => d.PersonId == _t.MemberA && d.Channel == NotificationChannel.Push).ToListAsync(ct));
            Assert.True(deliveries.Count == 3, "Zustellungen: " + string.Join(", ", deliveries.Select(d => $"{d.Status}/{d.Day}/{d.Error}")));
            Assert.All(deliveries, d => Assert.Equal(DeliveryStatus.Sent, d.Status));
            var sentIds = pg.Push.Sent.Where(m => m.Endpoint.ToString() == device.Endpoint).Select(m => JsonDocument.Parse(WebPushCrypto.Decrypt(m.Body, device.Key, device.Auth)).RootElement.GetProperty("id").GetString()).ToList();
            Assert.True(sentIds.Count == 3, "Gesendet: " + string.Join(", ", sentIds));

            // Wiederholter Job desselben Ereignisses: kein zweiter Eintrag, keine zweite Zustellung (Idempotenzschlüssel).
            var eventKey = await InNotificationsAsync((db, ct) => db.Entries.Where(e => e.PersonId == _t.MemberA && e.TextKey == "benachrichtigung.challengeGestartet").Select(e => e.EventKey).SingleAsync(ct));
            var eventId = eventKey[(eventKey.LastIndexOf(':') + 1)..];
            await Stufe6.Scopes(pg).RunAsync(TenantContext.ForTenant(_t.Id), async (sp, ct) =>
            {
                await using var tx = await sp.GetRequiredService<IContextTransaction>().BeginAsync(ct);
                await sp.GetRequiredService<IJobQueue>().EnqueueAsync(new JobRequest(NotificationJobTypes.Challenges, eventId, $"replay:{Guid.NewGuid():N}"), ct);
                await tx.CommitAsync(ct);
            }, Ct);
            await Stufe6.WaitForQueueAsync(pg, _t.Id, Ct);
            Assert.Equal(5, (await Stufe6.GetAsync<List<NotificationResponse>>(a, "/api/notifications", Ct)).Count(e => e.Category == NotificationCategoryDto.Challenge));
            Assert.Equal(3, pg.Push.Sent.Count(m => m.Endpoint.ToString() == device.Endpoint));

            // Tenant schaltet Push aus: Ereignis geht In-App, kein Push, Abonnement bleibt; Einschalten wirkt ohne neuen Prompt.
            using var off = await admin.PutAsJsonAsync("/api/notifications/tenant", new TenantNotificationSettingsRequest(false, true, true, "20:00", "07:00"), Ct);
            Assert.Equal(HttpStatusCode.OK, off.StatusCode);
            using var memberOff = await a.PutAsJsonAsync("/api/notifications/tenant", new TenantNotificationSettingsRequest(false, true, true, "20:00", "07:00"), Ct);
            Assert.Equal(HttpStatusCode.Forbidden, memberOff.StatusCode);
            Assert.False((await Stufe6.GetAsync<PersonNotificationSettingsResponse>(a, "/api/me/notifications", Ct)).Tenant.PushEnabled);
            using var end = await manager.PostAsJsonAsync($"/api/challenges/{challenge:D}/end", new EndChallengeRequestShim("Fertig"), Ct);
            Assert.Equal(HttpStatusCode.NoContent, end.StatusCode);
            await Stufe6.WaitForQueueAsync(pg, _t.Id, Ct);
            Assert.Single(await Stufe6.GetAsync<List<PushSubscriptionResponse>>(a, "/api/me/notifications/subscriptions", Ct));
            Assert.Equal(3, pg.Push.Sent.Count(m => m.Endpoint.ToString() == device.Endpoint));
        }
        finally
        {
            await worker.StopAsync(Ct);
        }
    }

    private sealed record EndChallengeRequestShim(string Reason);

    [Fact]
    public async Task Ruhezeit_haelt_Push_zurueck_bis_zu_ihrem_Ende_in_der_Tenant_Zeitzone()
    {
        using var a = pg.Client(_t.Id, _t.MemberA);
        using var admin = pg.Client(_t.Id, _t.Admin);
        using var manager = pg.Client(_t.Id, _t.Manager);
        using var device = NewDevice();
        await SubscribeAsync(a, device, Ct);

        // Die Ruhezeit des Tenants umschließt die reale Uhrzeit des Workers; das Ende liegt zwei Stunden voraus.
        var vienna = TenantTimeZone.Resolve("Europe/Vienna");
        var local = TenantTimeZone.TimeOf(DateTimeOffset.UtcNow, vienna);
        var start = local.AddHours(-1);
        var end = local.AddHours(2);
        using var settings = await admin.PutAsJsonAsync("/api/notifications/tenant", new TenantNotificationSettingsRequest(true, true, true, start.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture), end.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture)), Ct);
        Assert.Equal(HttpStatusCode.OK, settings.StatusCode);

        var challenge = await Stufe6.PlanChallengeAsync(manager, pg.Clock.GetUtcNow(), "Ruhezeit", 4m, Ct);
        var control = new TestJobControl { SubscriberPerson = _t.MemberA };
        using var worker = WorkerHost.Create(pg, control, "w-ruhe");
        await worker.StartAsync(Ct);
        try
        {
            await Stufe6.WaitUntilRunningAsync(worker, a, challenge, Ct);
            await Stufe6.WaitForQueueAsync(pg, _t.Id, Ct);
            var delivery = await InNotificationsAsync((db, ct) => db.Deliveries.Where(d => d.PersonId == _t.MemberA && d.Channel == NotificationChannel.Push).SingleAsync(ct));
            Assert.Equal(DeliveryStatus.Held, delivery.Status);
            Assert.NotNull(delivery.HeldUntil);
            Assert.True(delivery.HeldUntil > DateTimeOffset.UtcNow.AddHours(1));
            Assert.Equal((end.Hour, end.Minute), (TenantTimeZone.TimeOf(delivery.HeldUntil!.Value, vienna).Hour, TenantTimeZone.TimeOf(delivery.HeldUntil.Value, vienna).Minute));
            Assert.DoesNotContain(pg.Push.Sent, m => m.Endpoint.ToString() == device.Endpoint);
            Assert.Contains(await Stufe6.GetAsync<List<NotificationResponse>>(a, "/api/notifications", Ct), e => e.TextKey == "benachrichtigung.challengeGestartet");
        }
        finally
        {
            await worker.StopAsync(Ct);
        }
    }

    [Fact]
    public async Task E_Mail_ohne_Tracking_mit_Ein_Klick_Abmeldung_je_Kategorie_und_Konto_Mail_ohne_Abmeldelink()
    {
        using var a = pg.Client(_t.Id, _t.MemberA);
        using var manager = pg.Client(_t.Id, _t.Manager);
        using var admin = pg.Client(_t.Id, _t.Admin);
        await Stufe6.QuietHoursAwayAsync(admin, Ct);
        var email = $"a-{Guid.NewGuid():N}@example.test";
        using var setEmail = await a.PutAsJsonAsync("/api/me/email", new EmailRequest(email), Ct);
        Assert.True(setEmail.IsSuccessStatusCode, setEmail.StatusCode.ToString());
        using var settings = await a.PutAsJsonAsync("/api/me/notifications", new PersonNotificationSettingsRequest(true, true, true, false, null, null), Ct);
        Assert.Equal(HttpStatusCode.OK, settings.StatusCode);

        var challenge = await Stufe6.PlanChallengeAsync(manager, pg.Clock.GetUtcNow(), "Mail", 4m, Ct);
        var control = new TestJobControl { SubscriberPerson = _t.MemberA };
        using var worker = WorkerHost.Create(pg, control, "w-mail");
        await worker.StartAsync(Ct);
        try
        {
            await Stufe6.WaitUntilRunningAsync(worker, a, challenge, Ct);
            await Stufe6.WaitForQueueAsync(pg, _t.Id, Ct);
        }
        finally
        {
            await worker.StopAsync(Ct);
        }

        var mail = Assert.Single(pg.Mail.Sent, m => m.To == email);
        Assert.Contains("Mail", mail.TextBody, StringComparison.Ordinal);
        Assert.DoesNotContain("<img", mail.TextBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(_t.MemberA.ToString(), mail.TextBody, StringComparison.Ordinal);
        Assert.Contains(PostgresFixture.Origin + "/abmelden?token=", mail.TextBody, StringComparison.Ordinal);
        Assert.StartsWith(PostgresFixture.Origin + "/api/notifications/unsubscribe?token=", mail.ListUnsubscribeUrl, StringComparison.Ordinal);

        // Ein-Klick-Abmeldung ohne Sitzung wirkt sofort und nur auf diese Kategorie (Benachrichtigungen 6.3).
        using var anonymous = pg.Api.CreateClient();
        using var unsubscribe = await anonymous.PostAsync(new Uri(mail.ListUnsubscribeUrl!), null, Ct);
        Assert.Equal(HttpStatusCode.OK, unsubscribe.StatusCode);
        Assert.Equal(NotificationCategoryDto.Challenge, (await unsubscribe.Content.ReadFromJsonAsync<UnsubscribeResponse>(Stufe6.Json, Ct))!.Category);
        var after = await Stufe6.GetAsync<PersonNotificationSettingsResponse>(a, "/api/me/notifications", Ct);
        Assert.False(after.EmailChallenge);
        Assert.True(after.PushChallenge);
        using var invalid = await anonymous.PostAsync(new Uri("/api/notifications/unsubscribe?token=kaputt", UriKind.Relative), null, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        // Konto-Nachrichten über denselben Transport: Anrede, Link, kein Abmeldelink (Benachrichtigungen 4.1).
        using var host = WorkerHost.Create(pg, control, "w-konto");
        await host.Services.GetRequiredService<ITenantScopeFactory>().RunAsync(TenantContext.ForTenant(_t.Id), (sp, ct) =>
            sp.GetRequiredService<IAccountMailSender>().SendAsync(new AccountMail(email, "konto.magic_link", PostgresFixture.Origin + "/zugang/magic?token=abc", "Anna"), ct), Ct);
        var account = pg.Mail.Sent.Last(m => m.To == email);
        Assert.Null(account.ListUnsubscribeUrl);
        Assert.Contains("/zugang/magic?token=abc", account.TextBody, StringComparison.Ordinal);
        Assert.Contains("Anna", account.TextBody, StringComparison.Ordinal);
        Assert.DoesNotContain("abmelden", account.TextBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Aushang_als_PDF_mit_Tokenfarben_Kollektivstand_Beitrittscode_und_ohne_Personendaten()
    {
        using var manager = pg.Client(_t.Id, _t.Manager);
        using var member = pg.Client(_t.Id, _t.MemberA);
        var code = await AccessJoinTests.CreateJoinCodeAsync(pg, _t, Ct);
        var challenge = await Stufe6.PlanChallengeAsync(manager, pg.Clock.GetUtcNow(), "Aushang-Challenge", 4m, Ct);
        var control = new TestJobControl { SubscriberPerson = _t.MemberA };
        using var worker = WorkerHost.Create(pg, control, "w-aushang");
        await worker.StartAsync(Ct);
        try
        {
            await Stufe6.WaitUntilRunningAsync(worker, member, challenge, Ct);
            await Stufe6.ContributeAsync(member, challenge, pg.Clock.GetUtcNow(), Ct);
            await Stufe6.WaitForQueueAsync(pg, _t.Id, Ct);
        }
        finally
        {
            await worker.StopAsync(Ct);
        }

        using var denied = await member.PostAsync(new Uri("/api/notifications/aushang", UriKind.Relative), null, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var none = await manager.GetAsync(new Uri("/api/notifications/aushang", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.NotFound, none.StatusCode);
        using var render = await manager.PostAsync(new Uri("/api/notifications/aushang", UriKind.Relative), null, Ct);
        Assert.Equal(HttpStatusCode.Created, render.StatusCode);
        Assert.Equal("2026-W39", (await render.Content.ReadFromJsonAsync<AushangResponse>(Stufe6.Json, Ct))!.Week);

        using var pdf = await manager.GetAsync(new Uri("/api/notifications/aushang", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType!.MediaType);
        var bytes = await pdf.Content.ReadAsByteArrayAsync(Ct);
        if (Environment.GetEnvironmentVariable("CH_WRITE_SAMPLES") == "1")
        {
            // Abnahmeartefakt neben den Screenshots (Protokoll Stufe 6): der Aushang der Probe-Challenge im Standard-Theme.
            var target = Path.Combine(RepositoryRoot.Find().FullName, "durchstich", "abnahme", "laeufe", "stufe-6", "aushang.pdf");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await File.WriteAllBytesAsync(target, bytes, Ct);
        }

        var text = Encoding.Latin1.GetString(bytes);
        Assert.StartsWith("%PDF-1.4", text, StringComparison.Ordinal);
        // Standard-Theme: primary #2A45C9 (Marke 2.2) als identischer Farbwert im Aushang (A-013, Stufe 5 offener Punkt 1).
        Assert.Contains("0.165 0.271 0.788 rg", text, StringComparison.Ordinal);
        Assert.Contains("Aushang-Challenge", text, StringComparison.Ordinal);
        Assert.Contains("Mindestens 25 Prozent", text, StringComparison.Ordinal);
        Assert.Contains(code, text, StringComparison.Ordinal);
        Assert.DoesNotContain("Mitglied A", text, StringComparison.Ordinal);
        Assert.DoesNotContain(_t.MemberA.ToString(), text, StringComparison.Ordinal);
    }
}
