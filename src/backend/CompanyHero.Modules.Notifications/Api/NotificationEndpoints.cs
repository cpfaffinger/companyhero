using System.Text.Json.Serialization;
using CompanyHero.Modules.Notifications.Application;
using CompanyHero.Modules.Notifications.Domain;
using CompanyHero.Modules.Organisation.Domain;
using CompanyHero.Platform.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CompanyHero.Modules.Notifications.Api;

[JsonConverter(typeof(JsonStringEnumConverter<NotificationCategoryDto>))]
public enum NotificationCategoryDto
{
    [JsonStringEnumMemberName("challenge")] Challenge,
    [JsonStringEnumMemberName("progress")] Progress,
}

/// <summary>Öffentlicher VAPID-Schlüssel für <c>applicationServerKey</c> (Benachrichtigungen 3.1); <c>available=false</c>, wenn Push nicht konfiguriert ist.</summary>
public sealed record VapidResponse(bool Available, string? PublicKey);

/// <summary>Tenant-Schalter (Benachrichtigungen 2.1); Ruhezeit als <c>HH:mm</c> in der Tenant-Zeitzone.</summary>
public sealed record TenantNotificationSettingsResponse(bool PushEnabled, bool EmailEnabled, bool AushangEnabled, string QuietStart, string QuietEnd);

public sealed record TenantNotificationSettingsRequest(bool PushEnabled, bool EmailEnabled, bool AushangEnabled, string QuietStart, string QuietEnd);

/// <summary>Persönliche Einstellungen (Benachrichtigungen 6.1) samt Tenant-Zustand („Vom Unternehmen derzeit pausiert“).</summary>
public sealed record PersonNotificationSettingsResponse(bool PushChallenge, bool PushProgress, bool EmailChallenge, bool EmailProgress, string? QuietStart, string? QuietEnd, bool EmailPaused, TenantNotificationSettingsResponse Tenant);

public sealed record PersonNotificationSettingsRequest(bool PushChallenge, bool PushProgress, bool EmailChallenge, bool EmailProgress, string? QuietStart, string? QuietEnd);

public sealed record PushSubscriptionRequest(string Endpoint, string P256dh, string Auth, string? DeviceLabel);

public sealed record PushSubscriptionResponse(string Id, string DeviceLabel, DateTimeOffset CreatedAt, DateTimeOffset? LastSuccessAt, bool Paused);

/// <summary>Eintrag des Benachrichtigungszentrums (Benachrichtigungen 7.2): Textschlüssel und Platzhalter aus dem Katalog; Ziel als Pfad in der App.</summary>
public sealed record NotificationResponse(string Id, NotificationCategoryDto Category, string TextKey, IReadOnlyDictionary<string, string> Params, string Target, DateTimeOffset OccurredAt, DateTimeOffset? ReadAt);

public sealed record AushangResponse(string Id, string Week, DateTimeOffset GeneratedAt);

/// <summary>Ergebnis der Ein-Klick-Abmeldung: die Kategorie, für die keine E-Mails mehr gesendet werden.</summary>
public sealed record UnsubscribeResponse(NotificationCategoryDto Category);

internal static class NotificationEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/notifications/vapid", (IVapidKeys vapid) => Results.Ok(new VapidResponse(vapid.Configured, vapid.Configured ? vapid.PublicKey : null)))
            .RequireTenantContext()
            .WithName("GetVapidKey")
            .Produces<VapidResponse>();

        endpoints.MapGet("/api/notifications", async (bool? unreadOnly, INotificationSettings settings, CancellationToken ct) =>
            {
                var entries = await settings.ListEntriesAsync(unreadOnly ?? false, ct);
                return Results.Ok(entries.Select(ToResponse).ToList());
            })
            .RequireTenantContext()
            .WithName("ListNotifications")
            .Produces<List<NotificationResponse>>();

        endpoints.MapPost("/api/notifications/{entryId:guid}/read", async (Guid entryId, INotificationSettings settings, CancellationToken ct) =>
                await settings.MarkReadAsync(entryId, ct) ? Results.NoContent() : Results.NotFound())
            .RequireTenantContext()
            .WithName("MarkNotificationRead")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        endpoints.MapPost("/api/notifications/read-all", async (INotificationSettings settings, CancellationToken ct) =>
            {
                await settings.MarkAllReadAsync(ct);
                return Results.NoContent();
            })
            .RequireTenantContext()
            .WithName("MarkAllNotificationsRead")
            .Produces(StatusCodes.Status204NoContent);

        endpoints.MapGet("/api/me/notifications", async (INotificationSettings settings, CancellationToken ct) => Results.Ok(ToResponse(await settings.GetMineAsync(ct))))
            .RequireTenantContext()
            .WithName("GetMyNotificationSettings")
            .Produces<PersonNotificationSettingsResponse>();

        endpoints.MapPut("/api/me/notifications", async (PersonNotificationSettingsRequest request, INotificationSettings settings, CancellationToken ct) =>
            {
                TimeOnly? start = null, end = null;
                if (request.QuietStart is not null || request.QuietEnd is not null)
                {
                    if (!TimeFormat.TryParse(request.QuietStart, out var s) || !TimeFormat.TryParse(request.QuietEnd, out var e))
                    {
                        return Results.ValidationProblem(new Dictionary<string, string[]> { ["quietStart"] = ["Ruhezeit als HH:mm mit Beginn und Ende."] });
                    }

                    start = s;
                    end = e;
                }

                return Results.Ok(ToResponse(await settings.UpdateMineAsync(request.PushChallenge, request.PushProgress, request.EmailChallenge, request.EmailProgress, start, end, ct)));
            })
            .RequireTenantContext()
            .WithName("UpdateMyNotificationSettings")
            .Produces<PersonNotificationSettingsResponse>()
            .ProducesValidationProblem();

        endpoints.MapGet("/api/me/notifications/subscriptions", async (INotificationSettings settings, CancellationToken ct) =>
                Results.Ok((await settings.ListSubscriptionsAsync(ct)).Select(ToResponse).ToList()))
            .RequireTenantContext()
            .WithName("ListPushSubscriptions")
            .Produces<List<PushSubscriptionResponse>>();

        // Nie aus Kiosk-Sitzungen (Benachrichtigungen 3.2): der Endpunkt erlaubt keinen Kiosk, der Dienst prüft zusätzlich.
        endpoints.MapPost("/api/me/notifications/subscriptions", async (PushSubscriptionRequest request, INotificationSettings settings, CancellationToken ct) =>
            {
                try
                {
                    var record = await settings.SubscribeAsync(request.Endpoint, request.P256dh, request.Auth, request.DeviceLabel, ct);
                    return Results.Created($"/api/me/notifications/subscriptions/{record.Id:D}", ToResponse(record));
                }
                catch (ArgumentException ex)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]> { [ex.ParamName ?? "request"] = [ex.Message] });
                }
                catch (UnauthorizedAccessException)
                {
                    return Results.Forbid();
                }
            })
            .RequireTenantContext()
            .WithName("CreatePushSubscription")
            .Produces<PushSubscriptionResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        endpoints.MapDelete("/api/me/notifications/subscriptions/{subscriptionId:guid}", async (Guid subscriptionId, INotificationSettings settings, CancellationToken ct) =>
                await settings.UnsubscribeAsync(subscriptionId, ct) ? Results.NoContent() : Results.NotFound())
            .RequireTenantContext()
            .WithName("DeletePushSubscription")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        endpoints.MapGet("/api/notifications/tenant", async (INotificationSettings settings, CancellationToken ct) => Results.Ok(ToResponse(await settings.GetTenantAsync(ct))))
            .RequireTenantContext().RequireRoles(Role.TenantAdmin, Role.ProgrammeManager, Role.Insight)
            .WithName("GetTenantNotificationSettings")
            .Produces<TenantNotificationSettingsResponse>();

        endpoints.MapPut("/api/notifications/tenant", async (TenantNotificationSettingsRequest request, INotificationSettings settings, CancellationToken ct) =>
            {
                if (!TimeFormat.TryParse(request.QuietStart, out var start) || !TimeFormat.TryParse(request.QuietEnd, out var end))
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["quietStart"] = ["Ruhezeit als HH:mm."] });
                }

                return Results.Ok(ToResponse(await settings.UpdateTenantAsync(request.PushEnabled, request.EmailEnabled, request.AushangEnabled, start, end, ct)));
            })
            .RequireTenantContext().RequireRoles(Role.TenantAdmin, Role.ProgrammeManager)
            .WithName("UpdateTenantNotificationSettings")
            .Produces<TenantNotificationSettingsResponse>()
            .ProducesValidationProblem();

        // Ein-Klick-Abmeldung aus der E-Mail (Benachrichtigungen 6.3, 8): ohne Sitzung, signiertes Token, nur diese eine Änderung.
        // Der Link in der E-Mail führt auf die Seite /abmelden der App, die diesen Endpunkt aufruft; List-Unsubscribe-Post zeigt direkt hierher (RFC 8058).
        endpoints.MapPost("/api/notifications/unsubscribe", async (string? token, IUnsubscribeService unsubscribe, CancellationToken ct) =>
            {
                var category = string.IsNullOrWhiteSpace(token) ? null : await unsubscribe.UnsubscribeAsync(token, ct);
                return category is null
                    ? Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Abmeldelink ungültig oder abgelaufen", detail: "unsubscribe_token_invalid")
                    : Results.Ok(new UnsubscribeResponse(category == NotificationCategory.Challenge ? NotificationCategoryDto.Challenge : NotificationCategoryDto.Progress));
            })
            .WithName("UnsubscribeByToken")
            .Produces<UnsubscribeResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        endpoints.MapPost("/api/notifications/aushang", async (IAushangService aushang, CancellationToken ct) =>
            {
                var record = await aushang.RenderAsync(ct);
                return Results.Created("/api/notifications/aushang", new AushangResponse(record.Id.ToString("D"), record.Week, record.GeneratedAt));
            })
            .RequireTenantContext().RequireRoles(Role.ProgrammeManager, Role.TenantAdmin, Role.HealthAmbassador)
            .WithName("RenderAushang")
            .Produces<AushangResponse>(StatusCodes.Status201Created);

        endpoints.MapGet("/api/notifications/aushang", async (IAushangService aushang, CancellationToken ct) =>
            {
                var latest = await aushang.GetLatestAsync(ct);
                return latest is null ? Results.NotFound() : Results.File(latest.Value.Pdf, "application/pdf", $"aushang-{latest.Value.Record.Week}.pdf");
            })
            .RequireTenantContext().RequireRoles(Role.ProgrammeManager, Role.TenantAdmin, Role.HealthAmbassador)
            .WithName("GetAushang")
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf")
            .Produces(StatusCodes.Status404NotFound);
    }

    private static NotificationResponse ToResponse(EntryRecord e) =>
        new(e.Id.ToString("D"), e.Category == NotificationCategory.Challenge ? NotificationCategoryDto.Challenge : NotificationCategoryDto.Progress, e.TextKey, e.Parameters, e.Target, e.OccurredAt, e.ReadAt);

    private static PersonNotificationSettingsResponse ToResponse(PersonSettingsRecord s) =>
        new(s.PushChallenge, s.PushProgress, s.EmailChallenge, s.EmailProgress, s.QuietStart is { } qs ? TimeFormat.Hm(qs) : null, s.QuietEnd is { } qe ? TimeFormat.Hm(qe) : null, s.EmailPaused, ToResponse(s.Tenant));

    private static TenantNotificationSettingsResponse ToResponse(TenantSettingsRecord t) =>
        new(t.PushEnabled, t.EmailEnabled, t.AushangEnabled, TimeFormat.Hm(t.QuietStart), TimeFormat.Hm(t.QuietEnd));

    private static PushSubscriptionResponse ToResponse(SubscriptionRecord s) => new(s.Id.ToString("D"), s.DeviceLabel, s.CreatedAt, s.LastSuccessAt, s.Paused);
}
