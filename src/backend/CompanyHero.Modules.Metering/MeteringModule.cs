using CompanyHero.Modules.Metering.Api;
using CompanyHero.Modules.Metering.Application;
using CompanyHero.Modules.Metering.Infrastructure;
using CompanyHero.Platform.Data;
using CompanyHero.Platform.Jobs;
using CompanyHero.Platform.Metering;
using CompanyHero.Platform.Modules;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyHero.Modules.Metering;

public sealed class MeteringModule : IModule
{
    public static ModuleDescriptor Definition { get; } = new("Metering", ModuleSchemas.Metering, DependencyTier: 6);

    public ModuleDescriptor Descriptor => Definition;

    public void AddModule(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<MeteringDbContext>(ModuleSchemas.Metering);
        // Querschnitte der Plattform, Eigentümer Metering (Domänenkarte 3): alle Domänen emittieren über IMeteringEmitter, Fortschritt zählt über IMeteringSlots.
        services.AddScoped<PeriodStore>();
        services.AddScoped<UsageAggregator>();
        services.AddScoped<IMeteringEmitter, MeteringEmitter>();
        services.AddScoped<IMeteringSlots, MeteringSlots>();
        services.AddScoped<IMeteringLedger, MeteringLedger>();
        services.AddScoped<IBillingPeriods, BillingPeriods>();
        services.AddScoped<IPricePlans, PricePlans>();
        services.AddScoped<IUsageReports, UsageReports>();
        services.AddScoped<ICostPreview, CostPreviewService>();
        services.AddScoped<IInvoiceDrafts, InvoiceDrafts>();
        services.AddScheduledTask<MeteringSealTask>();
        services.AddScheduledTask<MeteringSaltDestructionTask>();
        services.AddScheduledTask<MeteringAggregateTask>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => BillingEndpoints.Map(endpoints);
}
