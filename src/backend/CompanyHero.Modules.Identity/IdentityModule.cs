using CompanyHero.Platform.Modules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyHero.Modules.Identity;

public sealed class IdentityModule : IModule
{
    public static ModuleDescriptor Definition { get; } = new("Identity", ModuleSchemas.Identity, DependencyTier: 1);

    public ModuleDescriptor Descriptor => Definition;

    public void AddModule(IServiceCollection services, IConfiguration configuration)
    {
    }
}
