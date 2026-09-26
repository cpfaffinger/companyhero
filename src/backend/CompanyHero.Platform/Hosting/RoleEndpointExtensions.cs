using CompanyHero.Platform.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyHero.Platform.Hosting;

/// <summary>Metadatum: Rollen, die den Endpunkt aufrufen dürfen (Rechtematrix, Organisation 4.2, A-036).</summary>
public sealed record RequiredRolesMetadata(IReadOnlySet<string> Roles);

public static class RoleEndpointExtensions
{
    /// <summary>
    /// Endpunkt nur für Personen mit mindestens einer der genannten Rollen laut Organisation (Rechtematrix, A-036).
    /// Jede Zelle mit „–“ wird mit 403 abgelehnt; die Rollen kommen aus dem geprüften Kontext, nie aus Anbieterclaims.
    /// Läuft nach <see cref="TenantContextEndpointExtensions.RequireTenantContext{TBuilder}"/>, das den Kontext sicherstellt.
    /// </summary>
    public static TBuilder RequireRoles<TBuilder>(this TBuilder builder, params string[] roles) where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (roles.Length == 0)
        {
            throw new ArgumentException("Mindestens eine Rolle.", nameof(roles));
        }

        var allowed = new HashSet<string>(roles, StringComparer.Ordinal);
        builder.WithMetadata(new RequiredRolesMetadata(allowed));
        builder.AddEndpointFilter(async (invocation, next) =>
        {
            var current = invocation.HttpContext.RequestServices.GetRequiredService<ITenantContextAccessor>().Current;
            if (current is null)
            {
                return Results.Unauthorized();
            }

            return current.Roles.Overlaps(allowed) ? await next(invocation) : Results.Forbid();
        });
        return builder;
    }
}
