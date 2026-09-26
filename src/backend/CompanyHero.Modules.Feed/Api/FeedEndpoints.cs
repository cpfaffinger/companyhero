using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CompanyHero.Modules.Challenges.Api;
using CompanyHero.Modules.Feed.Application;
using CompanyHero.Modules.Feed.Domain;
using CompanyHero.Platform.Hosting;
using CompanyHero.Platform.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Net.Http.Headers;

namespace CompanyHero.Modules.Feed.Api;

/// <summary>Kopfkarte (Feed 2.3): <c>kind</c> ist <c>challenge</c> mit Karte oder <c>daily_goal</c> mit Tagesziel und heutigen Handlungen.</summary>
public sealed record HeadCardResponse(string Kind, ChallengeCardResponse? Challenge, int DailyGoal, int TodayActions);

public sealed record FeedReferenceResponse(string Kind, string? Id);

/// <summary>Person auf einer Karte: nur, wenn ihre Sichtbarkeitsstufe es für die lesende Person erlaubt (A-022).</summary>
public sealed record FeedPersonResponse(string PersonId, string DisplayName);

/// <summary>Karte des Streams (Feed 2.2): Textschlüssel und Platzhalter aus dem Katalog des Operators; <c>kind</c> ist <c>system</c>, <c>aggregate</c> oder <c>member_post</c>.</summary>
public sealed record FeedCardResponse(string Id, string Kind, DateTimeOffset OccurredAt, string Day, string TextKey, IReadOnlyDictionary<string, string> Params, FeedPersonResponse? Person, FeedReferenceResponse? Reference, string? Body, bool Mine);

public sealed record FeedResponse(HeadCardResponse Head, bool CheckInDue, IReadOnlyList<FeedCardResponse> Cards);

public sealed record FeedPostRequest(string Body);

public sealed record FeedPostResponse(string Id);

internal static class FeedEndpoints
{
    private static readonly JsonSerializerOptions EtagJson = new(JsonSerializerDefaults.Web);

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        // Der Feed gehört zur angemeldeten Person auf dem eigenen Gerät; Kiosk-Sitzungen lesen und schreiben ihn nicht (Feed 2, A-005).
        endpoints.MapGet("/api/feed", async (HttpContext http, IFeedService feed, CancellationToken ct) =>
            {
                var page = await feed.ReadAsync(ct);
                var response = ToResponse(page);
                var etag = ComputeEtag(response);
                if (http.Request.Headers.IfNoneMatch.Any(v => string.Equals(v, etag, StringComparison.Ordinal)))
                {
                    http.Response.Headers[HeaderNames.ETag] = etag;
                    return Results.StatusCode(StatusCodes.Status304NotModified);
                }

                http.Response.Headers[HeaderNames.ETag] = etag;
                http.Response.Headers[HeaderNames.CacheControl] = "private, no-cache";
                return Results.Ok(response);
            })
            .RequireTenantContext()
            .WithName("GetFeed")
            .Produces<FeedResponse>()
            .Produces(StatusCodes.Status304NotModified);

        endpoints.MapPost("/api/feed/posts", async (FeedPostRequest request, IFeedService feed, CancellationToken ct) =>
            {
                PostResult result;
                try
                {
                    result = await feed.PostAsync(request.Body, ct);
                }
                catch (ArgumentException ex)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["body"] = [ex.Message] });
                }

                return result.Outcome switch
                {
                    PostOutcome.Created => Results.Created($"/api/feed/posts/{result.EntryId:D}", new FeedPostResponse(result.EntryId!.Value.ToString("D"))),
                    PostOutcome.VisibilityOnlyMe => Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "Mit „Nur für mich“ ist kein Beitrag möglich", detail: "visibility_only_me"),
                    _ => Results.Problem(statusCode: StatusCodes.Status429TooManyRequests, title: "Höchstens fünf Beiträge je Tag", detail: "daily_post_limit"),
                };
            })
            .RequireTenantContext()
            .WithName("CreateFeedPost")
            .Produces<FeedPostResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        endpoints.MapDelete("/api/feed/posts/{entryId:guid}", async (Guid entryId, IFeedService feed, CancellationToken ct) =>
                await feed.DeletePostAsync(entryId, ct) ? Results.NoContent() : Results.NotFound())
            .RequireTenantContext()
            .WithName("DeleteFeedPost")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);
    }

    private static FeedResponse ToResponse(FeedPageRecord page)
    {
        var head = page.Head.Challenge is null
            ? new HeadCardResponse("daily_goal", null, page.Head.DailyGoal, page.Head.TodayActions)
            : new HeadCardResponse("challenge", ChallengeCards.ToCard(page.Head.Challenge), page.Head.DailyGoal, page.Head.TodayActions);
        var cards = page.Cards.Select(c => new FeedCardResponse(
            c.Id.ToString("D"),
            c.Kind,
            c.OccurredAt,
            c.Day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            c.TextKey,
            c.Parameters,
            c.Subject is { } subject && page.DisplayNames.TryGetValue(subject, out var name) ? new FeedPersonResponse(subject.Value.ToString("D"), name) : null,
            c.ReferenceKind is null ? null : new FeedReferenceResponse(c.ReferenceKind, c.ReferenceId?.ToString("D")),
            c.Body,
            c.Mine)).ToList();
        return new FeedResponse(head, page.CheckInDue, cards);
    }

    /// <summary>ETag über die Nutzdaten (Feed 2.6): unverändert bedeutet 304 ohne Nutzdaten; neue Einträge oder ein neuer Kollektivstand ändern ihn.</summary>
    private static string ComputeEtag(FeedResponse response)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(response, EtagJson);
        var hash = SHA256.HashData(bytes);
        return "\"" + Convert.ToHexStringLower(hash.AsSpan(0, 16)) + "\"";
    }
}
