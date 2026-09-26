using System.Globalization;
using System.Security.Claims;
using CompanyHero.Platform.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CompanyHero.Platform.Hosting;

/// <summary>
/// Ermittelt je Request genau einen geprüften Kontext (Backend 5.1 Nr. 1 und 2): Tenant, Person und Sitzungsart kommen
/// aus der geprüften Sitzung (Claims), Mitgliedschaft und Rollen aus Organisation. Subdomain, URL oder Header benennen
/// keinen Tenant. Ohne gültige Mitgliedschaft bleibt der Request ohne Kontext; Endpunkte mit
/// <see cref="TenantContextEndpointExtensions.RequireTenantContext{TBuilder}"/> antworten dann mit 401.
/// Kiosk-Personensitzungen erhalten nur die Rolle Mitglied (Zugang 6.3: keine Verwaltung am Gemeinschaftsgerät).
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
            var context = await ResolveAsync(user, httpContext.RequestServices, httpContext.Items, httpContext.RequestAborted);
            accessor.Set(context);
        }

        await next(httpContext);
    }

    /// <summary>Schlüssel in <c>HttpContext.Items</c> mit dem neutralen Grund, warum eine geprüfte Sitzung keinen Kontext erhielt (Organisation 1.3).</summary>
    public const string DeniedReasonItem = "ch:denied_reason";

    private async Task<TenantContext?> ResolveAsync(ClaimsPrincipal user, IServiceProvider services, IDictionary<object, object?> httpItems, CancellationToken cancellationToken)
    {
        var kind = user.FindFirstValue(CompanyHeroClaims.Context) ?? CompanyHeroClaims.ContextTenant;
        var person = ParseId(user.FindFirstValue(CompanyHeroClaims.Person));

        if (string.Equals(kind, CompanyHeroClaims.ContextPlatform, StringComparison.Ordinal))
        {
            return TenantContext.ForPlatform(person is { } p ? new PersonId(p) : null);
        }

        var tenant = ParseId(user.FindFirstValue(CompanyHeroClaims.Tenant));
        var session = Enum.TryParse<SessionKind>(user.FindFirstValue(CompanyHeroClaims.Session), out var parsedSession) ? parsedSession : SessionKind.Member;
        var device = ParseId(user.FindFirstValue(CompanyHeroClaims.KioskDevice));
        var authenticatedAt = DateTimeOffset.TryParse(user.FindFirstValue(CompanyHeroClaims.AuthenticatedAt), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var at) ? at : (DateTimeOffset?)null;

        if (tenant is null)
        {
            logger.LogWarning("Sitzung ohne Tenantkennung; kein Kontext gesetzt.");
            return null;
        }

        var tenantId = new TenantId(tenant.Value);
        var accessor = services.GetRequiredService<TenantContextAccessor>();
        var verification = services.GetRequiredService<IMembershipVerification>();

        if (session == SessionKind.KioskDevice)
        {
            if (device is null)
            {
                return null;
            }

            accessor.Set(TenantContext.ForTenant(tenantId));
            try
            {
                return await verification.VerifyTenantAsync(tenantId, cancellationToken) ? TenantContext.ForKioskDevice(tenantId, device.Value) : null;
            }
            finally
            {
                accessor.Set(null);
            }
        }

        if (person is null)
        {
            logger.LogWarning("Sitzung ohne Personenkennung; kein Kontext gesetzt.");
            return null;
        }

        var personId = new PersonId(person.Value);

        // Die Mitgliedschaft liegt selbst unter RLS: Prüfung im provisorischen Tenant-Kontext ohne Person und ohne Rollen.
        accessor.Set(TenantContext.ForTenant(tenantId));
        try
        {
            var verdict = await verification.VerifyAsync(tenantId, personId, cancellationToken);
            if (!verdict.Active)
            {
                logger.LogWarning("Sitzung ohne aktive Mitgliedschaft; kein Kontext gesetzt.");
                if (verdict.DeniedReason is not null)
                {
                    httpItems[DeniedReasonItem] = verdict.DeniedReason;
                }

                return null;
            }

            var roles = session == SessionKind.KioskPerson ? verdict.Roles.Where(r => string.Equals(r, "member", StringComparison.Ordinal)) : verdict.Roles;
            return TenantContext.ForPerson(tenantId, personId, roles, session, authenticatedAt, device, verdict.ReadOnly);
        }
        finally
        {
            accessor.Set(null);
        }
    }

    private static Guid? ParseId(string? value) =>
        Guid.TryParseExact(value, "D", out var id) && id != Guid.Empty ? id : null;
}

/// <summary>Kiosk-Freigabe eines Endpunkts (Zugang 6.3): ohne Metadatum ist ein Endpunkt für Kiosk-Sitzungen gesperrt.</summary>
public sealed record KioskAccessMetadata(bool Device, bool Person);

/// <summary>Metadatum: sensible Aktion, Anmeldung höchstens so alt (Zugang 3.3, A-015).</summary>
public sealed record FreshLoginMetadata(TimeSpan MaxAge);

public static class TenantContextEndpointExtensions
{
    /// <summary>Höchstalter der Anmeldung für sensible Aktionen (Zugang 3.3).</summary>
    public static TimeSpan FreshLoginMaxAge { get; } = TimeSpan.FromMinutes(15);

    /// <summary>Nach der Authentifizierung; setzt den Kontext des Requests.</summary>
    public static IApplicationBuilder UseTenantContext(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseMiddleware<TenantContextMiddleware>();
    }

    /// <summary>
    /// Endpunkt nur mit Tenant-Kontext einer Person; sonst 401 (kein Kontext) beziehungsweise 403 (Plattformkontext).
    /// Kiosk-Sitzungen sind gesperrt, solange der Endpunkt sie nicht über <see cref="AllowKiosk{TBuilder}"/> zulässt;
    /// eine Gerätesitzung ohne Person besteht nur mit dieser Freigabe.
    /// </summary>
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

            if (current.Kind != TenantContextKind.Tenant)
            {
                return Results.Forbid();
            }

            var kiosk = invocation.HttpContext.GetEndpoint()?.Metadata.GetMetadata<KioskAccessMetadata>();
            if (current.Session == SessionKind.KioskDevice)
            {
                return kiosk is { Device: true } ? await next(invocation) : Results.Forbid();
            }

            if (current.Session == SessionKind.KioskPerson && kiosk is not { Person: true })
            {
                return Results.Forbid();
            }

            if (current.PersonId is null)
            {
                return Results.Forbid();
            }

            // Lesefrist nach der Kündigung (Organisation 1.3): Tenant-Admin und Einsichtsrolle lesen, nichts ändert sich mehr.
            if (current.ReadOnly && !HttpMethods.IsGet(invocation.HttpContext.Request.Method) && !HttpMethods.IsHead(invocation.HttpContext.Request.Method))
            {
                return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Nur lesender Zugriff", detail: "tenant_read_only");
            }

            var fresh = invocation.HttpContext.GetEndpoint()?.Metadata.GetMetadata<FreshLoginMetadata>();
            if (fresh is not null)
            {
                var clock = invocation.HttpContext.RequestServices.GetRequiredService<TimeProvider>();
                if (current.IsKiosk || current.AuthenticatedAt is null || clock.GetUtcNow() - current.AuthenticatedAt.Value > fresh.MaxAge)
                {
                    return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Frische Anmeldung erforderlich", detail: "fresh_login_required");
                }
            }

            return await next(invocation);
        });
        return builder;
    }

    /// <summary>Lässt Kiosk-Sitzungen zu: die Personensitzung und, wenn <paramref name="device"/> gesetzt ist, auch die Gerätesitzung ohne Person.</summary>
    public static TBuilder AllowKiosk<TBuilder>(this TBuilder builder, bool device = false) where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.WithMetadata(new KioskAccessMetadata(device, Person: true));
        return builder;
    }

    /// <summary>Sensible Aktion (Zugang 3.3): Anmeldung nicht älter als 15 Minuten; Kiosk-Sitzungen nie.</summary>
    public static TBuilder RequireFreshLogin<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.WithMetadata(new FreshLoginMetadata(FreshLoginMaxAge));
        return builder;
    }
}
