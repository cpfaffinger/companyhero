using CompanyHero.Platform.Modules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyHero.Modules.Organisation;

public sealed class OrganisationModule : IModule
{
    public static ModuleDescriptor Definition { get; } = new("Organisation", ModuleSchemas.Organisation, DependencyTier: 1);

    public ModuleDescriptor Descriptor => Definition;

    public void AddModule(IServiceCollection services, IConfiguration configuration)
    {
    }
}
