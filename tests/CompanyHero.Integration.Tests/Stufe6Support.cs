using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CompanyHero.Modules.Challenges.Api;
using CompanyHero.Modules.Identity.Application;
using CompanyHero.Modules.Organisation.Application;
using CompanyHero.Modules.Organisation.Domain;
using CompanyHero.Modules.Privacy.Application;
using CompanyHero.Modules.Privacy.Domain;
using CompanyHero.Platform.Jobs;
using CompanyHero.Platform.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CompanyHero.Integration.Tests;

/// <summary>Gemeinsame Handgriffe der Stufe-6-Tests: Personen anlegen, laufende Challenge über Wizard und Lebenszyklus, Worker abwarten.</summary>
internal static class Stufe6
{
    public static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web);

    public static readonly TimeSpan Wait = TimeSpan.FromSeconds(25);

    public static ITenantScopeFactory Scopes(PostgresFixture pg) => pg.Api.Services.GetRequiredService<ITenantScopeFactory>();

    /// <summary>Weitere Person mit Mitgliedschaft, Sichtbarkeitswahl und Sitzung; optional mit Rollen.</summary>
    public static async Task<PersonId> AddPersonAsync(PostgresFixture pg, TenantId tenant, string name, VisibilityLevel level, CancellationToken ct, params string[] roles)
    {
        var person = await Scopes(pg).RunAsync(TenantContext.ForTenant(tenant), async (sp, c) =>
        {
            var id = await sp.GetRequiredService<IPersonDirectory>().CreatePersonAsync(name, c);
            var organisations = sp.GetRequiredService<IOrganisationDirectory>();
            await organisations.AddMemberAsync(id, c);
            await organisations.AssignRoleAsync(id, Role.Member, c);
            foreach (var role in roles)
            {
                await organisations.AssignRoleAsync(id, role, c);
            }

            await sp.GetRequiredService<IVisibilityChoice>().ChooseAsync(id, level, c);
            return id;
        }, ct);
        await pg.IssueSessionAsync(tenant, person, ct);
        return person;
    }

    public static Task AssignRolesAsync(PostgresFixture pg, TenantId tenant, PersonId person, CancellationToken ct, params string[] roles) =>
        Scopes(pg).RunAsync(TenantContext.ForTenant(tenant), async (sp, c) =>
        {
            var organisations = sp.GetRequiredService<IOrganisationDirectory>();
            foreach (var role in roles)
            {
                await organisations.AssignRoleAsync(person, role, c);
            }
        }, ct);

    /// <summary>
    /// Entwurf über den Wizard, Vorschau, Planen. Der Start liegt drei Tage vor der festen Testuhr der API, damit sowohl der
    /// Lebenszyklus-Lauf des Workers (Systemuhr) ihn sofort startet als auch rückdatierte Beiträge der Testuhr im Zeitraum liegen.
    /// </summary>
    public static async Task<Guid> PlanChallengeAsync(HttpClient manager, DateTimeOffset now, string title, decimal target, CancellationToken ct, ChallengeMetricDto metric = ChallengeMetricDto.Checkmark)
    {
        var startsAt = now.AddDays(-3);
        var endsAt = now.AddDays(20);
        using var created = await manager.PostAsJsonAsync("/api/challenges", new ChallengeDraftRequest(title, "Gemeinsam", metric, target.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture), startsAt, endsAt, ChallengeVisibilityDto.Company), ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var card = (await created.Content.ReadFromJsonAsync<ChallengeCardResponse>(Json, ct))!;
        var id = Guid.Parse(card.ChallengeId);
        using var preview = await manager.PostAsync($"/api/challenges/{id:D}/preview", null, ct);
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        using var plan = await manager.PostAsync($"/api/challenges/{id:D}/plan", null, ct);
        Assert.Equal(HttpStatusCode.NoContent, plan.StatusCode);
        return id;
    }

    /// <summary>Stößt den zeitgesteuerten Lebenszyklus-Lauf des Workers an (die Fälligkeit in <c>job_schedule</c> teilt sich der Testlauf) und wartet, bis die Challenge läuft.</summary>
    public static async Task WaitUntilRunningAsync(IHost worker, HttpClient client, Guid challengeId, CancellationToken ct)
    {
        await WorkerHost.RunScheduledTaskAsync(worker, "challenges.lifecycle", ct);
        await TestJobControl.WaitUntilAsync(() => client.GetFromJsonAsync<ChallengeCardResponse>($"/api/challenges/{challengeId:D}", Json, ct).Result!.State == ChallengeStateDto.Running, Wait, ct);
    }

    public static async Task ContributeAsync(HttpClient client, Guid challengeId, DateTimeOffset at, CancellationToken ct, string value = "1")
    {
        using var response = await client.PostAsJsonAsync($"/api/challenges/{challengeId:D}/contributions", new ContributionRequest(value, at, "mobile", Guid.CreateVersion7().ToString("D"), null), ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    /// <summary>Wartet, bis keine Jobs des Tenants mehr warten oder laufen.</summary>
    public static async Task WaitForQueueAsync(PostgresFixture pg, TenantId tenant, CancellationToken ct)
    {
        await TestJobControl.WaitUntilAsync(() => PendingJobsAsync(pg, tenant).Result == 0, Wait, ct);
    }

    public static async Task<long> PendingJobsAsync(PostgresFixture pg, TenantId tenant)
    {
        await using var conn = new Npgsql.NpgsqlConnection(pg.SuperuserDatabaseConnectionString);
        await conn.OpenAsync();
        await using var cmd = new Npgsql.NpgsqlCommand("select count(*) from platform.job where tenant_id = @t and status in (1, 2) and (run_at is null or run_at <= now())", conn);
        cmd.Parameters.AddWithValue("t", tenant.Value);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    /// <summary>Ruhezeit des Tenants weg von der aktuellen Systemzeit des Workers legen, damit Zustellungen sofort laufen (Benachrichtigungen 4.2).</summary>
    public static async Task QuietHoursAwayAsync(HttpClient admin, CancellationToken ct)
    {
        var local = TenantTimeZone.TimeOf(DateTimeOffset.UtcNow, TenantTimeZone.Resolve("Europe/Vienna"));
        var start = local.AddHours(3).ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        var end = local.AddHours(4).ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        using var response = await admin.PutAsJsonAsync("/api/notifications/tenant", new Modules.Notifications.Api.TenantNotificationSettingsRequest(true, true, true, start, end), ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>Jobs eines Typs im Tenant mit Status, Versuchen und letztem Fehler (Diagnose in Tests, ohne Nutzdaten).</summary>
    public static async Task<string> JobsAsync(PostgresFixture pg, TenantId tenant, string jobType)
    {
        await using var conn = new Npgsql.NpgsqlConnection(pg.SuperuserDatabaseConnectionString);
        await conn.OpenAsync();
        await using var cmd = new Npgsql.NpgsqlCommand("select status, attempts, coalesce(last_error, '') from platform.job where tenant_id = @t and job_type = @j order by id", conn);
        cmd.Parameters.AddWithValue("t", tenant.Value);
        cmd.Parameters.AddWithValue("j", jobType);
        var rows = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add($"status={reader.GetInt16(0)} attempts={reader.GetInt32(1)} error={reader.GetString(2)[..Math.Min(300, reader.GetString(2).Length)]}");
        }

        return string.Join(" | ", rows);
    }

    public static Task RunTaskAsync(IServiceProvider services, string name, CancellationToken ct)
    {
        var registration = services.GetServices<ScheduledTaskRegistration>().Single(r => r.Name == name);
        return services.GetRequiredService<ITenantScopeFactory>().RunAsync(TenantContext.ForPlatform(), (sp, c) => ((IScheduledTask)sp.GetRequiredService(registration.TaskType)).RunAsync(c), ct);
    }

    public static async Task<T> GetAsync<T>(HttpClient client, string path, CancellationToken ct)
    {
        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{path}: {response.StatusCode}: {text}");
        return JsonSerializer.Deserialize<T>(text, Json)!;
    }

    public static async Task<string?> DetailAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonDocument.Parse(json).RootElement.TryGetProperty("detail", out var d) ? d.GetString() : null;
    }
}
