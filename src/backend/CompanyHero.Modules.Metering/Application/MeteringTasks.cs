using CompanyHero.Modules.Organisation.Application;
using CompanyHero.Platform.Jobs;
using CompanyHero.Platform.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyHero.Modules.Metering.Application;

/// <summary>Zeitgesteuert je Stunde (Metering 6.1 Nr. 1): öffnet die laufende Periode, versiegelt fällige Perioden und erzeugt Rechnungsentwürfe je Tenant.</summary>
internal sealed class MeteringSealTask(ITenantScopeFactory scopes, IOrganisationDirectory organisations) : IScheduledTask
{
    public static string Name => "metering.seal";

    public static TimeSpan Interval => TimeSpan.FromHours(1);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        foreach (var tenant in await organisations.ListActiveTenantsAsync(cancellationToken))
        {
            await scopes.RunAsync(TenantContext.ForTenant(tenant), (sp, ct) => sp.GetRequiredService<IBillingPeriods>().SealDueAsync(ct), cancellationToken);
        }
    }
}

/// <summary>Zeitgesteuert täglich (Metering 2.3): vernichtet Periodensalze sieben Tage nach der Versiegelung.</summary>
internal sealed class MeteringSaltDestructionTask(ITenantScopeFactory scopes, IOrganisationDirectory organisations) : IScheduledTask
{
    public static string Name => "metering.salt.destroy";

    public static TimeSpan Interval => TimeSpan.FromHours(24);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        foreach (var tenant in await organisations.ListActiveTenantsAsync(cancellationToken))
        {
            await scopes.RunAsync(TenantContext.ForTenant(tenant), (sp, ct) => sp.GetRequiredService<IBillingPeriods>().DestroySaltsDueAsync(ct), cancellationToken);
        }
    }
}

/// <summary>Zeitgesteuert je Stunde (Metering 5): führt die Tagesaggregate der offenen Periode je Tenant nach.</summary>
internal sealed class MeteringAggregateTask(ITenantScopeFactory scopes, IOrganisationDirectory organisations, TimeProvider clock) : IScheduledTask
{
    public static string Name => "metering.aggregate";

    public static TimeSpan Interval => TimeSpan.FromHours(1);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        foreach (var tenant in await organisations.ListActiveTenantsAsync(cancellationToken))
        {
            await scopes.RunAsync(TenantContext.ForTenant(tenant), async (sp, ct) =>
            {
                var zone = await sp.GetRequiredService<ITenantTimeZone>().GetAsync(ct);
                await sp.GetRequiredService<IUsageReports>().UsageAsync(TenantTimeZone.PeriodOf(clock.GetUtcNow(), zone), ct);
            }, cancellationToken);
        }
    }
}
