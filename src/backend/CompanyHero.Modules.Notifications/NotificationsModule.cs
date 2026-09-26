using CompanyHero.Modules.Notifications.Api;
using CompanyHero.Modules.Notifications.Application;
using CompanyHero.Modules.Notifications.Infrastructure;
using CompanyHero.Platform.Data;
using CompanyHero.Platform.Events;
using CompanyHero.Platform.Jobs;
using CompanyHero.Platform.Messaging;
using CompanyHero.Platform.Modules;
using CompanyHero.Platform.Privacy;
using Microsoft.AspNetCore.Routing;
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
        services.AddOptions<NotificationsOptions>().Bind(configuration.GetSection(NotificationsOptions.Section));
        services.AddModuleDbContext<NotificationsDbContext>(ModuleSchemas.Notifications);
        services.AddHttpClient(HttpWebPushTransport.ClientName, client => client.Timeout = TimeSpan.FromSeconds(10));
        services.TryAddSingleton<IVapidKeys, VapidKeys>();
        services.TryAddSingleton<IWebPushTransport, HttpWebPushTransport>();
        services.TryAddSingleton<IMailTransport, SmtpMailTransport>();
        services.TryAddSingleton<IUnsubscribeTokens, UnsubscribeTokens>();
        services.AddScoped<MailTexts>();
        // Konto-Nachrichten (Zugang 9): Identity verwendet die Plattformschnittstelle, Benachrichtigungen setzt sie mit dem SMTP-Transport um (A-032).
        services.TryAddScoped<IAccountMailSender, MailAccountMailSender>();
        services.AddScoped<NotificationPipeline>();
        services.AddScoped<ChallengeSnapshotProjector>();
        services.AddScoped<INotificationSettings, NotificationSettings>();
        services.AddScoped<IUnsubscribeService, UnsubscribeService>();
        services.AddScoped<IAushangService, AushangService>();
        // Abonnements ohne Projektreferenz auf Challenges und Feed (Domänenkarte 6): Lesen über die Ereignisquelle.
        services.AddEventSubscription<ChallengeNotificationHandler>("challenges.started");
        services.AddEventSubscription<ChallengeNotificationHandler>("challenges.milestone.reached");
        services.AddEventSubscription<ChallengeNotificationHandler>("challenges.ended");
        services.AddEventSubscription<ProgressNotificationHandler>("progress.badge.awarded");
        services.AddEventSubscription<ProgressNotificationHandler>("progress.level.reached");
        services.AddJobHandler<PushSendHandler>();
        services.AddJobHandler<EmailSendHandler>();
        services.AddJobHandler<AushangRenderHandler>();
        services.AddScheduledTask<AushangWeeklyTask>();
        services.AddScheduledTask<NotificationRetentionTask>();
        services.AddScoped<NotificationsPersonalData>();
        services.AddScoped<IPersonalDataExporter>(sp => sp.GetRequiredService<NotificationsPersonalData>());
        services.AddScoped<IPersonalDataEraser>(sp => sp.GetRequiredService<NotificationsPersonalData>());
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => NotificationEndpoints.Map(endpoints);
}
