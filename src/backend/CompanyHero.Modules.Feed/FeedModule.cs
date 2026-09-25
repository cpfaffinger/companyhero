using CompanyHero.Platform.Modules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyHero.Modules.Feed;

public sealed class FeedModule : IModule
{
    public static ModuleDescriptor Definition { get; } = new("Feed", ModuleSchemas.Feed, DependencyTier: 5);

    public ModuleDescriptor Descriptor => Definition;

    public void AddModule(IServiceCollection services, IConfiguration configuration)
    {
    }
}
