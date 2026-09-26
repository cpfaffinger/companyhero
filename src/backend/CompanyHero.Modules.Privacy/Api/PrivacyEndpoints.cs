using System.Text.Json.Serialization;
using CompanyHero.Modules.Organisation.Domain;
using CompanyHero.Modules.Privacy.Application;
using CompanyHero.Modules.Privacy.Domain;
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

[JsonConverter(typeof(JsonStringEnumConverter<VisibilityLevelDto>))]
public enum VisibilityLevelDto
{
    [JsonStringEnumMemberName("only_me")] OnlyMe,
    [JsonStringEnumMemberName("team")] Team,
    [JsonStringEnumMemberName("company")] Company,
}

/// <summary>Die eigene Sichtbarkeitsstufe (Datenschutz 3.1): dauerhaft sichtbar, mit einer Berührung änderbar.</summary>
public sealed record VisibilityResponse(VisibilityLevelDto Level, DateTimeOffset ChosenAt);

public sealed record VisibilityRequest(VisibilityLevelDto Level);

/// <summary>Eintrag des eigenen Zustimmungsprotokolls (Datenschutz 6.1).</summary>
public sealed record ConsentResponse(string EntryId, DateTimeOffset OccurredAt, string Kind, string? Previous, string Next);

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

        // Sichtbarkeitsstufe (Datenschutz 3.1, A-022): Chip im Profil, Änderung wirkt sofort und rückwirkend; am Kiosk nicht.
        endpoints.MapGet("/api/me/visibility", async (ITenantContextAccessor context, IVisibilityChoice choice, CancellationToken ct) =>
            {
                var level = await choice.GetAsync(context.Require().RequirePerson(), ct);
                return level is null ? Results.NotFound() : Results.Ok(new VisibilityResponse(ToDto(level.Value), DateTimeOffset.MinValue));
            })
            .RequireTenantContext()
            .WithName("GetMyVisibility")
            .Produces<VisibilityResponse>()
            .Produces(StatusCodes.Status404NotFound);

        endpoints.MapPut("/api/me/visibility", async (VisibilityRequest request, ITenantContextAccessor context, IVisibilityChoice choice, CancellationToken ct) =>
            {
                await choice.ChooseAsync(context.Require().RequirePerson(), FromDto(request.Level), ct);
                return Results.NoContent();
            })
            .RequireTenantContext()
            .WithName("ChangeMyVisibility")
            .Produces(StatusCodes.Status204NoContent);

        endpoints.MapGet("/api/me/consents", async (IPersonalDataService personalData, CancellationToken ct) =>
            {
                var consents = await personalData.ListConsentsAsync(ct);
                return Results.Ok(consents.Select(c => new ConsentResponse(c.Id.ToString("D"), c.OccurredAt, c.Kind, c.Previous, c.Next)).ToList());
            })
            .RequireTenantContext()
            .WithName("ListMyConsents")
            .Produces<List<ConsentResponse>>();

        // Auskunft (Datenschutz 6.4, A-025): maschinenlesbarer Selbstexport aller Daten mit Personenbezug, nur auf dem eigenen Gerät.
        endpoints.MapGet("/api/me/export", async (IPersonalDataService personalData, CancellationToken ct) =>
            {
                var export = await personalData.ExportAsync(ct);
                return Results.Text(export.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }), "application/json");
            })
            .RequireTenantContext()
            .WithName("ExportMyData")
            .Produces<System.Text.Json.Nodes.JsonObject>(StatusCodes.Status200OK, "application/json");
    }

    private static bool Allowed(ITenantContextAccessor context)
    {
        var current = context.Require();
        return current.HasRole(Role.TenantAdmin) || current.HasRole(Role.Insight);
    }

    private static VisibilityLevelDto ToDto(VisibilityLevel level) => level switch
    {
        VisibilityLevel.OnlyMe => VisibilityLevelDto.OnlyMe,
        VisibilityLevel.Team => VisibilityLevelDto.Team,
        _ => VisibilityLevelDto.Company,
    };

    private static VisibilityLevel FromDto(VisibilityLevelDto level) => level switch
    {
        VisibilityLevelDto.OnlyMe => VisibilityLevel.OnlyMe,
        VisibilityLevelDto.Team => VisibilityLevel.Team,
        _ => VisibilityLevel.Company,
    };
}
