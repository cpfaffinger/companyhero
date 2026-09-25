using System.Security.Claims;
using CompanyHero.Platform.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CompanyHero.Platform.Hosting;

/// <summary>
/// Ermittelt je Request genau einen geprüften Kontext (Backend 5.1 Nr. 1 und 2): Tenant und Person kommen aus der
/// geprüften Sitzung (Claims), Mitgliedschaft und Rollen aus Organisation. Subdomain, URL oder Header benennen
/// keinen Tenant. Ohne gültige Mitgliedschaft bleibt der Request ohne Kontext; Endpunkte mit
/// <see cref="TenantContextEndpointExtensions.RequireTenantContext{TBuilder}"/> antworten dann mit 401.
/// </summary>
public sealed class TenantContextMiddleware(RequestDelegate next, ILogger<TenantContextMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var user = httpContext.User;
        if (user.Identity?.IsAuthenticated == true)
        {
            var accessor = httpContext.RequestServices.GetRequiredService<TenantContextAccessor>();
            var context = await ResolveAsync(user, httpContext.RequestServices, httpContext.RequestAborted);
            accessor.Set(context);
        }

        await next(httpContext);
    }

    private async Task<TenantContext?> ResolveAsync(ClaimsPrincipal user, IServiceProvider services, CancellationToken cancellationToken)
    {
        var kind = user.FindFirstValue(CompanyHeroClaims.Context) ?? CompanyHeroClaims.ContextTenant;
        var person = ParseId(user.FindFirstValue(CompanyHeroClaims.Person));

        if (string.Equals(kind, CompanyHeroClaims.ContextPlatform, StringComparison.Ordinal))
        {
            return TenantContext.ForPlatform(person is { } p ? new PersonId(p) : null);
        }

        var tenant = ParseId(user.FindFirstValue(CompanyHeroClaims.Tenant));
        if (tenant is null || person is null)
        {
            logger.LogWarning("Sitzung ohne Tenant- oder Personenkennung; kein Kontext gesetzt.");
            return null;
        }

        var tenantId = new TenantId(tenant.Value);
        var personId = new PersonId(person.Value);

        // Die Mitgliedschaft liegt selbst unter RLS: Prüfung im provisorischen Tenant-Kontext ohne Person und ohne Rollen.
        var accessor = services.GetRequiredService<TenantContextAccessor>();
        accessor.Set(TenantContext.ForTenant(tenantId));
        try
        {
            var verdict = await services.GetRequiredService<IMembershipVerification>().VerifyAsync(tenantId, personId, cancellationToken);
            if (!verdict.Active)
            {
                logger.LogWarning("Sitzung ohne aktive Mitgliedschaft; kein Kontext gesetzt.");
                return null;
            }

            return TenantContext.ForPerson(tenantId, personId, verdict.Roles);
        }
        finally
        {
            accessor.Set(null);
        }
    }

    private static Guid? ParseId(string? value) =>
        Guid.TryParseExact(value, "D", out var id) && id != Guid.Empty ? id : null;
}

public static class TenantContextEndpointExtensions
{
    /// <summary>Nach der Authentifizierung; setzt den Kontext des Requests.</summary>
    public static IApplicationBuilder UseTenantContext(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseMiddleware<TenantContextMiddleware>();
    }

    /// <summary>Endpunkt nur mit Tenant-Kontext einer Person; sonst 401 (kein Kontext) beziehungsweise 403 (Plattformkontext).</summary>
    public static TBuilder RequireTenantContext<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddEndpointFilter(async (invocation, next) =>
        {
            var current = invocation.HttpContext.RequestServices.GetRequiredService<ITenantContextAccessor>().Current;
            if (current is null)
            {
                return Results.Unauthorized();
            }

            if (current.Kind != TenantContextKind.Tenant || current.PersonId is null)
            {
                return Results.Forbid();
            }

            return await next(invocation);
        });
        return builder;
    }
}
