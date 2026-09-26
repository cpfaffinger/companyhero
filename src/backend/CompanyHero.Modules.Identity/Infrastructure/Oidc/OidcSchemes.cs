using System.Text.RegularExpressions;
using CompanyHero.Modules.Identity.Application;
using CompanyHero.Modules.Identity.Application.ExternalLogin;
using CompanyHero.Modules.Identity.Application.Providers;
using CompanyHero.Modules.Identity.Infrastructure.Http;
using CompanyHero.Platform.Tenancy;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace CompanyHero.Modules.Identity.Infrastructure.Oidc;

/// <summary>Rückkanal zu den Anbietern (Discovery, Token, JWKS). Tests ersetzen ihn durch den In-Process-Anbieter.</summary>
public interface IOidcBackchannel
{
    HttpMessageHandler CreateHandler();
}

internal sealed class DefaultOidcBackchannel : IOidcBackchannel
{
    public HttpMessageHandler CreateHandler() => new HttpClientHandler();
}

/// <summary>Pfade und Schemanamen der Anbieter: ein Schema je Anbieter, ein Callback je Schema (A-007: nur freigegebene Redirect-Ziele).</summary>
public static partial class OidcPaths
{
    public const string SchemePrefix = "oidc:";
    public const string Base = "/api/auth/oidc/";

    public static string Scheme(string providerKey) => SchemePrefix + providerKey;

    public static string Callback(string providerKey) => Base + providerKey + "/callback";

    /// <summary>Anbieterschlüssel aus einem Callback- oder Startpfad, sonst <c>null</c>.</summary>
    public static string? ProviderKeyOf(PathString path)
    {
        var value = path.Value;
        if (value is null || !value.StartsWith(Base, StringComparison.Ordinal))
        {
            return null;
        }

        var rest = value[Base.Length..];
        var slash = rest.IndexOf('/', StringComparison.Ordinal);
        var key = slash < 0 ? rest : rest[..slash];
        return KeyPattern().IsMatch(key) ? key : null;
    }

    [GeneratedRegex("^[a-z0-9]{1,40}$")]
    private static partial Regex KeyPattern();
}

/// <summary>
/// Registriert OIDC-Schemata zur Laufzeit (A-007, A-111): plattformweite Anbieter aus der Konfiguration und tenant-eigene
/// Anbieter aus dem Datenbestand erhalten je ein Schema der Framework-Middleware (Authorization Code Flow mit PKCE, Prüfung
/// von Signatur, Issuer, Audience, State und Nonce durch die Middleware). Die Registrierung läuft auf jeder Instanz beim
/// ersten Bedarf; geänderte Anbieter werden anhand ihres Änderungszeitpunkts neu aufgebaut. Claim-Minimierung: nur
/// <c>openid</c>, keine Token gespeichert, kein UserInfo-Aufruf; aus dem Token werden nur Issuer und Subject verwendet.
/// </summary>
internal sealed class OidcSchemeRegistry(
    IAuthenticationSchemeProvider schemes,
    IOptionsMonitorCache<OpenIdConnectOptions> optionsCache,
    IEnumerable<IPostConfigureOptions<OpenIdConnectOptions>> postConfigure,
    IServiceScopeFactory scopeFactory,
    IOidcBackchannel backchannel,
    ILogger<OidcSchemeRegistry> logger) : IDisposable
{
    private readonly Dictionary<string, DateTimeOffset?> _registered = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = new(1, 1);

    public void Dispose() => _gate.Dispose();

    /// <summary>Stellt sicher, dass das Schema des Anbieters auf dieser Instanz existiert; falsch, wenn es den Anbieter nicht (mehr) gibt.</summary>
    public async Task<bool> EnsureAsync(string providerKey, CancellationToken cancellationToken)
    {
        ProviderRecord? provider;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var scopes = scope.ServiceProvider.GetRequiredService<ITenantScopeFactory>();
            provider = await scopes.RunAsync(TenantContext.ForPlatform(), (sp, ct) => sp.GetRequiredService<IProviderCatalog>().FindAsync(providerKey, ct), cancellationToken);
        }

        var scheme = OidcPaths.Scheme(providerKey);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (provider is null)
            {
                if (_registered.Remove(scheme))
                {
                    schemes.RemoveScheme(scheme);
                    optionsCache.TryRemove(scheme);
                }

                return false;
            }

            if (_registered.TryGetValue(scheme, out var version) && version == provider.UpdatedAt)
            {
                return true;
            }

            schemes.RemoveScheme(scheme);
            optionsCache.TryRemove(scheme);
            var options = Build(provider);
            foreach (var configure in postConfigure)
            {
                configure.PostConfigure(scheme, options);
            }

            options.Validate(scheme);
            optionsCache.TryAdd(scheme, options);
            schemes.AddScheme(new AuthenticationScheme(scheme, provider.DisplayName, typeof(OpenIdConnectHandler)));
            _registered[scheme] = provider.UpdatedAt;
            logger.LogInformation("OIDC-Schema für Anbieter {ProviderKey} registriert.", providerKey);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private OpenIdConnectOptions Build(ProviderRecord provider)
    {
        var options = new OpenIdConnectOptions
        {
            Authority = provider.Authority,
            ClientId = provider.ClientId,
            ClientSecret = provider.ClientSecret,
            ResponseType = OpenIdConnectResponseType.Code,
            ResponseMode = OpenIdConnectResponseMode.Query,
            UsePkce = true,
            CallbackPath = OidcPaths.Callback(provider.Key),
            SignInScheme = SessionCookies.Scheme,
            SaveTokens = false,
            GetClaimsFromUserInfoEndpoint = false,
            MapInboundClaims = false,
            RequireHttpsMetadata = true,
            BackchannelHttpHandler = backchannel.CreateHandler(),
        };
        options.Scope.Clear();
        options.Scope.Add("openid");
        options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.Always;
        options.NonceCookie.SecurePolicy = CookieSecurePolicy.Always;
        options.TokenValidationParameters.NameClaimType = "sub";
        if (provider.IssuerPattern is { Length: > 0 } pattern)
        {
            // Microsoft „common“: der Issuer trägt den Mandanten; gültig ist jeder Issuer nach dem Muster des Operators.
            var regex = new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            options.TokenValidationParameters.IssuerValidator = (issuer, _, _) => regex.IsMatch(issuer) ? issuer : throw new SecurityTokenInvalidIssuerException("Issuer entspricht nicht dem Muster des Anbieters.");
        }

        options.Events.OnTokenValidated = async ctx =>
        {
            var flow = ctx.HttpContext.RequestServices.GetRequiredService<IExternalLoginFlow>();
            var issuer = ctx.SecurityToken?.Issuer ?? throw new InvalidOperationException("Token ohne Issuer.");
            var subject = ctx.Principal?.FindFirst("sub")?.Value ?? throw new InvalidOperationException("Token ohne Subject.");
            var redirect = await flow.CompleteAsync(provider.Key, issuer, subject, ctx.Properties!, ctx.HttpContext, ctx.HttpContext.RequestAborted);
            ctx.Response.Redirect(redirect);
            ctx.HandleResponse();
        };
        options.Events.OnRemoteFailure = ctx =>
        {
            ctx.Response.Redirect("/zugang?fehler=anbieter");
            ctx.HandleResponse();
            return Task.CompletedTask;
        };
        options.Events.OnAccessDenied = ctx =>
        {
            ctx.Response.Redirect("/zugang?fehler=abgelehnt");
            ctx.HandleResponse();
            return Task.CompletedTask;
        };
        return options;
    }
}

/// <summary>
/// Schema-Provider, der OIDC-Schemata bei Bedarf registriert: beim Callback (die Middleware fragt die Request-Handler-Schemata
/// ab) und bei der Anmeldeaufforderung. So kennt jede API-Instanz den Anbieter, auf dem der Callback landet (A-007 Betrieb).
/// </summary>
internal sealed class DynamicOidcSchemeProvider(IOptions<AuthenticationOptions> options, IHttpContextAccessor httpContext, IServiceProvider services) : AuthenticationSchemeProvider(options)
{
    public override async Task<IEnumerable<AuthenticationScheme>> GetRequestHandlerSchemesAsync()
    {
        var path = httpContext.HttpContext?.Request.Path;
        if (path is { } p && OidcPaths.ProviderKeyOf(p) is { } key && p.Value!.EndsWith("/callback", StringComparison.Ordinal))
        {
            await services.GetRequiredService<OidcSchemeRegistry>().EnsureAsync(key, httpContext.HttpContext!.RequestAborted);
        }

        return await base.GetRequestHandlerSchemesAsync();
    }

    public override async Task<AuthenticationScheme?> GetSchemeAsync(string name)
    {
        if (name.StartsWith(OidcPaths.SchemePrefix, StringComparison.Ordinal) && await base.GetSchemeAsync(name) is null)
        {
            await services.GetRequiredService<OidcSchemeRegistry>().EnsureAsync(name[OidcPaths.SchemePrefix.Length..], httpContext.HttpContext?.RequestAborted ?? CancellationToken.None);
        }

        return await base.GetSchemeAsync(name);
    }
}

/// <summary>Liest das Discovery-Dokument eines tenant-eigenen Anbieters über den Rückkanal (Zugang 3.2 Validierung).</summary>
public interface IDiscoveryDocumentReader
{
    Task<Domain.DiscoveryValidation.Document?> ReadAsync(string issuer, CancellationToken cancellationToken);
}

internal sealed class DiscoveryDocumentReader(IOidcBackchannel backchannel) : IDiscoveryDocumentReader
{
    public async Task<Domain.DiscoveryValidation.Document?> ReadAsync(string issuer, CancellationToken cancellationToken)
    {
        using var http = new HttpClient(backchannel.CreateHandler(), disposeHandler: true) { Timeout = TimeSpan.FromSeconds(10) };
        try
        {
            var configuration = await OpenIdConnectConfigurationRetriever.GetAsync(issuer.TrimEnd('/') + "/.well-known/openid-configuration", http, cancellationToken);
            return new Domain.DiscoveryValidation.Document(
                configuration.Issuer,
                configuration.AuthorizationEndpoint,
                configuration.TokenEndpoint,
                configuration.JwksUri,
                configuration.ResponseTypesSupported.ToList(),
                configuration.CodeChallengeMethodsSupported.ToList(),
                configuration.ScopesSupported.ToList());
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException or TaskCanceledException or System.Text.Json.JsonException or ArgumentException)
        {
            return null;
        }
    }
}
