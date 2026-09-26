using System.Net;
using System.Net.Http.Json;
using CompanyHero.Modules.Challenges.Api;
using CompanyHero.Modules.Feed.Api;
using CompanyHero.Modules.Notifications.Api;
using CompanyHero.Modules.Organisation.Api;
using CompanyHero.Modules.Privacy.Api;
using CompanyHero.Modules.Privacy.Domain;
using CompanyHero.Modules.Progress.Api;
using Microsoft.Net.Http.Headers;

namespace CompanyHero.Integration.Tests;

/// <summary>Challenges 4, 6 (A-038 bis A-042) und Feed 2, 3.2 (A-049, A-050): Wizard, Lebenszyklus, Meilensteine, Kopfkarte, Sichtbarkeit im Feed, Sammelkarte, ETag.</summary>
[Collection(PostgresTests.Name)]
public sealed class Stufe6ChallengeFeedTests(PostgresFixture pg) : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ScratchTenant _t = null!;

    public async ValueTask InitializeAsync() => _t = await pg.CreateScratchTenantWithMembersAsync($"Feed {Guid.NewGuid():N}");

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Kickoff_Wizard_Vorschau_ist_Pflicht_und_nur_Programm_Manager_oder_Tenant_Admin()
    {
        using var member = pg.Client(_t.Id, _t.MemberA);
        using var manager = pg.Client(_t.Id, _t.Manager);

        using var denied = await member.PostAsync(new Uri("/api/challenges/kickoff", UriKind.Relative), null, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        using var created = await manager.PostAsync(new Uri("/api/challenges/kickoff", UriKind.Relative), null, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var card = (await created.Content.ReadFromJsonAsync<ChallengeCardResponse>(Stufe6.Json, Ct))!;
        Assert.Equal("kickoff", card.TemplateKey);
        Assert.Equal(ChallengeStateDto.Draft, card.State);
        Assert.Equal(ChallengeMetricDto.Checkmark, card.Metric);
        Assert.Equal("400.0000", card.Target);
        Assert.Equal(ChallengeVisibilityDto.Company, card.Visibility);
        // Nächster Montag 00:00 Wien nach dem 25.09.2026 (Freitag): 28.09.2026, vier Wochen.
        Assert.Equal(new DateTimeOffset(2026, 9, 27, 22, 0, 0, TimeSpan.Zero), card.StartsAt);
        Assert.Equal(DayOfWeek.Monday, TimeZoneInfo.ConvertTime(card.StartsAt, TimeZoneInfo.FindSystemTimeZoneById("Europe/Vienna")).DayOfWeek);

        using var planWithoutPreview = await manager.PostAsync(new Uri($"/api/challenges/{card.ChallengeId}/plan", UriKind.Relative), null, Ct);
        Assert.Equal(HttpStatusCode.Conflict, planWithoutPreview.StatusCode);
        Assert.Equal("preview_required", await Stufe6.DetailAsync(planWithoutPreview, Ct));

        using var preview = await manager.PostAsync(new Uri($"/api/challenges/{card.ChallengeId}/preview", UriKind.Relative), null, Ct);
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.True((await preview.Content.ReadFromJsonAsync<ChallengeCardResponse>(Stufe6.Json, Ct))!.Previewed);
        using var plan = await manager.PostAsync(new Uri($"/api/challenges/{card.ChallengeId}/plan", UriKind.Relative), null, Ct);
        Assert.Equal(HttpStatusCode.NoContent, plan.StatusCode);

        // Geplante Challenges sind für Mitglieder sichtbar („startet am“); Entwürfe nur in der Verwaltung.
        var visible = await Stufe6.GetAsync<List<ChallengeCardResponse>>(member, "/api/challenges", Ct);
        Assert.Contains(visible, c => c.ChallengeId == card.ChallengeId && c.State == ChallengeStateDto.Planned);
        using var manage = await member.GetAsync(new Uri("/api/challenges/manage", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.Forbidden, manage.StatusCode);
        Assert.Contains(await Stufe6.GetAsync<List<ChallengeCardResponse>>(manager, "/api/challenges/manage", Ct), c => c.ChallengeId == card.ChallengeId);

        // Texte bleiben änderbar, das vorzeitige Ende gehört dem Programm-Manager und braucht eine laufende Challenge.
        using var texts = await manager.PutAsJsonAsync($"/api/challenges/{card.ChallengeId}/texts", new ChallengeTextsRequest("Gemeinsam losgehen", "Vier Wochen"), Ct);
        Assert.Equal(HttpStatusCode.NoContent, texts.StatusCode);
        using var endPlanned = await manager.PostAsJsonAsync($"/api/challenges/{card.ChallengeId}/end", new EndChallengeRequest("Test"), Ct);
        Assert.Equal(HttpStatusCode.Conflict, endPlanned.StatusCode);
    }

    [Fact]
    public async Task Lebenszyklus_Meilensteine_Feed_Karten_Benachrichtigungen_und_Gegenbuchung()
    {
        using var manager = pg.Client(_t.Id, _t.Manager);
        using var admin = pg.Client(_t.Id, _t.Admin);
        using var a = pg.Client(_t.Id, _t.MemberA);
        using var b = pg.Client(_t.Id, _t.MemberB);

        var challenge = await Stufe6.PlanChallengeAsync(manager, pg.Clock.GetUtcNow(), "Rad zur Arbeit", 10m, Ct);
        var control = new TestJobControl { SubscriberPerson = _t.MemberA };
        using var worker = WorkerHost.Create(pg, control, "w-feed");
        await worker.StartAsync(Ct);
        try
        {
            await Stufe6.WaitUntilRunningAsync(worker, a, challenge, Ct);
            await Stufe6.WaitForQueueAsync(pg, _t.Id, Ct);

            // Kopfkarte ist die laufende Challenge; Start als Karte und als Benachrichtigung für alle Mitglieder (Feed 2.3, Benachrichtigungen 4.1).
            var feed = await Stufe6.GetAsync<FeedResponse>(a, "/api/feed", Ct);
            Assert.Equal("challenge", feed.Head.Kind);
            Assert.Equal(challenge.ToString("D"), feed.Head.Challenge!.ChallengeId);
            Assert.True(feed.CheckInDue);
            Assert.Contains(feed.Cards, c => c.TextKey == "feed.challengeGestartet" && c.Params["titel"] == "Rad zur Arbeit" && c.Reference?.Id == challenge.ToString("D"));
            var notifications = await Stufe6.GetAsync<List<NotificationResponse>>(b, "/api/notifications", Ct);
            Assert.Contains(notifications, n => n.TextKey == "benachrichtigung.challengeGestartet" && n.Target == "/challenges/" + challenge.ToString("D"));

            // Drei Häkchen von zwei Personen: 30 Prozent, Meilenstein 25 genau einmal.
            var now = pg.Clock.GetUtcNow();
            await Stufe6.ContributeAsync(a, challenge, now.AddHours(-1), Ct);
            await Stufe6.ContributeAsync(a, challenge, now.AddDays(-1), Ct);
            await Stufe6.ContributeAsync(b, challenge, now.AddHours(-2), Ct);
            await Stufe6.WaitForQueueAsync(pg, _t.Id, Ct);
            var collective = await Stufe6.GetAsync<CollectiveResponse>(a, $"/api/challenges/{challenge:D}/collective", Ct);
            Assert.Equal(30, collective.Percent);
            Assert.Null(collective.Total);
            feed = await Stufe6.GetAsync<FeedResponse>(a, "/api/feed", Ct);
            Assert.Single(feed.Cards, c => c.TextKey == "feed.meilenstein" && c.Params["prozent"] == "25");
            Assert.Equal(30, feed.Head.Challenge!.Percent);
            Assert.True(feed.Head.Challenge.ContributedToday);

            // Korrektur als Gegenbuchung: der Stand sinkt, die Meilenstein-Karte bleibt genau einmal; ein zweites Mal geht nicht.
            var mine = await Stufe6.GetAsync<List<OwnContributionResponse>>(a, $"/api/challenges/{challenge:D}/contributions/mine", Ct);
            var first = mine.First(c => !c.IsReversal);
            using var reverse = await a.PostAsync(new Uri($"/api/challenges/{challenge:D}/contributions/{first.ContributionId}/reverse", UriKind.Relative), null, Ct);
            Assert.Equal(HttpStatusCode.OK, reverse.StatusCode);
            using var again = await a.PostAsync(new Uri($"/api/challenges/{challenge:D}/contributions/{first.ContributionId}/reverse", UriKind.Relative), null, Ct);
            Assert.Equal("already_reversed", (await again.Content.ReadFromJsonAsync<ReversalResponse>(Stufe6.Json, Ct))!.Outcome);
            await Stufe6.WaitForQueueAsync(pg, _t.Id, Ct);
            Assert.Equal(20, (await Stufe6.GetAsync<CollectiveResponse>(a, $"/api/challenges/{challenge:D}/collective", Ct)).Percent);
            feed = await Stufe6.GetAsync<FeedResponse>(a, "/api/feed", Ct);
            Assert.Single(feed.Cards, c => c.TextKey == "feed.meilenstein");
            mine = await Stufe6.GetAsync<List<OwnContributionResponse>>(a, $"/api/challenges/{challenge:D}/contributions/mine", Ct);
            Assert.Contains(mine, c => c.IsReversal && c.Value == "-1.0000");
            Assert.True(mine.Single(c => c.ContributionId == first.ContributionId).Reversed);

            // Vorzeitiges Ende: nur der Programm-Manager, mit Begründung; danach Nachfrist mit Beiträgen und Korrektur.
            using var adminEnd = await admin.PostAsJsonAsync($"/api/challenges/{challenge:D}/end", new EndChallengeRequest("Betriebsversammlung"), Ct);
            Assert.Equal(HttpStatusCode.Forbidden, adminEnd.StatusCode);
            using var noReason = await manager.PostAsJsonAsync($"/api/challenges/{challenge:D}/end", new EndChallengeRequest(" "), Ct);
            Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
            using var end = await manager.PostAsJsonAsync($"/api/challenges/{challenge:D}/end", new EndChallengeRequest("Betriebsversammlung"), Ct);
            Assert.Equal(HttpStatusCode.NoContent, end.StatusCode);
            var card = await Stufe6.GetAsync<ChallengeCardResponse>(a, $"/api/challenges/{challenge:D}", Ct);
            Assert.Equal(ChallengeStateDto.Grace, card.State);
            await Stufe6.ContributeAsync(b, challenge, now.AddHours(-3), Ct);
        }
        finally
        {
            await worker.StopAsync(Ct);
        }
    }

    [Fact]
    public async Task Mitgliederbeitrag_folgt_der_Sichtbarkeitsstufe_rueckwirkend_und_nie_am_Kiosk()
    {
        using var admin = pg.Client(_t.Id, _t.Admin);
        using var a = pg.Client(_t.Id, _t.MemberA);
        using var b = pg.Client(_t.Id, _t.MemberB);

        // „Nur für mich“: kein Beitrag, Erklärung über den Grund.
        using var onlyMe = await b.PostAsJsonAsync("/api/feed/posts", new FeedPostRequest("Hallo"), Ct);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, onlyMe.StatusCode);
        Assert.Equal("visibility_only_me", await Stufe6.DetailAsync(onlyMe, Ct));

        using var created = await a.PostAsJsonAsync("/api/feed/posts", new FeedPostRequest("Heute Rad gefahren"), Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var post = (await created.Content.ReadFromJsonAsync<FeedPostResponse>(Stufe6.Json, Ct))!;

        var forAdmin = await Stufe6.GetAsync<FeedResponse>(admin, "/api/feed", Ct);
        var card = Assert.Single(forAdmin.Cards, c => c.Id == post.Id);
        Assert.Equal("member_post", card.Kind);
        Assert.Equal("Mitglied A", card.Person!.DisplayName);
        Assert.Equal("Heute Rad gefahren", card.Body);
        Assert.False(card.Mine);
        Assert.True(Assert.Single((await Stufe6.GetAsync<FeedResponse>(a, "/api/feed", Ct)).Cards, c => c.Id == post.Id).Mine);

        // Wechsel auf „Mein Team“ wirkt rückwirkend: ohne gemeinsame Gruppe sieht der Tenant-Admin den Beitrag nicht mehr (A-022).
        using var team = await a.PutAsJsonAsync("/api/me/visibility", new VisibilityRequest(VisibilityLevelDto.Team), Ct);
        Assert.Equal(HttpStatusCode.NoContent, team.StatusCode);
        Assert.DoesNotContain((await Stufe6.GetAsync<FeedResponse>(admin, "/api/feed", Ct)).Cards, c => c.Id == post.Id);

        var dimensions = await Stufe6.GetAsync<List<DimensionResponse>>(admin, "/api/organisation/dimensions", Ct);
        using var groupResponse = await admin.PostAsJsonAsync($"/api/organisation/dimensions/{dimensions[0].DimensionId}/groups", new CreateGroupRequest("Wien", 20), Ct);
        var group = (await groupResponse.Content.ReadFromJsonAsync<CreatedResponse>(Stufe6.Json, Ct))!;
        foreach (var client in new[] { admin, a })
        {
            using var choose = await client.PutAsJsonAsync("/api/me/groups", new GroupChoiceRequest(dimensions[0].DimensionId, group.Id), Ct);
            Assert.Equal(HttpStatusCode.NoContent, choose.StatusCode);
        }

        Assert.Contains((await Stufe6.GetAsync<FeedResponse>(admin, "/api/feed", Ct)).Cards, c => c.Id == post.Id);

        // ETag: unveränderter Feed liefert 304 ohne Nutzdaten (Feed 2.6).
        using var firstResponse = await admin.GetAsync(new Uri("/api/feed", UriKind.Relative), Ct);
        var etag = firstResponse.Headers.ETag!.ToString();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/feed", UriKind.Relative));
        request.Headers.TryAddWithoutValidation(HeaderNames.IfNoneMatch, etag);
        using var notModified = await admin.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.NotModified, notModified.StatusCode);
        Assert.Equal(0, notModified.Content.Headers.ContentLength ?? 0);

        // Deckel: fünf Beiträge je Tag, der sechste wird abgelehnt; Löschen des eigenen Beitrags.
        for (var i = 0; i < 4; i++)
        {
            using var more = await a.PostAsJsonAsync("/api/feed/posts", new FeedPostRequest($"Beitrag {i}"), Ct);
            Assert.Equal(HttpStatusCode.Created, more.StatusCode);
        }

        using var sixth = await a.PostAsJsonAsync("/api/feed/posts", new FeedPostRequest("Zu viel"), Ct);
        Assert.Equal(HttpStatusCode.TooManyRequests, sixth.StatusCode);
        using var deleteForeign = await b.DeleteAsync(new Uri($"/api/feed/posts/{post.Id}", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.NotFound, deleteForeign.StatusCode);
        using var deleteOwn = await a.DeleteAsync(new Uri($"/api/feed/posts/{post.Id}", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.NoContent, deleteOwn.StatusCode);

        // Kiosk: kein Feed, weder lesen noch schreiben (A-005).
        using var kiosk = pg.KioskClient(await pg.KioskSessionAsync(_t.Id, _t.MemberA, Ct));
        using var kioskRead = await kiosk.GetAsync(new Uri("/api/feed", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.Forbidden, kioskRead.StatusCode);
        using var kioskWrite = await kiosk.PostAsJsonAsync("/api/feed/posts", new FeedPostRequest("Vom Kiosk"), Ct);
        Assert.Equal(HttpStatusCode.Forbidden, kioskWrite.StatusCode);
    }

    [Fact]
    public async Task Sammelkarte_Abzeichen_erst_ab_fuenf_Personen_und_Check_in_Karte()
    {
        using var admin = pg.Client(_t.Id, _t.Admin);
        var extra = await Stufe6.AddPersonAsync(pg, _t.Id, "Fünfte Person", VisibilityLevel.OnlyMe, Ct);
        var clients = new[] { _t.Admin, _t.Manager, _t.MemberA, _t.MemberB, extra }.Select(p => pg.Client(_t.Id, p)).ToList();
        var control = new TestJobControl { SubscriberPerson = _t.MemberA };
        using var worker = WorkerHost.Create(pg, control, "w-sammel");
        await worker.StartAsync(Ct);
        try
        {
            // Vier Personen mit Abzeichen: nur individuell sichtbare Karten (Mitglied B mit „Nur für mich“ nicht).
            foreach (var client in clients.Take(4))
            {
                using var checkIn = await client.PostAsJsonAsync("/api/me/check-in", new CheckInRequest([CheckInTileDto.Moved]), Ct);
                Assert.Equal(HttpStatusCode.Created, checkIn.StatusCode);
            }

            await Stufe6.WaitForQueueAsync(pg, _t.Id, Ct);
            var feed = await Stufe6.GetAsync<FeedResponse>(admin, "/api/feed", Ct);
            Assert.False(feed.CheckInDue);
            Assert.Contains(feed.Cards, c => c.TextKey == "feed.checkin" && c.Mine);
            Assert.DoesNotContain(feed.Cards, c => c.TextKey == "feed.abzeichenSammel");
            var badgeCards = feed.Cards.Where(c => c.TextKey == "feed.abzeichenVerdient").ToList();
            Assert.NotEmpty(badgeCards);
            Assert.DoesNotContain(badgeCards, c => c.Person?.PersonId == _t.MemberB.ToString());
            Assert.Contains(badgeCards, c => c.Person?.PersonId == _t.MemberA.ToString());

            // Fünfte Person: eine Sammelkarte mit Zahl, keine Einzelkarten mehr (Feed 2.5, A-023).
            using var fifth = await clients[4].PostAsJsonAsync("/api/me/check-in", new CheckInRequest([CheckInTileDto.Rested]), Ct);
            Assert.Equal(HttpStatusCode.Created, fifth.StatusCode);
            await Stufe6.WaitForQueueAsync(pg, _t.Id, Ct);
            feed = await Stufe6.GetAsync<FeedResponse>(admin, "/api/feed", Ct);
            var aggregate = Assert.Single(feed.Cards, c => c.TextKey == "feed.abzeichenSammel");
            Assert.Equal("5", aggregate.Params["anzahl"]);
            Assert.Null(aggregate.Person);
            Assert.DoesNotContain(feed.Cards, c => c.TextKey == "feed.abzeichenVerdient");
        }
        finally
        {
            await worker.StopAsync(Ct);
            clients.ForEach(c => c.Dispose());
        }
    }
}
