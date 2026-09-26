using CompanyHero.Modules.Entitlements.Api;
using CompanyHero.Modules.Entitlements.Application;
using CompanyHero.Modules.Entitlements.Infrastructure;
using CompanyHero.Platform.Data;
using CompanyHero.Platform.Entitlements;
using CompanyHero.Platform.Jobs;
using CompanyHero.Platform.Modules;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyHero.Modules.Entitlements;

public sealed class EntitlementsModule : IModule
{
    public static ModuleDescriptor Definition { get; } = new("Entitlements", ModuleSchemas.Entitlements, DependencyTier: 1);

    public ModuleDescriptor Descriptor => Definition;

    public void AddModule(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<EntitlementsDbContext>(ModuleSchemas.Entitlements);
        services.AddScoped<EntitlementRegistry>();
        services.AddScoped<IEntitlementService, EntitlementService>();
        services.AddScoped<IEntitlementAdministration, EntitlementAdministration>();
        // Querschnitte der Plattform, Eigentümer Entitlements (Entitlements 5, 6.2): Modulprüfung der API und Grenzwerte.
        services.AddScoped<IModuleEntitlements, ModuleEntitlements>();
        services.AddScoped<ITenantLimits, TenantLimits>();
        services.AddScheduledTask<EntitlementTransitionsTask>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => EntitlementEndpoints.Map(endpoints);
}
