using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyHero.Platform.Modules;

/// <summary>
/// Einstiegspunkt eines Moduls für API und Worker. Beide Hosts laden dieselben Module (Backend 3.1).
/// </summary>
public interface IModule
{
    ModuleDescriptor Descriptor { get; }

    void AddModule(IServiceCollection services, IConfiguration configuration);
}
