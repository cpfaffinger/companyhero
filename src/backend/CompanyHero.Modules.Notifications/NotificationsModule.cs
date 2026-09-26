using CompanyHero.Modules.Notifications.Application;
using CompanyHero.Platform.Messaging;
using CompanyHero.Platform.Modules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CompanyHero.Modules.Notifications;

public sealed class NotificationsModule : IModule
{
    public static ModuleDescriptor Definition { get; } = new("Notifications", ModuleSchemas.Notifications, DependencyTier: 6);

    public ModuleDescriptor Descriptor => Definition;

    public void AddModule(IServiceCollection services, IConfiguration configuration)
    {
        // Konto-Nachrichten (Zugang 9): Identity verwendet die Plattformschnittstelle, Benachrichtigungen setzt sie um.
        services.TryAddSingleton<IAccountMailSender, LoggingAccountMailSender>();
    }
}
