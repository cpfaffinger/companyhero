using CompanyHero.Modules.Privacy.Api;
using CompanyHero.Modules.Privacy.Application;
using CompanyHero.Modules.Privacy.Infrastructure;
using CompanyHero.Platform.Audit;
using CompanyHero.Platform.Data;
using CompanyHero.Platform.Jobs;
using CompanyHero.Platform.Modules;
using CompanyHero.Platform.Privacy;
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
        services.AddScoped<IVisibilityChoiceRecorder, VisibilityChoiceRecorder>();
        services.AddScoped<IAuditLog, AuditLog>();
        services.AddScoped<ISecurityLog, SecurityLog>();
        services.AddScoped<IAuditReader, AuditReader>();
        services.AddScoped<IPersonalDataService, PersonalDataService>();
        services.AddScoped<PrivacyPersonalData>();
        services.AddScoped<IPersonalDataExporter>(sp => sp.GetRequiredService<PrivacyPersonalData>());
        services.AddScoped<IPersonalDataEraser>(sp => sp.GetRequiredService<PrivacyPersonalData>());
        services.AddJobHandler<PersonErasureHandler>();
        services.AddScheduledTask<PrivacyRetentionTask>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => PrivacyEndpoints.Map(endpoints);
}
