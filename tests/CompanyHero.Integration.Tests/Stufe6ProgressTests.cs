using System.Net;
using System.Net.Http.Json;
using CompanyHero.Modules.Organisation.Api;
using CompanyHero.Modules.Privacy.Domain;
using CompanyHero.Modules.Progress.Api;

namespace CompanyHero.Integration.Tests;

/// <summary>Fortschritt 2 bis 8 (A-044 bis A-048): Check-in, Punkte, Abzeichen, Tagesziel, Beteiligung ab fünf, Zeitzone.</summary>
[Collection(PostgresTests.Name)]
public sealed class Stufe6ProgressTests(PostgresFixture pg) : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ScratchTenant _t = null!;

    public async ValueTask InitializeAsync() => _t = await pg.CreateScratchTenantWithMembersAsync($"Progress {Guid.NewGuid():N}");

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Check_in_einmal_je_Tag_Punkte_Abzeichen_und_Tagesziel()
    {
        using var a = pg.Client(_t.Id, _t.MemberA);
        var before = await Stufe6.GetAsync<PersonalProgressResponse>(a, "/api/me/progress", Ct);
        Assert.Equal(0, before.Points);
        Assert.Equal(1, before.Level);
        Assert.False(before.CheckedInToday);
        Assert.Contains(before.Earned, b => b.Key == "einstieg.erster_tag");
        Assert.All(before.Next.GroupBy(n => n.Category), g => Assert.Single(g));

        using var first = await a.PostAsJsonAsync("/api/me/check-in", new CheckInRequest([CheckInTileDto.Moved, CheckInTileDto.Paused]), Ct);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        using var second = await a.PostAsJsonAsync("/api/me/check-in", new CheckInRequest([CheckInTileDto.Rested]), Ct);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal("already_today", (await second.Content.ReadFromJsonAsync<CheckInResponse>(Stufe6.Json, Ct))!.Outcome);

        var control = new TestJobControl { SubscriberPerson = _t.MemberA };
        using var worker = WorkerHost.Create(pg, control, "w-progress");
        await worker.StartAsync(Ct);
        try
        {
            await Stufe6.WaitForQueueAsync(pg, _t.Id, Ct);
        }
        finally
        {
            await worker.StopAsync(Ct);
        }

        var after = await Stufe6.GetAsync<PersonalProgressResponse>(a, "/api/me/progress", Ct);
        Assert.Equal(10, after.Points);
        Assert.Equal(1, after.CurrentStreak);
        Assert.Equal(1, after.TodayActions);
        Assert.True(after.CheckedInToday);
        Assert.Contains(after.Earned, b => b.Key == "einstieg.erste_uebung" && b.AwardedAt is not null);

        using var goal = await a.PutAsJsonAsync("/api/me/progress/daily-goal", new DailyGoalRequest(2), Ct);
        Assert.Equal(HttpStatusCode.NoContent, goal.StatusCode);
        Assert.Equal(2, (await Stufe6.GetAsync<PersonalProgressResponse>(a, "/api/me/progress", Ct)).DailyGoal);
        using var tooHigh = await a.PutAsJsonAsync("/api/me/progress/daily-goal", new DailyGoalRequest(5), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, tooHigh.StatusCode);

        // Aktivitäten anderer Personen bleiben für den Tenant-Admin unsichtbar (Regel 1), eigene sichtbar; am Kiosk zählt der Check-in ebenso.
        using var admin = pg.Client(_t.Id, _t.Admin);
        using var foreign = await admin.GetAsync(new Uri($"/api/persons/{_t.MemberA}/activities", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        var own = await Stufe6.GetAsync<List<ActivityResponse>>(a, $"/api/persons/{_t.MemberA}/activities", Ct);
        Assert.Contains(own, x => x.Kind == "check_in" && x.Points == 10);
        using var kiosk = pg.KioskClient(await pg.KioskSessionAsync(_t.Id, _t.MemberB, Ct));
        using var kioskCheckIn = await kiosk.PostAsJsonAsync("/api/me/check-in", new CheckInRequest([CheckInTileDto.Moved]), Ct);
        Assert.Equal(HttpStatusCode.Created, kioskCheckIn.StatusCode);
    }

    [Fact]
    public async Task Beteiligung_erst_ab_fuenf_Personen_und_nur_fuer_Funktionsrollen()
    {
        using var admin = pg.Client(_t.Id, _t.Admin);
        using var member = pg.Client(_t.Id, _t.MemberA);
        using var headcount = await admin.PostAsJsonAsync("/api/organisation/tenant/headcount", new HeadcountRequest(new DateOnly(2026, 9, 1), 10), Ct);
        Assert.Equal(HttpStatusCode.NoContent, headcount.StatusCode);

        var persons = new List<Guid> { _t.Admin.Value, _t.Manager.Value, _t.MemberA.Value, _t.MemberB.Value };
        foreach (var person in persons)
        {
            using var client = pg.Client(_t.Id, new Platform.Tenancy.PersonId(person));
            using var checkIn = await client.PostAsJsonAsync("/api/me/check-in", new CheckInRequest([CheckInTileDto.Moved]), Ct);
            Assert.Equal(HttpStatusCode.Created, checkIn.StatusCode);
        }

        using var denied = await member.GetAsync(new Uri("/api/progress/participation?period=month", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        var below = await Stufe6.GetAsync<ParticipationResponse>(admin, "/api/progress/participation?period=month", Ct);
        Assert.False(below.Available);
        Assert.Null(below.ActivePersons);
        Assert.Null(below.Percent);

        var fifth = await Stufe6.AddPersonAsync(pg, _t.Id, "Fünfte", VisibilityLevel.OnlyMe, Ct);
        using var fifthClient = pg.Client(_t.Id, fifth);
        using var fifthCheckIn = await fifthClient.PostAsJsonAsync("/api/me/check-in", new CheckInRequest([CheckInTileDto.Moved]), Ct);
        var above = await Stufe6.GetAsync<ParticipationResponse>(admin, "/api/progress/participation?period=month", Ct);
        Assert.True(above.Available);
        Assert.Equal(5, above.ActivePersons);
        Assert.Equal(10, above.Headcount);
        Assert.Equal(50, above.Percent);
    }
}
