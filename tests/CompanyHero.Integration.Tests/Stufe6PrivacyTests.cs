using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CompanyHero.Modules.Challenges.Application;
using CompanyHero.Modules.Challenges.Domain;
using CompanyHero.Modules.Challenges.Infrastructure;
using CompanyHero.Modules.Feed.Api;
using CompanyHero.Modules.Feed.Infrastructure;
using CompanyHero.Modules.Identity.Application;
using CompanyHero.Modules.Identity.Infrastructure;
using CompanyHero.Modules.Metering.Infrastructure;
using CompanyHero.Modules.Notifications.Api;
using CompanyHero.Modules.Notifications.Infrastructure;
using CompanyHero.Modules.Organisation.Application;
using CompanyHero.Modules.Privacy.Api;
using CompanyHero.Modules.Privacy.Infrastructure;
using CompanyHero.Modules.Progress.Api;
using CompanyHero.Modules.Progress.Infrastructure;
using CompanyHero.Platform.Audit;
using CompanyHero.Platform.Data;
using CompanyHero.Platform.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyHero.Integration.Tests;

/// <summary>Datenschutz 5, 6.4 (A-024, A-025, A-111) und Metering (A-071): Auskunft, Zustimmungsprotokoll, Löschung nach Austritt, Fristen, Zähler ohne Personenbezug.</summary>
[Collection(PostgresTests.Name)]
public sealed class Stufe6PrivacyTests(PostgresFixture pg) : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ScratchTenant _t = null!;

    public async ValueTask InitializeAsync() => _t = await pg.CreateScratchTenantWithMembersAsync($"Privacy {Guid.NewGuid():N}");

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private Task<T> InTenantAsync<T>(Func<IServiceProvider, CancellationToken, Task<T>> work) => Stufe6.Scopes(pg).RunAsync(TenantContext.ForTenant(_t.Id), work, Ct);

    private Task<T> QueryAsync<TContext, T>(Func<TContext, CancellationToken, Task<T>> query) where TContext : DbContext =>
        InTenantAsync(async (sp, ct) =>
        {
            await using var tx = await sp.GetRequiredService<IContextTransaction>().BeginAsync(ct);
            var result = await query(sp.GetRequiredService<TContext>(), ct);
            await tx.CommitAsync(ct);
            return result;
        });

    [Fact]
    public async Task Auskunft_als_Selbstexport_und_Zustimmungsprotokoll()
    {
        using var a = pg.Client(_t.Id, _t.MemberA);
        using var visibility = await a.PutAsJsonAsync("/api/me/visibility", new VisibilityRequest(VisibilityLevelDto.Team), Ct);
        Assert.Equal(HttpStatusCode.NoContent, visibility.StatusCode);
        using var checkIn = await a.PostAsJsonAsync("/api/me/check-in", new CheckInRequest([CheckInTileDto.Moved]), Ct);

        var consents = await Stufe6.GetAsync<List<ConsentResponse>>(a, "/api/me/consents", Ct);
        Assert.Contains(consents, c => c.Kind == "visibility" && c.Next == "Team" && c.Previous == "Company");

        using var export = await a.GetAsync(new Uri("/api/me/export", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        Assert.Equal("application/json", export.Content.Headers.ContentType!.MediaType);
        var doc = JsonDocument.Parse(await export.Content.ReadAsStringAsync(Ct)).RootElement;
        Assert.Equal("companyhero-selbstexport/1", doc.GetProperty("format").GetString());
        Assert.Equal(_t.MemberA.ToString(), doc.GetProperty("person").GetString());
        foreach (var section in new[] { "profil", "organisation", "datenschutz", "fortschritt", "challenges", "feed", "benachrichtigungen" })
        {
            Assert.True(doc.TryGetProperty(section, out _), section);
        }

        Assert.Equal("Mitglied A", doc.GetProperty("profil").GetProperty("anzeigename").GetString());
        Assert.Equal(1, doc.GetProperty("fortschritt").GetProperty("checkIns").GetArrayLength());

        // Export nur auf dem eigenen Gerät (Datenschutz 6.4).
        using var kiosk = pg.KioskClient(await pg.KioskSessionAsync(_t.Id, _t.MemberA, Ct));
        using var kioskExport = await kiosk.GetAsync(new Uri("/api/me/export", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.Forbidden, kioskExport.StatusCode);
    }

    [Fact]
    public async Task Austritt_loescht_alle_Daten_mit_Personenbezug_und_laesst_Summen_bestehen()
    {
        using var a = pg.Client(_t.Id, _t.MemberA);
        using var manager = pg.Client(_t.Id, _t.Manager);
        var challenge = await Stufe6.PlanChallengeAsync(manager, pg.Clock.GetUtcNow(), "Austritt", 4m, Ct);
        var control = new TestJobControl { SubscriberPerson = _t.MemberB };
        using var worker = WorkerHost.Create(pg, control, "w-austritt");
        await worker.StartAsync(Ct);
        try
        {
            await Stufe6.WaitUntilRunningAsync(worker, a, challenge, Ct);
            await Stufe6.ContributeAsync(a, challenge, pg.Clock.GetUtcNow(), Ct);
            using var post = await a.PostAsJsonAsync("/api/feed/posts", new FeedPostRequest("Bis bald"), Ct);
            using var subscription = await a.PostAsJsonAsync("/api/me/notifications/subscriptions", new PushSubscriptionRequest("https://push.test/austritt", "AAAA", "BBBB", "Handy"), Ct);
            Assert.Equal(HttpStatusCode.Created, subscription.StatusCode);
            await Stufe6.WaitForQueueAsync(pg, _t.Id, Ct);
            Assert.NotEmpty(await Stufe6.GetAsync<List<NotificationResponse>>(a, "/api/notifications", Ct));

            using var leave = await a.PostAsync(new Uri("/api/me/leave", UriKind.Relative), null, Ct);
            Assert.True(leave.IsSuccessStatusCode, leave.StatusCode.ToString());
            await Stufe6.WaitForQueueAsync(pg, _t.Id, Ct);
        }
        finally
        {
            await worker.StopAsync(Ct);
        }

        using var me = await a.GetAsync(new Uri("/api/me", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
        var person = _t.MemberA;
        var eraseJobs = await Stufe6.JobsAsync(pg, _t.Id, "privacy.person.erase");
        Assert.True(eraseJobs.Contains("status=3", StringComparison.Ordinal) && !eraseJobs.Contains("status=4", StringComparison.Ordinal), eraseJobs);
        Assert.Equal(0, await QueryAsync<ProgressDbContext, int>((db, ct) => db.ActivityEvents.CountAsync(e => e.PersonId == person, ct)));
        Assert.Equal(0, await QueryAsync<ProgressDbContext, int>((db, ct) => db.PersonProgress.CountAsync(p => p.PersonId == person, ct)));
        Assert.Equal(0, await QueryAsync<FeedDbContext, int>((db, ct) => db.Entries.CountAsync(e => e.SubjectPersonId == person, ct)));
        Assert.Equal(0, await QueryAsync<NotificationsDbContext, int>(async (db, ct) => await db.Subscriptions.CountAsync(s => s.PersonId == person, ct) + await db.Entries.CountAsync(e => e.PersonId == person, ct)));
        Assert.Equal(0, await QueryAsync<IdentityDbContext, int>((db, ct) => db.Sessions.CountAsync(s => s.PersonId == person, ct)));
        Assert.Equal(0, await QueryAsync<ChallengesDbContext, int>((db, ct) => db.Contributions.CountAsync(c => c.PersonId == person, ct)));
        // Der Beitrag zählt anonym weiter (Datenschutz 5.2): Summe bleibt, Beitragende sinken.
        Assert.Equal(1, await QueryAsync<ChallengesDbContext, int>((db, ct) => db.Contributions.CountAsync(c => c.ChallengeId == challenge && c.PersonId == Contribution.AnonymousPerson, ct)));
        var collective = await InTenantAsync((sp, ct) => sp.GetRequiredService<IChallengeCatalog>().GetCollectiveAsync(challenge, ct));
        Assert.Equal(1m, collective!.Total);
        Assert.Equal(0, collective.ContributorCount);
        Assert.DoesNotContain(person, await InTenantAsync((sp, ct) => sp.GetRequiredService<IOrganisationDirectory>().ListActiveMemberIdsAsync(ct)));
        Assert.Null(await InTenantAsync((sp, ct) => sp.GetRequiredService<IPersonDirectory>().GetAsync(person, ct)));
    }

    [Fact]
    public async Task Fristen_Sicherheitsprotokoll_12_Monate_abgelaufene_Sitzungen_und_verwaiste_Personen()
    {
        var start = pg.Clock.GetUtcNow();
        await InTenantAsync(async (sp, ct) =>
        {
            await using var tx = await sp.GetRequiredService<IContextTransaction>().BeginAsync(ct);
            await sp.GetRequiredService<ISecurityLog>().RecordAsync(new SecurityEvent("test.login", true, "pseudonym", null), ct);
            await sp.GetRequiredService<IAuditLog>().RecordAsync(new AuditEntry("test.audit", null, null), ct);
            await tx.CommitAsync(ct);
            return 0;
        });
        Assert.Equal(1, await QueryAsync<PrivacyDbContext, int>((db, ct) => db.SecurityRecords.CountAsync(r => r.EventType == "test.login", ct)));
        var sessions = await QueryAsync<IdentityDbContext, int>((db, ct) => db.Sessions.CountAsync(ct));
        Assert.True(sessions > 0);

        try
        {
            pg.Clock.Set(start.AddDays(400));
            await Stufe6.RunTaskAsync(pg.Api.Services, "privacy.retention", Ct);
            await Stufe6.RunTaskAsync(pg.Api.Services, "identity.cleanup", Ct);
            Assert.Equal(0, await QueryAsync<PrivacyDbContext, int>((db, ct) => db.SecurityRecords.CountAsync(r => r.EventType == "test.login", ct)));
            Assert.Equal(1, await QueryAsync<PrivacyDbContext, int>((db, ct) => db.AuditRecords.CountAsync(r => r.Action == "test.audit", ct)));
            Assert.Equal(0, await QueryAsync<IdentityDbContext, int>((db, ct) => db.Sessions.CountAsync(ct)));

            // Verwaiste Personen (24 Monate ohne Anmeldung und Handlung) werden erkannt; die Löschung läuft als Austritt.
            var orphaned = await InTenantAsync((sp, ct) => sp.GetRequiredService<IPersonDirectory>().ListOrphanedAsync(pg.Clock.GetUtcNow().AddDays(1), ct));
            Assert.Contains(_t.MemberB, orphaned);
            Assert.Empty(await InTenantAsync((sp, ct) => sp.GetRequiredService<IPersonDirectory>().ListOrphanedAsync(start.AddYears(-3), ct)));
        }
        finally
        {
            pg.Clock.Set(start);
            // Der Aufräumlauf hat auch die Sitzungen der Stamm-Tenants entfernt (sie waren zur vorgestellten Uhr abgelaufen): neu ausstellen.
            foreach (var (tenant, person) in pg.Tenants.Persons)
            {
                await pg.IssueSessionAsync(tenant, person, Ct);
            }
        }
    }

    [Fact]
    public async Task Metering_zaehlt_Teilnehmertag_und_gesendete_Benachrichtigungen_ohne_Personenbezug()
    {
        using var a = pg.Client(_t.Id, _t.MemberA);
        using var manager = pg.Client(_t.Id, _t.Manager);
        using var admin = pg.Client(_t.Id, _t.Admin);
        await Stufe6.QuietHoursAwayAsync(admin, Ct);
        using var key = System.Security.Cryptography.ECDiffieHellman.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        using var subscription = await a.PostAsJsonAsync("/api/me/notifications/subscriptions", new PushSubscriptionRequest("https://push.test/metering/" + Guid.NewGuid().ToString("N"), System.Buffers.Text.Base64Url.EncodeToString(Modules.Notifications.Application.WebPushCrypto.ExportRaw(key.PublicKey)), System.Buffers.Text.Base64Url.EncodeToString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)), "Handy"), Ct);
        Assert.Equal(HttpStatusCode.Created, subscription.StatusCode);
        var challenge = await Stufe6.PlanChallengeAsync(manager, pg.Clock.GetUtcNow(), "Metering", 4m, Ct);
        var control = new TestJobControl { SubscriberPerson = _t.MemberB };
        using var worker = WorkerHost.Create(pg, control, "w-metering");
        await worker.StartAsync(Ct);
        try
        {
            await Stufe6.WaitUntilRunningAsync(worker, a, challenge, Ct);
            await Stufe6.ContributeAsync(a, challenge, pg.Clock.GetUtcNow(), Ct);
            await Stufe6.WaitForQueueAsync(pg, _t.Id, Ct);
        }
        finally
        {
            await worker.StopAsync(Ct);
        }

        var ledger = await QueryAsync<MeteringDbContext, List<(string Metric, string SubjectRef)>>(async (db, ct) => (await db.LedgerEvents.Where(e => e.TenantId == _t.Id).Select(e => new { e.Metric, e.SubjectRef }).ToListAsync(ct)).Select(x => (x.Metric, x.SubjectRef)).ToList());
        Assert.Contains(ledger, e => e.Metric == "member.active_day");
        Assert.Contains(ledger, e => e.Metric == "challenge.participant_day");
        Assert.Contains(ledger, e => e.Metric == "notification.sent" && e.SubjectRef == "push:challenge");
        Assert.All(ledger, e => Assert.DoesNotContain(_t.MemberA.ToString(), e.SubjectRef, StringComparison.OrdinalIgnoreCase));
    }
}
