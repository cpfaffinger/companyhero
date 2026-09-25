using CompanyHero.Modules.Metering.Application;
using CompanyHero.Modules.Metering.Infrastructure;
using CompanyHero.Platform.Data;
using CompanyHero.Platform.Metering;
using CompanyHero.Platform.Modules;
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
        // Querschnitt der Plattform, Eigentümer Metering (Domänenkarte 3): alle Domänen emittieren über IMeteringEmitter.
        services.AddScoped<IMeteringEmitter, MeteringEmitter>();
        services.AddScoped<IMeteringLedger, MeteringLedger>();
    }
}
