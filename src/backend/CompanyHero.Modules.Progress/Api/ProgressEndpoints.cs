using CompanyHero.Modules.Progress.Application;
using CompanyHero.Platform.Hosting;
using CompanyHero.Platform.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CompanyHero.Modules.Progress.Api;

public sealed record ActivityResponse(string Id, string Kind, DateTimeOffset OccurredAt);

internal static class ProgressEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        // Fremder Tenant, unbekannte Person und unsichtbare Werte antworten gleich: 404 (Datenschutz 7, A-026).
        endpoints.MapGet("/api/persons/{personId:guid}/activities", async (Guid personId, IPersonalActivityQuery query, CancellationToken ct) =>
            {
                var activities = await query.GetForPersonAsync(new PersonId(personId), ct);
                return activities is null
                    ? Results.NotFound()
                    : Results.Ok(activities.Select(a => new ActivityResponse(a.Id.ToString("D"), a.Kind, a.OccurredAt)).ToList());
            })
            .RequireTenantContext()
            .WithName("GetPersonActivities")
            .Produces<List<ActivityResponse>>()
            .Produces(StatusCodes.Status404NotFound);
    }
}
