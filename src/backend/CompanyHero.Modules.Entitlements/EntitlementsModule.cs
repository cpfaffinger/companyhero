using CompanyHero.Platform.Modules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyHero.Modules.Entitlements;

public sealed class EntitlementsModule : IModule
{
    public static ModuleDescriptor Definition { get; } = new("Entitlements", ModuleSchemas.Entitlements, DependencyTier: 1);

    public ModuleDescriptor Descriptor => Definition;

    public void AddModule(IServiceCollection services, IConfiguration configuration)
    {
    }
}
