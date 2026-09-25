using CompanyHero.Platform.Modules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyHero.Modules.Progress;

public sealed class ProgressModule : IModule
{
    public static ModuleDescriptor Definition { get; } = new("Progress", ModuleSchemas.Progress, DependencyTier: 3);

    public ModuleDescriptor Descriptor => Definition;

    public void AddModule(IServiceCollection services, IConfiguration configuration)
    {
    }
}
