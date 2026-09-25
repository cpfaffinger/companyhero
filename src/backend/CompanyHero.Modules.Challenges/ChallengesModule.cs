using CompanyHero.Platform.Modules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyHero.Modules.Challenges;

public sealed class ChallengesModule : IModule
{
    public static ModuleDescriptor Definition { get; } = new("Challenges", ModuleSchemas.Challenges, DependencyTier: 4);

    public ModuleDescriptor Descriptor => Definition;

    public void AddModule(IServiceCollection services, IConfiguration configuration)
    {
    }
}
