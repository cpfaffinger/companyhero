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
    }
}
