using CompanyHero.Modules.Organisation.Application;
using CompanyHero.Modules.Organisation.Infrastructure;
using CompanyHero.Platform.Data;
using CompanyHero.Platform.Modules;
using CompanyHero.Platform.Tenancy;
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
    }
}
