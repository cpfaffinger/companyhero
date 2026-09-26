using CompanyHero.Modules.Organisation.Domain;
using CompanyHero.Modules.Privacy.Application;
using CompanyHero.Platform.Hosting;
using CompanyHero.Platform.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CompanyHero.Modules.Privacy.Api;

/// <summary>Eintrag des Prüfprotokolls (Datenschutz 6.2): handelnde Person als Kennung mit Rollen, Aktion, Objektbezug, kein Anzeigename.</summary>
public sealed record AuditEntryResponse(string EntryId, DateTimeOffset OccurredAt, string? ActorId, IReadOnlyList<string> ActorRoles, string Action, string? SubjectRef, string? Detail);

/// <summary>Eintrag des pseudonymisierten Sicherheitsprotokolls (Datenschutz 5.1).</summary>
public sealed record SecurityEntryResponse(string EntryId, DateTimeOffset OccurredAt, string EventType, bool Success, string? Pseudonym, string? Detail);

internal static class PrivacyEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/audit", async (ITenantContextAccessor context, IAuditReader reader, CancellationToken ct) =>
            {
                if (!Allowed(context))
                {
                    return Results.Forbid();
                }

                var entries = await reader.ListAuditAsync(ct);
                return Results.Ok(entries.Select(e => new AuditEntryResponse(e.Id.ToString("D"), e.OccurredAt, e.Actor, e.ActorRoles.Length == 0 ? [] : e.ActorRoles.Split(','), e.Action, e.SubjectRef, e.Detail)).ToList());
            })
            .RequireTenantContext()
            .WithName("ListAuditEntries")
            .Produces<List<AuditEntryResponse>>();

        endpoints.MapGet("/api/audit/security", async (ITenantContextAccessor context, IAuditReader reader, CancellationToken ct) =>
            {
                if (!Allowed(context))
                {
                    return Results.Forbid();
                }

                var entries = await reader.ListSecurityAsync(ct);
                return Results.Ok(entries.Select(e => new SecurityEntryResponse(e.Id.ToString("D"), e.OccurredAt, e.EventType, e.Success, e.Pseudonym, e.Detail)).ToList());
            })
            .RequireTenantContext()
            .WithName("ListSecurityEntries")
            .Produces<List<SecurityEntryResponse>>();
    }

    private static bool Allowed(ITenantContextAccessor context)
    {
        var current = context.Require();
        return current.HasRole(Role.TenantAdmin) || current.HasRole(Role.Insight);
    }
}
