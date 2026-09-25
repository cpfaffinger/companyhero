using CompanyHero.Modules.Challenges.Api;
using CompanyHero.Modules.Challenges.Application;
using CompanyHero.Modules.Challenges.Infrastructure;
using CompanyHero.Platform.Data;
using CompanyHero.Platform.Jobs;
using CompanyHero.Platform.Modules;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyHero.Modules.Challenges;

public sealed class ChallengesModule : IModule
{
    public static ModuleDescriptor Definition { get; } = new("Challenges", ModuleSchemas.Challenges, DependencyTier: 4);

    public ModuleDescriptor Descriptor => Definition;

    public void AddModule(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<ChallengesDbContext>(ModuleSchemas.Challenges);
        services.AddScoped<IChallengeCatalog, ChallengeCatalog>();
        services.AddScoped<IContributionService, ContributionService>();
        services.AddScoped<IChallengeEvents, ChallengeEvents>();
        services.AddJobHandler<CollectiveRecalculateHandler>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => ChallengeEndpoints.Map(endpoints);
}
