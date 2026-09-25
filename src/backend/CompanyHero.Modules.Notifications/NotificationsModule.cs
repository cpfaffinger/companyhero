using CompanyHero.Platform.Modules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyHero.Modules.Notifications;

public sealed class NotificationsModule : IModule
{
    public static ModuleDescriptor Definition { get; } = new("Notifications", ModuleSchemas.Notifications, DependencyTier: 6);

    public ModuleDescriptor Descriptor => Definition;

    public void AddModule(IServiceCollection services, IConfiguration configuration)
    {
    }
}
