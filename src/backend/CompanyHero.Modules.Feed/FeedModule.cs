using CompanyHero.Modules.Feed.Api;
using CompanyHero.Modules.Feed.Application;
using CompanyHero.Modules.Feed.Infrastructure;
using CompanyHero.Platform.Data;
using CompanyHero.Platform.Events;
using CompanyHero.Platform.Jobs;
using CompanyHero.Platform.Modules;
using CompanyHero.Platform.Privacy;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyHero.Modules.Feed;

public sealed class FeedModule : IModule
{
    public static ModuleDescriptor Definition { get; } = new("Feed", ModuleSchemas.Feed, DependencyTier: 5);

    public ModuleDescriptor Descriptor => Definition;

    public void AddModule(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<FeedDbContext>(ModuleSchemas.Feed);
        services.AddScoped<IFeedService, FeedService>();
        services.AddScoped<FeedProjector>();
        // Ereignisse anderer Domänen werden als Jobs zugestellt und über die Ereignisquelle gelesen (Backend 6.4, Feed 2.1).
        services.AddEventSubscription<ChallengeFeedHandler>("challenges.started");
        services.AddEventSubscription<ChallengeFeedHandler>("challenges.milestone.reached");
        services.AddEventSubscription<ChallengeFeedHandler>("challenges.ended");
        services.AddEventSubscription<ProgressFeedHandler>("progress.badge.awarded");
        services.AddEventSubscription<ProgressFeedHandler>("progress.level.reached");
        services.AddEventSubscription<ProgressFeedHandler>("progress.checkin.recorded");
        services.AddScheduledTask<FeedRetentionTask>();
        services.AddScoped<FeedPersonalData>();
        services.AddScoped<IPersonalDataExporter>(sp => sp.GetRequiredService<FeedPersonalData>());
        services.AddScoped<IPersonalDataEraser>(sp => sp.GetRequiredService<FeedPersonalData>());
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => FeedEndpoints.Map(endpoints);
}
