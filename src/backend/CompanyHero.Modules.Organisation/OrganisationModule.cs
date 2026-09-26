using CompanyHero.Modules.Organisation.Api;
using CompanyHero.Modules.Organisation.Application;
using CompanyHero.Modules.Organisation.Infrastructure;
using CompanyHero.Platform.Data;
using CompanyHero.Platform.Modules;
using CompanyHero.Platform.Privacy;
using CompanyHero.Platform.Tenancy;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyHero.Modules.Organisation;

public sealed class OrganisationModule : IModule
{
    public static ModuleDescriptor Definition { get; } = new("Organisation", ModuleSchemas.Organisation, DependencyTier: 1);

    public ModuleDescriptor Descriptor => Definition;

    public void AddModule(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<OrganisationDbContext>(ModuleSchemas.Organisation);
        services.AddScoped<IOrganisationDirectory, OrganisationDirectory>();
        services.AddScoped<IMembershipVerification, MembershipVerification>();
        services.AddScoped<ITenantTimeZone, TenantTimeZoneProvider>();
        services.AddScoped<OrganisationPersonalData>();
        services.AddScoped<IPersonalDataExporter>(sp => sp.GetRequiredService<OrganisationPersonalData>());
        services.AddScoped<IPersonalDataEraser>(sp => sp.GetRequiredService<OrganisationPersonalData>());
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => OrganisationEndpoints.Map(endpoints);
}
