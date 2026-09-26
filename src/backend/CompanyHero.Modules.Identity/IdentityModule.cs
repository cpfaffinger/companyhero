using CompanyHero.Modules.Identity.Api;
using CompanyHero.Modules.Identity.Application;
using CompanyHero.Modules.Identity.Application.Access;
using CompanyHero.Modules.Identity.Application.Account;
using CompanyHero.Modules.Identity.Application.ExternalLogin;
using CompanyHero.Modules.Identity.Application.Join;
using CompanyHero.Modules.Identity.Application.Kiosk;
using CompanyHero.Modules.Identity.Application.Login;
using CompanyHero.Modules.Identity.Application.Passkeys;
using CompanyHero.Modules.Identity.Application.Providers;
using CompanyHero.Modules.Identity.Application.Sessions;
using CompanyHero.Modules.Identity.Infrastructure;
using CompanyHero.Modules.Identity.Infrastructure.Http;
using CompanyHero.Modules.Identity.Infrastructure.Oidc;
using CompanyHero.Platform.Data;
using CompanyHero.Platform.Jobs;
using CompanyHero.Platform.Modules;
using CompanyHero.Platform.Privacy;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace CompanyHero.Modules.Identity;

/// <summary>
/// Identität und Zugang (A-005, A-007, A-014 bis A-019, A-111): serverseitige Cookie-Sitzung als Standardschema, Anmeldewege,
/// Beitritt, Kiosk, Konto und Verwaltung. Die OIDC-Middleware des Frameworks erhält je Anbieter ein Schema zur Laufzeit.
/// </summary>
public sealed class IdentityModule : IModule
{
    public static ModuleDescriptor Definition { get; } = new("Identity", ModuleSchemas.Identity, DependencyTier: 1);

    public ModuleDescriptor Descriptor => Definition;

    public void AddModule(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<IdentityDbContext>(ModuleSchemas.Identity);
        services.AddOptions<IdentityOptions>().Bind(configuration.GetSection(IdentityOptions.Section));
        services.AddHttpContextAccessor();

        services.AddScoped<IPersonDirectory, PersonDirectory>();
        services.AddScoped<ISessionService, SessionService>();
        services.AddScoped<IPasskeyService, PasskeyService>();
        services.AddScoped<IProviderCatalog, ProviderCatalog>();
        services.AddScoped<ILoginService, LoginService>();
        services.AddScoped<IExternalLoginFlow, ExternalLoginFlow>();
        services.AddScoped<IJoinService, JoinService>();
        services.AddScoped<IKioskService, KioskService>();
        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<AccessAdministration>();
        services.AddScoped<IAccessAdministration>(sp => sp.GetRequiredService<AccessAdministration>());
        services.AddScoped<IJoinLinks>(sp => sp.GetRequiredService<AccessAdministration>());
        services.AddScoped<IDiscoveryDocumentReader, DiscoveryDocumentReader>();
        services.TryAddSingleton<IOidcBackchannel, DefaultOidcBackchannel>();
        services.AddSingleton<OidcSchemeRegistry>();
        services.AddScoped<IPersonLifecycle, PersonLifecycle>();
        services.AddScoped<IdentityPersonalData>();
        services.AddScoped<IPersonalDataExporter>(sp => sp.GetRequiredService<IdentityPersonalData>());
        services.AddScoped<IPersonalDataEraser>(sp => sp.GetRequiredService<IdentityPersonalData>());
        services.AddScheduledTask<KioskDeviceMonthTask>();
        services.AddScheduledTask<IdentityCleanupTask>();
        services.AddScheduledTask<OrphanedPersonsTask>();

        // Sitzungsschema als Standard (A-007); OIDC-Schemata entstehen dynamisch je Anbieter mit dem Sitzungsschema als SignInScheme.
        services.AddAuthentication(SessionCookies.Scheme)
            .AddScheme<AuthenticationSchemeOptions, SessionAuthenticationHandler>(SessionCookies.Scheme, null);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPostConfigureOptions<OpenIdConnectOptions>, OpenIdConnectPostConfigureOptions>());
        services.AddTransient<OpenIdConnectHandler>();
        services.Replace(ServiceDescriptor.Singleton<IAuthenticationSchemeProvider, DynamicOidcSchemeProvider>());
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        IdentityEndpoints.Map(endpoints);
        AuthEndpoints.Map(endpoints);
        JoinEndpoints.Map(endpoints);
        KioskEndpoints.Map(endpoints);
        MeEndpoints.Map(endpoints);
        AccessAdminEndpoints.Map(endpoints);
    }
}
