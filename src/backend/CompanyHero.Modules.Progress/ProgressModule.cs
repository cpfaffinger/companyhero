using CompanyHero.Modules.Progress.Api;
using CompanyHero.Modules.Progress.Application;
using CompanyHero.Modules.Progress.Infrastructure;
using CompanyHero.Platform.Data;
using CompanyHero.Platform.Events;
using CompanyHero.Platform.Jobs;
using CompanyHero.Platform.Modules;
using CompanyHero.Platform.Privacy;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyHero.Modules.Progress;

public sealed class ProgressModule : IModule
{
    public static ModuleDescriptor Definition { get; } = new("Progress", ModuleSchemas.Progress, DependencyTier: 3);

    public ModuleDescriptor Descriptor => Definition;

    public void AddModule(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<ProgressDbContext>(ModuleSchemas.Progress);
        services.AddScoped<IActivityRecorder, ActivityRecorder>();
        services.AddScoped<IPersonalActivityQuery, PersonalActivityQuery>();
        services.AddScoped<IProgressQuery, ProgressQuery>();
        services.AddScoped<ICheckInService, CheckInService>();
        services.AddScoped<IProgressAggregates, ProgressAggregates>();
        services.AddScoped<BadgeEvaluator>();
        services.AddDomainEventSource<ProgressEventSource>();
        services.AddJobHandler<BadgeEvaluationHandler>();
        services.AddEventSubscription<CollectiveGoalHandler>(CollectiveGoalHandler.SubscribedEventType);
        services.AddScoped<ProgressPersonalData>();
        services.AddScoped<IPersonalDataExporter>(sp => sp.GetRequiredService<ProgressPersonalData>());
        services.AddScoped<IPersonalDataEraser>(sp => sp.GetRequiredService<ProgressPersonalData>());
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => ProgressEndpoints.Map(endpoints);
}
