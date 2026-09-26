using CompanyHero.Modules.Branding.Api;
using CompanyHero.Modules.Branding.Application;
using CompanyHero.Modules.Branding.Infrastructure;
using CompanyHero.Platform.Data;
using CompanyHero.Platform.Modules;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyHero.Modules.Branding;

public sealed class BrandingModule : IModule
{
    public static ModuleDescriptor Definition { get; } = new("Branding", ModuleSchemas.Branding, DependencyTier: 1);

    public ModuleDescriptor Descriptor => Definition;

    public void AddModule(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<BrandingDbContext>(ModuleSchemas.Branding);
        services.Configure<BrandingOptions>(configuration.GetSection(BrandingOptions.Section));
        services.AddScoped<IThemeService, ThemeService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => BrandingEndpoints.Map(endpoints);
}
