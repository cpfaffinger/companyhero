using CompanyHero.Modules.Progress.Api;
using CompanyHero.Modules.Progress.Application;
using CompanyHero.Modules.Progress.Infrastructure;
using CompanyHero.Platform.Data;
using CompanyHero.Platform.Modules;
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
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => ProgressEndpoints.Map(endpoints);
}
