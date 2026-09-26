using System.Globalization;
using System.Text.Json.Nodes;
using CompanyHero.Modules.Identity.Application;
using CompanyHero.Modules.Notifications.Domain;
using CompanyHero.Modules.Notifications.Infrastructure;
using CompanyHero.Modules.Organisation.Application;
using CompanyHero.Platform.Data;
using CompanyHero.Platform.Events;
using CompanyHero.Platform.Jobs;
using CompanyHero.Platform.Metering;
using CompanyHero.Platform.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CompanyHero.Modules.Notifications.Application;

/// <summary>Textschlüssel der Benachrichtigungen; Texte aus dem Katalog des Operators (Marke 6.2).</summary>
public static class NotificationTextKeys
{
    public const string ChallengeStarted = "benachrichtigung.challengeGestartet";
    public const string Milestone = "benachrichtigung.meilenstein";
    public const string ChallengeEnded = "benachrichtigung.challengeBeendet";
    public const string BadgeAwarded = "benachrichtigung.abzeichen";
    public const string LevelReached = "benachrichtigung.stufe";
}

public static class NotificationJobTypes
{
    public const string PushSend = "notifications.push.send";
    public const string EmailSend = "notifications.email.send";
    public const string Challenges = "notifications.pipeline.challenges";
    public const string Progress = "notifications.pipeline.progress";
    public const string MeteringModule = "Kern";
    public const string SentMetric = "notification.sent";
}

/// <summary>Ein zu verteilendes Ereignis nach der Deutung der Nutzdaten: Kategorie, Empfänger, Text, Ziel.</summary>
internal sealed record NotificationIntent(NotificationCategory Category, IReadOnlyList<PersonId> Recipients, string TextKey, IReadOnlyDictionary<string, string> Parameters, string Target);

/// <summary>
/// Die Zustellpipeline (Benachrichtigungen 7.1): je Empfänger ein In-App-Eintrag, je gewähltem Kanal eine Zustellung mit
/// Idempotenzschlüssel; Tenant- und Personenschalter, Kontingent, Ruhezeiten und Deduplikation werden hier angewandt. Der
/// Zustelljob entsteht in derselben Kontexttransaktion wie der Eintrag (A-006).
/// </summary>
internal sealed class NotificationPipeline(NotificationsDbContext db, IContextTransaction transaction, ITenantContextAccessor context, ITenantTimeZone timeZone, IJobQueue jobs, TimeProvider clock)
{
    public async Task<int> DistributeAsync(string eventKey, DateTimeOffset occurredAt, NotificationIntent intent, CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        var zone = await timeZone.GetAsync(cancellationToken);
        var now = clock.GetUtcNow();
        var day = TenantTimeZone.DayOf(now, zone);
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var tenant = await db.TenantSettings.SingleOrDefaultAsync(s => s.TenantId == tenantId, cancellationToken) ?? TenantNotificationSettings.Default(tenantId);
        var created = 0;
        foreach (var person in intent.Recipients)
        {
            // Kontingent je Person und Tag (Benachrichtigungen 4.2) muss auch unter parallelen Pipeline-Jobs halten: Sperre je
            // Tenant und Person bis zum Ende der Transaktion, damit Zählung und Anlage der Zustellung nicht verschränkt laufen.
            await db.Database.ExecuteSqlAsync($"select pg_advisory_xact_lock(hashtext({tenantId.Value.ToString("D")}), hashtext({person.Value.ToString("D")}))", cancellationToken);
            if (await db.Entries.AnyAsync(e => e.TenantId == tenantId && e.PersonId == person && e.EventKey == eventKey, cancellationToken))
            {
                continue;
            }

            var entry = NotificationEntry.Create(tenantId, person, intent.Category, eventKey, intent.TextKey, intent.Parameters, intent.Target, occurredAt);
            db.Entries.Add(entry);
            created++;

            var settings = await db.PersonSettings.AsNoTracking().SingleOrDefaultAsync(s => s.TenantId == tenantId && s.PersonId == person, cancellationToken) ?? PersonNotificationSettings.Default(tenantId, person);
            var personQuiet = settings.QuietStart is { } qs && settings.QuietEnd is { } qe ? (qs, qe) : ((TimeOnly, TimeOnly)?)null;
            var heldUntil = NotificationRules.HoldUntil(now, zone, (tenant.QuietStart, tenant.QuietEnd), personQuiet);

            if (tenant.PushEnabled && settings.PushFor(intent.Category))
            {
                var hasSubscription = await db.Subscriptions.AnyAsync(s => s.TenantId == tenantId && s.PersonId == person && s.PausedAt == null, cancellationToken);
                var pushesToday = await db.Deliveries.CountAsync(d => d.TenantId == tenantId && d.PersonId == person && d.Channel == NotificationChannel.Push && d.Day == day && d.Status != DeliveryStatus.Dropped, cancellationToken);
                if (hasSubscription && pushesToday < NotificationRules.MaxPushPerPersonAndDay)
                {
                    await QueueAsync(tenantId, entry, NotificationChannel.Push, NotificationJobTypes.PushSend, now, day, heldUntil, cancellationToken);
                }
            }

            if (tenant.EmailEnabled && settings.EmailFor(intent.Category) && settings.EmailPausedAt is null)
            {
                await QueueAsync(tenantId, entry, NotificationChannel.Email, NotificationJobTypes.EmailSend, now, day, heldUntil, cancellationToken);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return created;
    }

    private async Task QueueAsync(TenantId tenantId, NotificationEntry entry, NotificationChannel channel, string jobType, DateTimeOffset now, DateOnly day, DateTimeOffset? heldUntil, CancellationToken cancellationToken)
    {
        var delivery = Delivery.Create(tenantId, entry, channel, now, day, heldUntil);
        if (await db.Deliveries.AnyAsync(d => d.TenantId == tenantId && d.DedupeKey == delivery.DedupeKey, cancellationToken))
        {
            return;
        }

        db.Deliveries.Add(delivery);
        await db.SaveChangesAsync(cancellationToken);
        await jobs.EnqueueAsync(new JobRequest(jobType, delivery.Id.ToString("D"), delivery.DedupeKey, heldUntil), cancellationToken);
    }
}

/// <summary>Deutung der Fachereignisse in Absichten (Benachrichtigungen 4.1): Challenge an alle aktiven Mitglieder, Fortschritt an die Person.</summary>
internal static class EventInterpretation
{
    public static NotificationIntent? Interpret(DomainEventRecord record, IReadOnlyList<PersonId> members)
    {
        var payload = JsonNode.Parse(record.PayloadJson)?.AsObject() ?? throw new InvalidOperationException("Leere Nutzdaten.");
        return record.Type switch
        {
            "challenges.started" => new NotificationIntent(NotificationCategory.Challenge, members, NotificationTextKeys.ChallengeStarted, Params(("titel", Text(payload, "title"))), ChallengeTarget(payload)),
            "challenges.milestone.reached" => new NotificationIntent(NotificationCategory.Challenge, members, NotificationTextKeys.Milestone, Params(("titel", Text(payload, "title")), ("prozent", Text(payload, "percent"))), ChallengeTarget(payload)),
            "challenges.ended" => new NotificationIntent(NotificationCategory.Challenge, members, NotificationTextKeys.ChallengeEnded, Params(("titel", Text(payload, "title")), ("prozent", Text(payload, "percent"))), ChallengeTarget(payload)),
            "progress.badge.awarded" => Person(payload) is { } badgePerson ? new NotificationIntent(NotificationCategory.Progress, [badgePerson], NotificationTextKeys.BadgeAwarded, Params(("abzeichen", Text(payload, "textKey"))), "/ich") : null,
            "progress.level.reached" => Person(payload) is { } levelPerson ? new NotificationIntent(NotificationCategory.Progress, [levelPerson], NotificationTextKeys.LevelReached, Params(("stufe", Text(payload, "level"))), "/ich") : null,
            _ => null,
        };
    }

    private static string ChallengeTarget(JsonObject payload) => "/challenges/" + Text(payload, "challengeId");

    private static Dictionary<string, string> Params(params (string Key, string Value)[] values)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in values)
        {
            result[key] = value;
        }

        return result;
    }

    private static string Text(JsonObject payload, string name) => payload[name] switch
    {
        null => string.Empty,
        JsonValue v when v.TryGetValue<string>(out var s) => s,
        JsonValue v when v.TryGetValue<int>(out var i) => i.ToString(CultureInfo.InvariantCulture),
        var other => other.ToJsonString(),
    };

    private static PersonId? Person(JsonObject payload) => Guid.TryParse(Text(payload, "personId"), out var id) ? new PersonId(id) : null;
}

/// <summary>Abonnent der Challenge-Ereignisse; liest über die Ereignisquelle, nie über Tabellen von Challenges (Domänenkarte 6).</summary>
internal sealed class ChallengeNotificationHandler(IDomainEventReader reader, IOrganisationDirectory organisations, NotificationPipeline pipeline, ChallengeSnapshotProjector snapshots) : IJobHandler
{
    public static string JobType => NotificationJobTypes.Challenges;

    public async Task HandleAsync(JobExecution job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        var record = await reader.ReadAsync("challenges.", Guid.ParseExact(job.Reference, "D"), cancellationToken)
            ?? throw new InvalidOperationException("Ereignis im Kontext des Tenants nicht lesbar.");
        await snapshots.ApplyAsync(record, cancellationToken);
        var intent = EventInterpretation.Interpret(record, await organisations.ListActiveMemberIdsAsync(cancellationToken));
        if (intent is not null)
        {
            await pipeline.DistributeAsync($"{record.Type}:{record.Id:D}", record.OccurredAt, intent, cancellationToken);
        }
    }
}

internal sealed class ProgressNotificationHandler(IDomainEventReader reader, NotificationPipeline pipeline) : IJobHandler
{
    public static string JobType => NotificationJobTypes.Progress;

    public async Task HandleAsync(JobExecution job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        var record = await reader.ReadAsync("progress.", Guid.ParseExact(job.Reference, "D"), cancellationToken)
            ?? throw new InvalidOperationException("Ereignis im Kontext des Tenants nicht lesbar.");
        var intent = EventInterpretation.Interpret(record, []);
        if (intent is not null)
        {
            await pipeline.DistributeAsync($"{record.Type}:{record.Id:D}", record.OccurredAt, intent, cancellationToken);
        }
    }
}

/// <summary>Kanaljob Push (Benachrichtigungen 3.1, 7.1 Nr. 4): sendet an alle aktiven Abonnements der Person; Antworten werden als Status gespeichert.</summary>
internal sealed class PushSendHandler(NotificationsDbContext db, IContextTransaction transaction, ITenantContextAccessor context, IVapidKeys vapid, IWebPushTransport transport, IMeteringEmitter metering, TimeProvider clock) : IJobHandler
{
    public static string JobType => NotificationJobTypes.PushSend;

    public async Task HandleAsync(JobExecution job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        var tenantId = context.Require().RequireTenant();
        var now = clock.GetUtcNow();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var delivery = await db.Deliveries.SingleOrDefaultAsync(d => d.TenantId == tenantId && d.Id == Guid.ParseExact(job.Reference, "D"), cancellationToken);
        if (delivery is null || delivery.Status is not (DeliveryStatus.Queued or DeliveryStatus.Held))
        {
            await tx.CommitAsync(cancellationToken);
            return;
        }

        var tenant = await db.TenantSettings.AsNoTracking().SingleOrDefaultAsync(s => s.TenantId == tenantId, cancellationToken);
        var entry = await db.Entries.AsNoTracking().SingleOrDefaultAsync(e => e.TenantId == tenantId && e.Id == delivery.EntryId, cancellationToken);
        var subscriptions = await db.Subscriptions.Where(s => s.TenantId == tenantId && s.PersonId == delivery.PersonId && s.PausedAt == null).ToListAsync(cancellationToken);
        if (entry is null || !vapid.Configured || (tenant is not null && !tenant.PushEnabled) || subscriptions.Count == 0)
        {
            delivery.Finish(DeliveryStatus.Dropped, now, entry is null ? "entry_missing" : !vapid.Configured ? "vapid_missing" : subscriptions.Count == 0 ? "no_subscription" : "tenant_paused");
            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return;
        }

        var payload = new PushPayload(entry.Id.ToString("D"), PushPayload.CategoryName(entry.Category), entry.Target).ToBytes();
        var sent = 0;
        string? lastError = null;
        foreach (var subscription in subscriptions)
        {
            var endpoint = new Uri(subscription.Endpoint, UriKind.Absolute);
            var body = WebPushCrypto.Encrypt(payload, subscription.P256dh, subscription.Auth);
            var result = await transport.SendAsync(new PushMessage(endpoint, body, vapid.Authorization(endpoint, now), (int)NotificationRules.PushTimeToLive.TotalSeconds, NotificationRules.Topic(entry.Target), "normal"), cancellationToken);
            if (PushResponses.Accepted(result.StatusCode))
            {
                subscription.RecordSuccess(now);
                sent++;
            }
            else if (PushResponses.SubscriptionGone(result.StatusCode))
            {
                db.Subscriptions.Remove(subscription);
                lastError = "push:" + result.StatusCode.ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                subscription.RecordFailure(now);
                lastError = "push:" + result.StatusCode.ToString(CultureInfo.InvariantCulture);
            }
        }

        delivery.Finish(sent > 0 ? DeliveryStatus.Sent : DeliveryStatus.Failed, now, sent > 0 ? null : lastError);
        await db.SaveChangesAsync(cancellationToken);
        if (sent > 0)
        {
            // Metering ohne Personenbezug in derselben Kontexttransaktion (Benachrichtigungen 9, Metering).
            await metering.EmitAsync(new MeteringEmission(NotificationJobTypes.MeteringModule, NotificationJobTypes.SentMetric, $"push:{PushPayload.CategoryName(entry.Category)}", 1m, MeteringSource.Automatic, now, "ns:" + delivery.Id.ToString("D")), cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);
    }
}

/// <summary>Kanaljob E-Mail (Benachrichtigungen 6.3): Adresse erst beim Versand aus Identity gelesen, nie gespeichert; Abmeldelink je Kategorie; kein Tracking.</summary>
internal sealed class EmailSendHandler(NotificationsDbContext db, IContextTransaction transaction, ITenantContextAccessor context, IPersonDirectory persons, IMailTransport transport, MailTexts texts, IUnsubscribeTokens tokens, IOptions<NotificationsOptions> options, IOptions<IdentityOptions> identity, IMeteringEmitter metering, TimeProvider clock) : IJobHandler
{
    public static string JobType => NotificationJobTypes.EmailSend;

    public async Task HandleAsync(JobExecution job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        var tenantId = context.Require().RequireTenant();
        var now = clock.GetUtcNow();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var delivery = await db.Deliveries.SingleOrDefaultAsync(d => d.TenantId == tenantId && d.Id == Guid.ParseExact(job.Reference, "D"), cancellationToken);
        if (delivery is null || delivery.Status is not (DeliveryStatus.Queued or DeliveryStatus.Held))
        {
            await tx.CommitAsync(cancellationToken);
            return;
        }

        var tenant = await db.TenantSettings.AsNoTracking().SingleOrDefaultAsync(s => s.TenantId == tenantId, cancellationToken);
        var entry = await db.Entries.AsNoTracking().SingleOrDefaultAsync(e => e.TenantId == tenantId && e.Id == delivery.EntryId, cancellationToken);
        var settings = await db.PersonSettings.SingleOrDefaultAsync(s => s.TenantId == tenantId && s.PersonId == delivery.PersonId, cancellationToken);
        var email = entry is null ? null : await persons.GetEmailAsync(delivery.PersonId, cancellationToken);
        if (entry is null || email is null || (tenant is not null && !tenant.EmailEnabled) || settings?.EmailPausedAt is not null || !transport.Configured)
        {
            delivery.Finish(DeliveryStatus.Dropped, now, entry is null ? "entry_missing" : email is null ? "no_address" : !transport.Configured ? "smtp_missing" : "paused");
            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return;
        }

        var t = await texts.ResolveAsync(cancellationToken);
        var origin = (string.IsNullOrWhiteSpace(options.Value.PublicOrigin) ? identity.Value.PublicOrigin : options.Value.PublicOrigin).TrimEnd('/');
        var token = Uri.EscapeDataString(tokens.Protect(new UnsubscribeToken(tenantId, delivery.PersonId, entry.Category)));
        var unsubscribe = origin + "/abmelden?token=" + token;
        var oneClick = origin + "/api/notifications/unsubscribe?token=" + token;
        var name = (await persons.GetAsync(delivery.PersonId, cancellationToken))?.DisplayName;
        var parameters = entry.Parameters().Select(p => (p.Key, p.Value)).ToArray();
        var body = (name is null ? t.Text("mail.anredeOhneName") : t.Text("mail.anrede", ("name", name))) + "\n\n"
            + t.Text(entry.TextKey, parameters) + "\n\n"
            + t.Text("mail.oeffnen", ("link", origin + entry.Target)) + "\n\n"
            + t.Text("mail.einstellungen", ("link", origin + "/ich/benachrichtigungen")) + "\n"
            + t.Text("mail.abmelden", ("link", unsubscribe));
        try
        {
            await transport.SendAsync(new MailEnvelope(email, t.Text("mail.betreff", ("produktname", t.Produktname)), body, oneClick), cancellationToken);
            settings?.RecordEmailSuccess();
            delivery.Finish(DeliveryStatus.Sent, now);
            await metering.EmitAsync(new MeteringEmission(NotificationJobTypes.MeteringModule, NotificationJobTypes.SentMetric, $"email:{PushPayload.CategoryName(entry.Category)}", 1m, MeteringSource.Automatic, now, "ns:" + delivery.Id.ToString("D")), cancellationToken);
        }
        catch (System.Net.Mail.SmtpException ex)
        {
            settings?.RecordEmailFailure(now);
            delivery.Finish(DeliveryStatus.Failed, now, "smtp:" + ex.StatusCode);
        }

        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }
}
