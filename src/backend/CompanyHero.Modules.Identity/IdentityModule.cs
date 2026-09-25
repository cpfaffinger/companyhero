using CompanyHero.Modules.Identity.Api;
using CompanyHero.Modules.Identity.Application;
using CompanyHero.Modules.Identity.Infrastructure;
using CompanyHero.Platform.Data;
using CompanyHero.Platform.Modules;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyHero.Modules.Identity;

public sealed class IdentityModule : IModule
{
    public static ModuleDescriptor Definition { get; } = new("Identity", ModuleSchemas.Identity, DependencyTier: 1);

    public ModuleDescriptor Descriptor => Definition;

    public void AddModule(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<IdentityDbContext>(ModuleSchemas.Identity);
        services.AddScoped<IPersonDirectory, PersonDirectory>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => IdentityEndpoints.Map(endpoints);
}
