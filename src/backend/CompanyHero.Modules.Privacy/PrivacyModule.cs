using CompanyHero.Modules.Privacy.Application;
using CompanyHero.Modules.Privacy.Infrastructure;
using CompanyHero.Platform.Data;
using CompanyHero.Platform.Modules;
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
        services.AddScoped<IVisibilityChoice, VisibilityChoice>();
    }
}
