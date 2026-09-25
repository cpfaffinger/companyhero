using Microsoft.AspNetCore.Routing;
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

    /// <summary>HTTP-Schnittstelle des Moduls; nur der API-Host ruft sie auf. Module ohne Endpunkte lassen die Voreinstellung.</summary>
    void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
    }
}
