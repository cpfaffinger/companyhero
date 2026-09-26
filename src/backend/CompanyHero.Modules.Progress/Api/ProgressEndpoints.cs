using System.Text.Json.Serialization;
using CompanyHero.Modules.Organisation.Domain;
using CompanyHero.Modules.Progress.Application;
using CompanyHero.Modules.Progress.Domain;
using CompanyHero.Platform.Hosting;
using CompanyHero.Platform.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CompanyHero.Modules.Progress.Api;

public sealed record ActivityResponse(string Id, string Kind, DateTimeOffset OccurredAt, int Points, bool Reversed);

[JsonConverter(typeof(JsonStringEnumConverter<BadgeCategoryDto>))]
public enum BadgeCategoryDto
{
    [JsonStringEnumMemberName("einstieg")] Einstieg,
    [JsonStringEnumMemberName("dranbleiben")] Dranbleiben,
    [JsonStringEnumMemberName("gemeinsam")] Gemeinsam,
    [JsonStringEnumMemberName("vielfalt")] Vielfalt,
    [JsonStringEnumMemberName("saison")] Saison,
}

/// <summary>Abzeichen mit Textschlüssel aus dem Katalog des Operators (Fortschritt 4.1); verdiente mit Datum, nächste ohne.</summary>
public sealed record BadgeResponse(string Key, BadgeCategoryDto Category, string TextKey, DateTimeOffset? AwardedAt);

/// <summary>Persönlicher Fortschritt (Fortschritt 6.2): nur für die Person; Bezeichnungen der Stufen kommen aus der Marke.</summary>
public sealed record PersonalProgressResponse(int Points, int Level, int CurrentStreak, int LongestStreak, int DailyGoal, int TodayActions, bool CheckedInToday, IReadOnlyList<BadgeResponse> Earned, IReadOnlyList<BadgeResponse> Next);

public sealed record DailyGoalRequest(int Goal);

[JsonConverter(typeof(JsonStringEnumConverter<CheckInTileDto>))]
public enum CheckInTileDto
{
    [JsonStringEnumMemberName("moved")] Moved,
    [JsonStringEnumMemberName("paused")] Paused,
    [JsonStringEnumMemberName("rested")] Rested,
}

/// <summary>Täglicher Check-in (Fortschritt 6.1): drei Kacheln, Mehrfachauswahl, kein Rating.</summary>
public sealed record CheckInRequest(IReadOnlyList<CheckInTileDto> Tiles);

public sealed record CheckInResponse(string Outcome);

/// <summary>Beteiligung (Datenschutz 4.1): unter fünf Personen mit Handlung <c>available=false</c> ohne jede Zahl.</summary>
public sealed record ParticipationResponse(string Period, string? GroupId, bool Available, int? ActivePersons, int? Headcount, int? Percent);

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
                    : Results.Ok(activities.Select(a => new ActivityResponse(a.Id.ToString("D"), a.Kind, a.OccurredAt, a.Points, a.Reversed)).ToList());
            })
            .RequireTenantContext()
            .WithName("GetPersonActivities")
            .Produces<List<ActivityResponse>>()
            .Produces(StatusCodes.Status404NotFound);

        endpoints.MapGet("/api/me/progress", async (IProgressQuery query, CancellationToken ct) =>
            {
                var p = await query.GetMineAsync(ct);
                return Results.Ok(new PersonalProgressResponse(p.PointsBalance, p.Level, p.CurrentStreak, p.LongestStreak, p.DailyGoal, p.TodayActions, p.CheckedInToday, p.Earned.Select(ToDto).ToList(), p.Next.Select(ToDto).ToList()));
            })
            .RequireTenantContext()
            .WithName("GetMyProgress")
            .Produces<PersonalProgressResponse>();

        endpoints.MapPut("/api/me/progress/daily-goal", async (DailyGoalRequest request, IProgressQuery query, CancellationToken ct) =>
            {
                if (request.Goal is < 1 or > 3)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["goal"] = ["1 bis 3 Handlungen je Tag."] });
                }

                await query.SetDailyGoalAsync(request.Goal, ct);
                return Results.NoContent();
            })
            .RequireTenantContext()
            .WithName("SetMyDailyGoal")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem();

        // Check-in auch am Kiosk (Fortschritt 6.1): ein Tap, einmal je Tag.
        endpoints.MapPost("/api/me/check-in", async (CheckInRequest request, ICheckInService checkIns, CancellationToken ct) =>
            {
                var tiles = CheckInTiles.None;
                foreach (var tile in request.Tiles ?? [])
                {
                    tiles |= tile switch { CheckInTileDto.Moved => CheckInTiles.Moved, CheckInTileDto.Paused => CheckInTiles.Paused, _ => CheckInTiles.Rested };
                }

                if (tiles == CheckInTiles.None)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["tiles"] = ["Mindestens eine Kachel."] });
                }

                var outcome = await checkIns.CheckInAsync(tiles, ct);
                return outcome == CheckInOutcome.Recorded
                    ? Results.Created("/api/me/progress", new CheckInResponse("recorded"))
                    : Results.Ok(new CheckInResponse("already_today"));
            })
            .RequireTenantContext().AllowKiosk()
            .WithName("CheckIn")
            .Produces<CheckInResponse>(StatusCodes.Status201Created)
            .Produces<CheckInResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem();

        endpoints.MapGet("/api/progress/participation", async (string? period, Guid? groupId, IProgressAggregates aggregates, CancellationToken ct) =>
            {
                var p = period is "week" or "month" ? period : "month";
                try
                {
                    var result = await aggregates.ParticipationAsync(groupId, p, ct);
                    return Results.Ok(new ParticipationResponse(p, groupId?.ToString("D"), result.Available, result.ActivePersons, result.Headcount, result.Percent));
                }
                catch (UnauthorizedAccessException)
                {
                    return Results.Forbid();
                }
            })
            .RequireTenantContext().RequireRoles(Role.TenantAdmin, Role.ProgrammeManager, Role.Insight)
            .WithName("GetParticipation")
            .Produces<ParticipationResponse>()
            .Produces(StatusCodes.Status403Forbidden);
    }

    private static BadgeResponse ToDto(BadgeRecord badge) => new(badge.Key, badge.Category switch
    {
        BadgeCategory.Einstieg => BadgeCategoryDto.Einstieg,
        BadgeCategory.Dranbleiben => BadgeCategoryDto.Dranbleiben,
        BadgeCategory.Gemeinsam => BadgeCategoryDto.Gemeinsam,
        BadgeCategory.Vielfalt => BadgeCategoryDto.Vielfalt,
        _ => BadgeCategoryDto.Saison,
    }, badge.TextKey, badge.AwardedAt);
}
