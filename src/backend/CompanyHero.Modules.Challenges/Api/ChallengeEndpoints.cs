using System.Globalization;
using System.Text.Json.Serialization;
using CompanyHero.Modules.Challenges.Application;
using CompanyHero.Modules.Challenges.Domain;
using CompanyHero.Modules.Organisation.Domain;
using CompanyHero.Modules.Privacy.Domain;
using CompanyHero.Platform.Entitlements;
using CompanyHero.Platform.Hosting;
using CompanyHero.Platform.Tenancy;
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

/// <summary>Kollektivstand mit Altersangabe (Challenges 6.1); <c>percent</c> ist der gerundete Anteil am Sammelziel. Unter fünf Beitragenden nur der Prozentwert (A-023).</summary>
public sealed record CollectiveResponse(string? Total, int? ContributionCount, int? ContributorCount, DateTimeOffset UpdatedAt, int Percent);

[JsonConverter(typeof(JsonStringEnumConverter<ChallengeMetricDto>))]
public enum ChallengeMetricDto
{
    [JsonStringEnumMemberName("checkmark")] Checkmark,
    [JsonStringEnumMemberName("count")] Count,
}

[JsonConverter(typeof(JsonStringEnumConverter<ChallengeStateDto>))]
public enum ChallengeStateDto
{
    [JsonStringEnumMemberName("draft")] Draft,
    [JsonStringEnumMemberName("planned")] Planned,
    [JsonStringEnumMemberName("running")] Running,
    [JsonStringEnumMemberName("grace")] Grace,
    [JsonStringEnumMemberName("ended")] Ended,
    [JsonStringEnumMemberName("archived")] Archived,
}

[JsonConverter(typeof(JsonStringEnumConverter<ChallengeVisibilityDto>))]
public enum ChallengeVisibilityDto
{
    [JsonStringEnumMemberName("only_me")] OnlyMe,
    [JsonStringEnumMemberName("team")] Team,
    [JsonStringEnumMemberName("company")] Company,
}

/// <summary>Daten der Challenge-Karte (Marke 2.5): Sammelziel, Zeitraum, Zustand, Kollektivstand, eigener Beitrag heute; keine Werte anderer Personen.</summary>
public sealed record ChallengeCardResponse(
    string ChallengeId,
    string Title,
    string? Description,
    ChallengeMetricDto Metric,
    string Target,
    ChallengeStateDto State,
    ChallengeVisibilityDto Visibility,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    CollectiveResponse? Collective,
    int Percent,
    bool ContributedToday,
    bool Previewed,
    string? TemplateKey);

/// <summary>Wizard (Challenges 2, 4): Achsen des Durchstichs; Sammelziel und ganze Firma sind gesetzt.</summary>
public sealed record ChallengeDraftRequest(string Title, string? Description, ChallengeMetricDto Metric, string Target, DateTimeOffset StartsAt, DateTimeOffset EndsAt, ChallengeVisibilityDto Visibility);

public sealed record ChallengeTextsRequest(string Title, string? Description);

public sealed record EndChallengeRequest(string Reason);

public sealed record OwnContributionResponse(string ContributionId, string Value, DateTimeOffset RecordedAt, string Channel, bool Reversed, bool IsReversal);

public sealed record ReversalResponse(string Outcome);

/// <summary>Tenant-Export der Challenges (Entitlements 4.3, A-024): nach <c>aktiv_bis</c> 90 Tage lesbar, nur Karten und Kollektivstände ohne Personenbezug.</summary>
public sealed record ChallengeExportResponse(DateTimeOffset ExportedAt, IReadOnlyList<ChallengeCardResponse> Challenges);

internal static class ChallengeEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        // Jede Operation des Moduls M1 prüft das Entitlement im Autorisierungsquerschnitt; ohne Entitlement „nicht gefunden“ (Entitlements 5, A-067).
        var group = endpoints.MapGroup("/api/challenges").HandleChallengeErrors();

        group.MapGet("/", async (IChallengeCatalog catalog, CancellationToken ct) =>
            {
                var cards = await catalog.ListRunningAsync(ct);
                return Results.Ok(cards.Select(ToCard).ToList());
            })
            .RequireTenantContext().RequireModule(ModuleCodes.M1Challenges).AllowKiosk(device: true)
            .WithName("ListRunningChallenges")
            .Produces<List<ChallengeCardResponse>>();

        group.MapGet("/manage", async (IChallengeCatalog catalog, CancellationToken ct) =>
            {
                var cards = await catalog.ListAllAsync(ct);
                return Results.Ok(cards.Select(ToCard).ToList());
            })
            .RequireTenantContext().RequireModule(ModuleCodes.M1Challenges).RequireRoles(Role.ProgrammeManager, Role.TenantAdmin, Role.Insight)
            .WithName("ListAllChallenges")
            .Produces<List<ChallengeCardResponse>>();

        // Wizard (Challenges 4.1, 4.2): nur Programm-Manager oder Tenant-Admin legen an; Botschafter und Mitglieder werden abgelehnt.
        group.MapPost("/", async (ChallengeDraftRequest request, IChallengeCatalog catalog, CancellationToken ct) =>
            {
                if (!decimal.TryParse(request.Target, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var target))
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["target"] = ["Dezimalwert als kulturunabhängiger String erwartet."] });
                }

                var id = await catalog.CreateDraftAsync(new ChallengeDraft(request.Title, request.Description, ChallengeCards.FromDto(request.Metric), target, request.StartsAt, request.EndsAt, ChallengeCards.FromDto(request.Visibility), null), ct);
                var record = await catalog.GetAsync(id, ct);
                return Results.Created($"/api/challenges/{id:D}", ToCard(new ChallengeCardRecord(record!, null, 0, false)));
            })
            .RequireTenantContext().RequireModule(ModuleCodes.M1Challenges).RequireRoles(Role.ProgrammeManager, Role.TenantAdmin)
            .WithName("CreateChallengeDraft")
            .Produces<ChallengeCardResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        group.MapPost("/kickoff", async (IChallengeCatalog catalog, CancellationToken ct) =>
            {
                var id = await catalog.CreateKickoffDraftAsync(ct);
                var record = await catalog.GetAsync(id, ct);
                return Results.Created($"/api/challenges/{id:D}", ToCard(new ChallengeCardRecord(record!, null, 0, false)));
            })
            .RequireTenantContext().RequireModule(ModuleCodes.M1Challenges).RequireRoles(Role.ProgrammeManager, Role.TenantAdmin)
            .WithName("CreateKickoffChallenge")
            .Produces<ChallengeCardResponse>(StatusCodes.Status201Created);

        // Export für den Tenant (Entitlements 5 „Daten“): auch in den 90 Tagen nach aktiv_bis erreichbar, danach „nicht gefunden“.
        group.MapGet("/export", async (IChallengeCatalog catalog, TimeProvider clock, CancellationToken ct) =>
            {
                var cards = await catalog.ListAllAsync(ct);
                return Results.Ok(new ChallengeExportResponse(clock.GetUtcNow(), cards.Select(ToCard).ToList()));
            })
            .RequireTenantContext().RequireModule(ModuleCodes.M1Challenges, allowExportWindow: true).RequireRoles(Role.TenantAdmin, Role.Insight)
            .WithName("ExportChallenges")
            .Produces<ChallengeExportResponse>();

        group.MapGet("/{challengeId:guid}", async (Guid challengeId, IChallengeCatalog catalog, CancellationToken ct) =>
            {
                var record = await catalog.GetAsync(challengeId, ct);
                if (record is null)
                {
                    return Results.NotFound();
                }

                var collective = await catalog.GetCollectiveAsync(challengeId, ct);
                var percent = collective is null ? 0 : (int)Math.Min(100m, Math.Max(0m, Math.Round(collective.Total / record.Target * 100m, 0, MidpointRounding.AwayFromZero)));
                return Results.Ok(ToCard(new ChallengeCardRecord(record, collective, percent, false)));
            })
            .RequireTenantContext().RequireModule(ModuleCodes.M1Challenges).AllowKiosk(device: true)
            .WithName("GetChallenge")
            .Produces<ChallengeCardResponse>()
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/{challengeId:guid}/preview", async (Guid challengeId, IChallengeCatalog catalog, CancellationToken ct) =>
            {
                var card = await catalog.PreviewAsync(challengeId, ct);
                return card is null ? Results.NotFound() : Results.Ok(ToCard(card));
            })
            .RequireTenantContext().RequireModule(ModuleCodes.M1Challenges).RequireRoles(Role.ProgrammeManager, Role.TenantAdmin)
            .WithName("PreviewChallenge")
            .Produces<ChallengeCardResponse>()
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/{challengeId:guid}/plan", async (Guid challengeId, IChallengeCatalog catalog, CancellationToken ct) =>
                await catalog.PlanAsync(challengeId, ct) ? Results.NoContent() : Results.NotFound())
            .RequireTenantContext().RequireModule(ModuleCodes.M1Challenges).RequireRoles(Role.ProgrammeManager, Role.TenantAdmin)
            .WithName("PlanChallenge")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{challengeId:guid}/end", async (Guid challengeId, EndChallengeRequest request, IChallengeCatalog catalog, CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(request.Reason))
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["reason"] = ["Begründung ist Pflicht (Challenges 4.1)."] });
                }

                return await catalog.EndEarlyAsync(challengeId, request.Reason, ct) ? Results.NoContent() : Results.NotFound();
            })
            .RequireTenantContext().RequireModule(ModuleCodes.M1Challenges).RequireRoles(Role.ProgrammeManager)
            .WithName("EndChallengeEarly")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/{challengeId:guid}/texts", async (Guid challengeId, ChallengeTextsRequest request, IChallengeCatalog catalog, CancellationToken ct) =>
                await catalog.UpdateTextsAsync(challengeId, request.Title, request.Description, ct) ? Results.NoContent() : Results.NotFound())
            .RequireTenantContext().RequireModule(ModuleCodes.M1Challenges).RequireRoles(Role.ProgrammeManager, Role.TenantAdmin)
            .WithName("UpdateChallengeTexts")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

        // Vorgangskennungen reserviert nur die Kiosk-Personensitzung (A-005, A-018); Handy-Beiträge tragen den Client-Schlüssel.
        group.MapPost("/{challengeId:guid}/contribution-operations", async (Guid challengeId, ITenantContextAccessor context, IChallengeCatalog catalog, IContributionService contributions, CancellationToken ct) =>
            {
                if (context.Require().Session != SessionKind.KioskPerson)
                {
                    return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Vorgangskennung nur am Kiosk", detail: "kiosk_person_session_required");
                }

                if (await catalog.GetAsync(challengeId, ct) is null)
                {
                    return Results.NotFound();
                }

                var operationId = await contributions.ReserveOperationAsync(ct);
                return Results.Created($"/api/challenges/{challengeId:D}/contribution-operations/{operationId}", new OperationResponse(operationId));
            })
            .RequireTenantContext().RequireModule(ModuleCodes.M1Challenges).AllowKiosk()
            .WithName("ReserveContributionOperation")
            .Produces<OperationResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/{challengeId:guid}/contributions", async (Guid challengeId, ContributionRequest request, ITenantContextAccessor context, IContributionService contributions, CancellationToken ct) =>
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

                // Kanal und Sitzungsart gehören zusammen (A-005, A-009): Kiosk-Beiträge nur aus der Kiosk-Personensitzung, Handy-Beiträge nie daraus.
                var isKioskSession = context.Require().Session == SessionKind.KioskPerson;
                if (isKioskSession != (channel == ContributionChannel.Kiosk))
                {
                    return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Kanal passt nicht zur Sitzung", detail: isKioskSession ? "kiosk_channel_required" : "kiosk_person_session_required");
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
            .RequireTenantContext().RequireModule(ModuleCodes.M1Challenges).AllowKiosk()
            .WithName("SubmitContribution")
            .Produces<ContributionResponse>(StatusCodes.Status201Created)
            .Produces<ContributionResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status410Gone)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .Produces(StatusCodes.Status404NotFound);

        // Eigene Beiträge und Korrektur (Challenges 3): nur auf dem eigenen Gerät, nie am Kiosk; bis zum Ende der Nachfrist.
        group.MapGet("/{challengeId:guid}/contributions/mine", async (Guid challengeId, IChallengeCatalog catalog, IContributionService contributions, CancellationToken ct) =>
            {
                if (await catalog.GetAsync(challengeId, ct) is null)
                {
                    return Results.NotFound();
                }

                var mine = await contributions.ListMineAsync(challengeId, ct);
                return Results.Ok(mine.Select(c => new OwnContributionResponse(c.Id.ToString("D"), ChallengeCards.Decimal(c.Value), c.RecordedAt, c.Channel == ContributionChannel.Kiosk ? "kiosk" : "mobile", c.Reversed, c.IsReversal)).ToList());
            })
            .RequireTenantContext().RequireModule(ModuleCodes.M1Challenges)
            .WithName("ListMyContributions")
            .Produces<List<OwnContributionResponse>>()
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/{challengeId:guid}/contributions/{contributionId:guid}/reverse", async (Guid challengeId, Guid contributionId, IChallengeCatalog catalog, IContributionService contributions, CancellationToken ct) =>
            {
                if (await catalog.GetAsync(challengeId, ct) is null)
                {
                    return Results.NotFound();
                }

                var result = await contributions.ReverseAsync(contributionId, ct);
                return result switch
                {
                    ReversalResult.Reversed => Results.Ok(new ReversalResponse("reversed")),
                    ReversalResult.AlreadyReversed => Results.Ok(new ReversalResponse("already_reversed")),
                    ReversalResult.GracePeriodOver => Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "Korrektur abgelehnt", detail: "GracePeriodOver"),
                    _ => Results.NotFound(),
                };
            })
            .RequireTenantContext().RequireModule(ModuleCodes.M1Challenges)
            .WithName("ReverseContribution")
            .Produces<ReversalResponse>()
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/{challengeId:guid}/collective", async (Guid challengeId, IChallengeCatalog catalog, CancellationToken ct) =>
            {
                var challenge = await catalog.GetAsync(challengeId, ct);
                if (challenge is null)
                {
                    return Results.NotFound();
                }

                var collective = await catalog.GetCollectiveAsync(challengeId, ct);
                return collective is null
                    ? Results.Ok(new CollectiveResponse(null, null, null, DateTimeOffset.MinValue, 0))
                    : Results.Ok(ChallengeCards.ToCollective(collective, (int)Math.Min(100m, Math.Max(0m, Math.Round(collective.Total / challenge.Target * 100m, 0, MidpointRounding.AwayFromZero)))));
            })
            .RequireTenantContext().RequireModule(ModuleCodes.M1Challenges).AllowKiosk(device: true)
            .WithName("GetChallengeCollective")
            .Produces<CollectiveResponse>()
            .Produces(StatusCodes.Status404NotFound);
    }

    private static ChallengeCardResponse ToCard(ChallengeCardRecord card) => ChallengeCards.ToCard(card);

    private static TBuilder HandleChallengeErrors<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder
    {
        builder.AddEndpointFilter(async (invocation, next) =>
        {
            try
            {
                return await next(invocation);
            }
            catch (ChallengeLifecycleException ex)
            {
                return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Lebenszyklus", detail: ex.Reason);
            }
            catch (ArgumentException ex)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { [ex.ParamName ?? "request"] = [ex.Message] });
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Forbid();
            }
        });
        return builder;
    }
}

/// <summary>Abbildung der Challenge-Karte für andere Module mit Kopfkarte (Feed 2.3), damit der Vertrag nur eine Kartenform kennt.</summary>
public static class ChallengeCards
{
    public static ChallengeCardResponse ToCard(ChallengeCardRecord card) => new(
        card.Challenge.Id.ToString("D"),
        card.Challenge.Title,
        card.Challenge.Description,
        card.Challenge.Metric == ChallengeMetric.Count ? ChallengeMetricDto.Count : ChallengeMetricDto.Checkmark,
        Decimal(card.Challenge.Target),
        ToDto(card.Challenge.State),
        ToDto(card.Challenge.Visibility),
        card.Challenge.StartsAt,
        card.Challenge.EndsAt,
        card.Collective is null ? null : ToCollective(card.Collective, card.Percent),
        card.Percent,
        card.ContributedToday,
        card.Challenge.Previewed,
        card.Challenge.TemplateKey);

    /// <summary>Mindestzahl (A-023, Challenges 6.2): unter fünf sichtbaren Beitragenden nur der gerundete Prozentwert, keine Einzelbeiträge und keine Zahlen.</summary>
    internal static CollectiveResponse ToCollective(CollectiveRecord collective, int percent) =>
        AggregateRule.MayPublish(collective.ContributorCount)
            ? new CollectiveResponse(Decimal(collective.Total), collective.ContributionCount, collective.ContributorCount, collective.UpdatedAt, percent)
            : new CollectiveResponse(null, null, null, collective.UpdatedAt, percent);

    internal static string Decimal(decimal value) => value.ToString("0.0000", CultureInfo.InvariantCulture);

    internal static ChallengeStateDto ToDto(ChallengeState state) => state switch
    {
        ChallengeState.Draft => ChallengeStateDto.Draft,
        ChallengeState.Planned => ChallengeStateDto.Planned,
        ChallengeState.Running => ChallengeStateDto.Running,
        ChallengeState.Grace => ChallengeStateDto.Grace,
        ChallengeState.Ended => ChallengeStateDto.Ended,
        _ => ChallengeStateDto.Archived,
    };

    internal static ChallengeVisibilityDto ToDto(ChallengeVisibility visibility) => visibility switch
    {
        ChallengeVisibility.OnlyMe => ChallengeVisibilityDto.OnlyMe,
        ChallengeVisibility.Team => ChallengeVisibilityDto.Team,
        _ => ChallengeVisibilityDto.Company,
    };

    internal static ChallengeMetric FromDto(ChallengeMetricDto metric) => metric == ChallengeMetricDto.Count ? ChallengeMetric.Count : ChallengeMetric.Checkmark;

    internal static ChallengeVisibility FromDto(ChallengeVisibilityDto visibility) => visibility switch
    {
        ChallengeVisibilityDto.OnlyMe => ChallengeVisibility.OnlyMe,
        ChallengeVisibilityDto.Team => ChallengeVisibility.Team,
        _ => ChallengeVisibility.Company,
    };

}
