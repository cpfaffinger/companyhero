using System.Net;
using System.Net.Http.Json;
using CompanyHero.Modules.Challenges.Api;
using CompanyHero.Modules.Challenges.Application;
using CompanyHero.Modules.Challenges.Domain;
using CompanyHero.Modules.Entitlements.Application;
using CompanyHero.Modules.Metering.Application;
using CompanyHero.Modules.Metering.Domain;
using CompanyHero.Modules.Organisation.Domain;
using CompanyHero.Platform.Entitlements;
using CompanyHero.Platform.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyHero.Integration.Tests;

/// <summary>Gemeinsame Handgriffe der Stufe-7-Tests: Ledger lesen, Module entziehen, Perioden versiegeln, Sitzungen nach Zeitsprüngen erneuern.</summary>
internal static class Stufe7
{
    public static readonly DateTimeOffset MonthStart = new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);

    /// <summary>Versiegelung des Septembers 2026: dritter Kalendertag 03:00 Wien = 01:00 UTC (Metering 2.2).</summary>
    public static readonly DateTimeOffset AfterSealing = new(2026, 10, 3, 1, 30, 0, TimeSpan.Zero);

    public static Task<IReadOnlyList<LedgerEntry>> LedgerAsync(PostgresFixture pg, TenantId tenant, string? metric, CancellationToken ct) =>
        Stufe6.Scopes(pg).RunAsync(TenantContext.ForTenant(tenant), (sp, c) => sp.GetRequiredService<IMeteringLedger>().ListAsync(metric, c), ct);

    /// <summary>Tenant nur mit Kern (Entitlements 8.1): der Operator deaktiviert die Voreinstellung M1 sofort (Entitlements 4.4, rechtliche Notwendigkeit).</summary>
    public static Task WithoutModulesAsync(PostgresFixture pg, TenantId tenant, CancellationToken ct) =>
        Stufe6.Scopes(pg).RunAsync(TenantContext.ForTenant(tenant), async (sp, c) =>
        {
            var admin = sp.GetRequiredService<IEntitlementAdministration>();
            foreach (var module in new[] { ModuleCodes.M1Challenges })
            {
                await admin.ForceDeactivateAsync(module, "durchstich:nur_kern", pg.Clock.GetUtcNow(), c);
            }
        }, ct);

    public static Task SetLimitAsync(PostgresFixture pg, TenantId tenant, string name, int value, CancellationToken ct) =>
        Stufe6.Scopes(pg).RunAsync(TenantContext.ForTenant(tenant), (sp, c) => sp.GetRequiredService<IEntitlementAdministration>().SetLimitAsync(name, value, c), ct);

    /// <summary>Versiegelt fällige Perioden nur dieses Tenants (dieselbe Anwendungsfunktion wie der zeitgesteuerte Lauf <c>metering.seal</c>).</summary>
    public static Task<IReadOnlyList<string>> SealAsync(PostgresFixture pg, TenantId tenant, CancellationToken ct) =>
        Stufe6.Scopes(pg).RunAsync(TenantContext.ForTenant(tenant), (sp, c) => sp.GetRequiredService<IBillingPeriods>().SealDueAsync(c), ct);

    public static Task<IReadOnlyList<string>> DestroySaltsAsync(PostgresFixture pg, TenantId tenant, CancellationToken ct) =>
        Stufe6.Scopes(pg).RunAsync(TenantContext.ForTenant(tenant), (sp, c) => sp.GetRequiredService<IBillingPeriods>().DestroySaltsDueAsync(c), ct);

    public static Task<IReadOnlyList<PeriodRecord>> PeriodsAsync(PostgresFixture pg, TenantId tenant, CancellationToken ct) =>
        Stufe6.Scopes(pg).RunAsync(TenantContext.ForTenant(tenant), (sp, c) => sp.GetRequiredService<IBillingPeriods>().ListAsync(c), ct);

    public static Task SetPlanAsync(PostgresFixture pg, TenantId tenant, PricePlan plan, string validFrom, CancellationToken ct) =>
        Stufe6.Scopes(pg).RunAsync(TenantContext.ForTenant(tenant), (sp, c) => sp.GetRequiredService<IPricePlans>().SetAsync(plan, validFrom, c), ct);

    /// <summary>Laufende Challenge über die Anwendungsfunktion (ohne Lebenszyklus-Lauf), Zeitraum um die Testuhr.</summary>
    public static Task<Guid> RunningChallengeAsync(PostgresFixture pg, ScratchTenant tenant, string title, CancellationToken ct) =>
        Stufe6.Scopes(pg).RunAsync(TenantContext.ForPerson(tenant.Id, tenant.Manager, [Role.Member, Role.ProgrammeManager]), (sp, c) =>
            sp.GetRequiredService<IChallengeCatalog>().StartRunningAsync(title, ChallengeMetric.Checkmark, 25m, pg.Clock.GetUtcNow().AddDays(-3), pg.Clock.GetUtcNow().AddDays(20), c), ct);

    public static async Task<string> ContributeAsync(HttpClient client, Guid challengeId, DateTimeOffset at, string idempotencyKey, CancellationToken ct, HttpStatusCode expected = HttpStatusCode.Created)
    {
        using var response = await client.PostAsJsonAsync($"/api/challenges/{challengeId:D}/contributions", new ContributionRequest("1", at, "mobile", idempotencyKey, null), ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        Assert.True(response.StatusCode == expected, $"Beitrag: {response.StatusCode} statt {expected}: {text}");
        return System.Text.Json.JsonSerializer.Deserialize<ContributionResponse>(text, Stufe6.Json)!.ContributionId;
    }

    /// <summary>Neue Sitzung nach einem Zeitsprung der Testuhr (Sitzungslaufzeiten und frische Anmeldung folgen der Uhr).</summary>
    public static async Task<HttpClient> FreshClientAsync(PostgresFixture pg, TenantId tenant, PersonId person, CancellationToken ct) =>
        pg.ClientWithSession(await pg.IssueSessionAsync(tenant, person, ct));

    /// <summary>Rechnungsentwurf des Tenants für den Isolationsfall: September versiegeln, Entwurf lesen, Uhr zurückstellen.</summary>
    public static async Task<string> InvoiceIdAsync(PostgresFixture pg, ScratchTenant tenant, CancellationToken ct)
    {
        var start = pg.Clock.GetUtcNow();
        try
        {
            pg.Clock.Set(AfterSealing);
            await SealAsync(pg, tenant.Id, ct);
            var drafts = await Stufe6.Scopes(pg).RunAsync(TenantContext.ForTenant(tenant.Id), (sp, c) => sp.GetRequiredService<IInvoiceDrafts>().ListAsync(c), ct);
            return drafts.Single(d => d.Period == "2026-09").Id.ToString("D");
        }
        finally
        {
            pg.Clock.Set(start);
        }
    }

    public static async Task<T> GetAsync<T>(HttpClient client, string path, CancellationToken ct) => await Stufe6.GetAsync<T>(client, path, ct);

    public static async Task<HttpStatusCode> StatusAsync(HttpClient client, HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        using var response = await client.SendAsync(request, ct);
        return response.StatusCode;
    }
}
