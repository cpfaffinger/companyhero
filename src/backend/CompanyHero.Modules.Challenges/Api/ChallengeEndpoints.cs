using System.Globalization;
using System.Text.Json.Serialization;
using CompanyHero.Modules.Challenges.Application;
using CompanyHero.Modules.Challenges.Domain;
using CompanyHero.Platform.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CompanyHero.Modules.Challenges.Api;

/// <summary>
/// Vertrag nach A-009: Kennungen als Strings, Dezimalwerte als kulturunabhängige Strings, Zeitpunkte nach K13.
/// Idempotenzregime je Kanal: <c>kiosk</c> mit <c>operationId</c> (vom Backend reserviert), <c>mobile</c> mit
/// <c>idempotencyKey</c> (vom Client erzeugt, UUIDv7).
/// </summary>
public sealed record ContributionRequest(string Value, DateTimeOffset RecordedAt, string Channel, string? IdempotencyKey, string? OperationId);

public sealed record ContributionResponse(string ContributionId, string Outcome);

public sealed record OperationResponse(string OperationId);

/// <summary>Kollektivstand mit Altersangabe (Challenges 6.1); <c>percent</c> ist der gerundete Anteil am Sammelziel.</summary>
public sealed record CollectiveResponse(string Total, int ContributionCount, int ContributorCount, DateTimeOffset UpdatedAt, int Percent);

[JsonConverter(typeof(JsonStringEnumConverter<ChallengeMetricDto>))]
public enum ChallengeMetricDto
{
    [JsonStringEnumMemberName("checkmark")] Checkmark,
    [JsonStringEnumMemberName("count")] Count,
}

/// <summary>Daten der Challenge-Karte (Marke 2.5): Sammelziel, Zeitraum, Kollektivstand, eigener Beitrag heute; keine Werte anderer Personen.</summary>
public sealed record ChallengeCardResponse(
    string ChallengeId,
    string Title,
    ChallengeMetricDto Metric,
    string Target,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    CollectiveResponse? Collective,
    int Percent,
    bool ContributedToday);

internal static class ChallengeEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/challenges", async (IChallengeCatalog catalog, CancellationToken ct) =>
            {
                var cards = await catalog.ListRunningAsync(ct);
                return Results.Ok(cards.Select(card => new ChallengeCardResponse(
                    card.Challenge.Id.ToString("D"),
                    card.Challenge.Title,
                    card.Challenge.Metric == ChallengeMetric.Count ? ChallengeMetricDto.Count : ChallengeMetricDto.Checkmark,
                    Decimal(card.Challenge.Target),
                    card.Challenge.StartsAt,
                    card.Challenge.EndsAt,
                    card.Collective is null ? null : ToCollective(card.Collective, card.Percent),
                    card.Percent,
                    card.ContributedToday)).ToList());
            })
            .RequireTenantContext()
            .WithName("ListRunningChallenges")
            .Produces<List<ChallengeCardResponse>>();

        endpoints.MapPost("/api/challenges/{challengeId:guid}/contribution-operations", async (Guid challengeId, IChallengeCatalog catalog, IContributionService contributions, CancellationToken ct) =>
            {
                if (await catalog.GetAsync(challengeId, ct) is null)
                {
                    return Results.NotFound();
                }

                var operationId = await contributions.ReserveOperationAsync(ct);
                return Results.Created($"/api/challenges/{challengeId:D}/contribution-operations/{operationId}", new OperationResponse(operationId));
            })
            .RequireTenantContext()
            .WithName("ReserveContributionOperation")
            .Produces<OperationResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status404NotFound);

        endpoints.MapPost("/api/challenges/{challengeId:guid}/contributions", async (Guid challengeId, ContributionRequest request, IContributionService contributions, CancellationToken ct) =>
            {
                if (!decimal.TryParse(request.Value, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["value"] = ["Dezimalwert als kulturunabhängiger String erwartet."] });
                }

                ContributionChannel channel;
                string? key;
                switch (request.Channel)
                {
                    case "kiosk":
                        channel = ContributionChannel.Kiosk;
                        key = request.OperationId;
                        break;
                    case "mobile":
                        channel = ContributionChannel.Mobile;
                        key = request.IdempotencyKey;
                        break;
                    default:
                        return Results.ValidationProblem(new Dictionary<string, string[]> { ["channel"] = ["kiosk oder mobile"] });
                }

                if (string.IsNullOrWhiteSpace(key) || (channel == ContributionChannel.Mobile ? request.OperationId is not null : request.IdempotencyKey is not null))
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        [channel == ContributionChannel.Kiosk ? "operationId" : "idempotencyKey"] = ["Genau eine Kennung des Regimes dieses Kanals (A-009)."],
                    });
                }

                if (channel == ContributionChannel.Mobile && !ContributionKey.IsValidClientKey(key))
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["idempotencyKey"] = ["Zeitlich sortierbare Kennung (UUIDv7) erwartet."] });
                }

                var outcome = await contributions.SubmitAsync(new ContributionSubmission(challengeId, value, request.RecordedAt, channel, key), ct);
                return outcome.Result switch
                {
                    ContributionResult.Recorded => Results.Created($"/api/challenges/{challengeId:D}/contributions/{outcome.ContributionId:D}", new ContributionResponse(outcome.ContributionId!.Value.ToString("D"), "recorded")),
                    ContributionResult.AlreadyRecorded => Results.Ok(new ContributionResponse(outcome.ContributionId!.Value.ToString("D"), "already_recorded")),
                    ContributionResult.ConflictingContent => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Gleiche Kennung mit abweichendem Inhalt"),
                    ContributionResult.OperationUnknown => Results.NotFound(),
                    ContributionResult.OperationAborted => Results.Problem(statusCode: StatusCodes.Status410Gone, title: "Vorgang abgebrochen"),
                    ContributionResult.ChallengeNotFound => Results.NotFound(),
                    _ => Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "Beitrag abgelehnt", detail: outcome.Rejection.ToString()),
                };
            })
            .RequireTenantContext()
            .WithName("SubmitContribution")
            .Produces<ContributionResponse>(StatusCodes.Status201Created)
            .Produces<ContributionResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status410Gone)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .Produces(StatusCodes.Status404NotFound);

        endpoints.MapGet("/api/challenges/{challengeId:guid}/collective", async (Guid challengeId, IChallengeCatalog catalog, CancellationToken ct) =>
            {
                var challenge = await catalog.GetAsync(challengeId, ct);
                if (challenge is null)
                {
                    return Results.NotFound();
                }

                var collective = await catalog.GetCollectiveAsync(challengeId, ct);
                return collective is null
                    ? Results.Ok(new CollectiveResponse("0.0000", 0, 0, DateTimeOffset.MinValue, 0))
                    : Results.Ok(ToCollective(collective, (int)Math.Min(100m, Math.Round(collective.Total / challenge.Target * 100m, 0, MidpointRounding.AwayFromZero))));
            })
            .RequireTenantContext()
            .WithName("GetChallengeCollective")
            .Produces<CollectiveResponse>()
            .Produces(StatusCodes.Status404NotFound);
    }

    private static CollectiveResponse ToCollective(CollectiveRecord collective, int percent) =>
        new(Decimal(collective.Total), collective.ContributionCount, collective.ContributorCount, collective.UpdatedAt, percent);

    private static string Decimal(decimal value) => value.ToString("0.0000", CultureInfo.InvariantCulture);
}
