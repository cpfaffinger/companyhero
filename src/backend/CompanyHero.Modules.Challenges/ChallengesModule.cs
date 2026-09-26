using CompanyHero.Modules.Challenges.Api;
using CompanyHero.Modules.Challenges.Application;
using CompanyHero.Modules.Challenges.Infrastructure;
using CompanyHero.Platform.Data;
using CompanyHero.Platform.Events;
using CompanyHero.Platform.Jobs;
using CompanyHero.Platform.Modules;
using CompanyHero.Platform.Privacy;
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
        services.AddScoped<ChallengeEvents>();
        services.AddScoped<IChallengeEvents>(sp => sp.GetRequiredService<ChallengeEvents>());
        services.AddScoped<IDomainEventSource>(sp => sp.GetRequiredService<ChallengeEvents>());
        services.AddJobHandler<CollectiveRecalculateHandler>();
        services.AddScheduledTask<ChallengeLifecycleTask>();
        services.AddScoped<ChallengesPersonalData>();
        services.AddScoped<IPersonalDataExporter>(sp => sp.GetRequiredService<ChallengesPersonalData>());
        services.AddScoped<IPersonalDataEraser>(sp => sp.GetRequiredService<ChallengesPersonalData>());
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => ChallengeEndpoints.Map(endpoints);
}
