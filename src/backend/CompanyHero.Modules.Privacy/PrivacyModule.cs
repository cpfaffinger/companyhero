using CompanyHero.Modules.Privacy.Api;
using CompanyHero.Modules.Privacy.Application;
using CompanyHero.Platform.Audit;
using CompanyHero.Modules.Privacy.Infrastructure;
using CompanyHero.Platform.Data;
using CompanyHero.Platform.Modules;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyHero.Modules.Privacy;

public sealed class PrivacyModule : IModule
{
    public static ModuleDescriptor Definition { get; } = new("Privacy", ModuleSchemas.Privacy, DependencyTier: 2);

    public ModuleDescriptor Descriptor => Definition;

    public void AddModule(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<PrivacyDbContext>(ModuleSchemas.Privacy);
        services.AddScoped<IVisibilityRule, VisibilityRuleService>();
        services.AddScoped<IVisibilityChoice, Application.VisibilityChoice>();
        services.AddScoped<Platform.Privacy.IVisibilityChoiceRecorder, VisibilityChoiceRecorder>();
        services.AddScoped<IAuditLog, AuditLog>();
        services.AddScoped<ISecurityLog, SecurityLog>();
        services.AddScoped<IAuditReader, AuditReader>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => PrivacyEndpoints.Map(endpoints);
}
