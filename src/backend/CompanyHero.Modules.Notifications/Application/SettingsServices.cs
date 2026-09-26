using CompanyHero.Modules.Notifications.Domain;
using CompanyHero.Modules.Notifications.Infrastructure;
using CompanyHero.Platform.Audit;
using CompanyHero.Platform.Data;
using CompanyHero.Platform.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyHero.Modules.Notifications.Application;

public sealed record PersonSettingsRecord(bool PushChallenge, bool PushProgress, bool EmailChallenge, bool EmailProgress, TimeOnly? QuietStart, TimeOnly? QuietEnd, bool EmailPaused, TenantSettingsRecord Tenant);

public sealed record TenantSettingsRecord(bool PushEnabled, bool EmailEnabled, bool AushangEnabled, TimeOnly QuietStart, TimeOnly QuietEnd);

public sealed record SubscriptionRecord(Guid Id, string DeviceLabel, DateTimeOffset CreatedAt, DateTimeOffset? LastSuccessAt, bool Paused);

public sealed record EntryRecord(Guid Id, NotificationCategory Category, string TextKey, IReadOnlyDictionary<string, string> Parameters, string Target, DateTimeOffset OccurredAt, DateTimeOffset? ReadAt);

/// <summary>Persönliche Einstellungen, Geräte und Benachrichtigungszentrum der angemeldeten Person (Benachrichtigungen 6.1, 7.2).</summary>
public interface INotificationSettings
{
    Task<PersonSettingsRecord> GetMineAsync(CancellationToken cancellationToken);

    Task<PersonSettingsRecord> UpdateMineAsync(bool pushChallenge, bool pushProgress, bool emailChallenge, bool emailProgress, TimeOnly? quietStart, TimeOnly? quietEnd, CancellationToken cancellationToken);

    Task<IReadOnlyList<SubscriptionRecord>> ListSubscriptionsAsync(CancellationToken cancellationToken);

    /// <summary>Abonnement je Gerät; derselbe Endpunkt erneuert das bestehende (pushsubscriptionchange). Nie aus Kiosk-Sitzungen.</summary>
    Task<SubscriptionRecord> SubscribeAsync(string endpoint, string p256dh, string auth, string? deviceLabel, CancellationToken cancellationToken);

    Task<bool> UnsubscribeAsync(Guid subscriptionId, CancellationToken cancellationToken);

    Task<IReadOnlyList<EntryRecord>> ListEntriesAsync(bool unreadOnly, CancellationToken cancellationToken);

    Task<bool> MarkReadAsync(Guid entryId, CancellationToken cancellationToken);

    Task<int> MarkAllReadAsync(CancellationToken cancellationToken);

    /// <summary>Tenant-Schalter (Benachrichtigungen 2.1): Tenant-Admin oder Programm-Manager; im Prüfprotokoll.</summary>
    Task<TenantSettingsRecord> GetTenantAsync(CancellationToken cancellationToken);

    Task<TenantSettingsRecord> UpdateTenantAsync(bool pushEnabled, bool emailEnabled, bool aushangEnabled, TimeOnly quietStart, TimeOnly quietEnd, CancellationToken cancellationToken);
}

/// <summary>Ein-Klick-Abmeldung aus einer E-Mail (Benachrichtigungen 6.3): ohne Sitzung, nur mit signiertem Token; wirkt sofort.</summary>
public interface IUnsubscribeService
{
    Task<NotificationCategory?> UnsubscribeAsync(string token, CancellationToken cancellationToken);
}

internal sealed class NotificationSettings(NotificationsDbContext db, IContextTransaction transaction, ITenantContextAccessor context, IAuditLog audit, TimeProvider clock) : INotificationSettings
{
    public async Task<PersonSettingsRecord> GetMineAsync(CancellationToken cancellationToken)
    {
        var (tenantId, person) = Require();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var settings = await db.PersonSettings.AsNoTracking().SingleOrDefaultAsync(s => s.TenantId == tenantId && s.PersonId == person, cancellationToken) ?? PersonNotificationSettings.Default(tenantId, person);
        var tenant = await LoadTenantAsync(tenantId, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return ToRecord(settings, tenant);
    }

    public async Task<PersonSettingsRecord> UpdateMineAsync(bool pushChallenge, bool pushProgress, bool emailChallenge, bool emailProgress, TimeOnly? quietStart, TimeOnly? quietEnd, CancellationToken cancellationToken)
    {
        var (tenantId, person) = Require();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var settings = await db.PersonSettings.SingleOrDefaultAsync(s => s.TenantId == tenantId && s.PersonId == person, cancellationToken);
        if (settings is null)
        {
            settings = PersonNotificationSettings.Default(tenantId, person);
            db.PersonSettings.Add(settings);
        }

        settings.Update(pushChallenge, pushProgress, emailChallenge, emailProgress, quietStart, quietEnd, clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        var tenant = await LoadTenantAsync(tenantId, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return ToRecord(settings, tenant);
    }

    public async Task<IReadOnlyList<SubscriptionRecord>> ListSubscriptionsAsync(CancellationToken cancellationToken)
    {
        var (tenantId, person) = Require();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var subscriptions = await db.Subscriptions.AsNoTracking().Where(s => s.TenantId == tenantId && s.PersonId == person).OrderBy(s => s.CreatedAt).ToListAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return subscriptions.Select(ToRecord).ToList();
    }

    public async Task<SubscriptionRecord> SubscribeAsync(string endpoint, string p256dh, string auth, string? deviceLabel, CancellationToken cancellationToken)
    {
        var current = context.Require();
        if (current.IsKiosk)
        {
            throw new UnauthorizedAccessException("Der Kiosk abonniert nie (Benachrichtigungen 3.2).");
        }

        var (tenantId, person) = Require();
        var now = clock.GetUtcNow();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var existing = await db.Subscriptions.SingleOrDefaultAsync(s => s.TenantId == tenantId && s.PersonId == person && s.Endpoint == endpoint, cancellationToken);
        if (existing is not null)
        {
            existing.Renew(endpoint, p256dh, auth);
            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return ToRecord(existing);
        }

        var subscription = PushSubscription.Register(tenantId, person, endpoint, p256dh, auth, deviceLabel, now);
        db.Subscriptions.Add(subscription);
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return ToRecord(subscription);
    }

    public async Task<bool> UnsubscribeAsync(Guid subscriptionId, CancellationToken cancellationToken)
    {
        var (tenantId, person) = Require();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var removed = await db.Subscriptions.Where(s => s.TenantId == tenantId && s.PersonId == person && s.Id == subscriptionId).ExecuteDeleteAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return removed > 0;
    }

    public async Task<IReadOnlyList<EntryRecord>> ListEntriesAsync(bool unreadOnly, CancellationToken cancellationToken)
    {
        var (tenantId, person) = Require();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var query = db.Entries.AsNoTracking().Where(e => e.TenantId == tenantId && e.PersonId == person);
        if (unreadOnly)
        {
            query = query.Where(e => e.ReadAt == null);
        }

        var entries = await query.OrderByDescending(e => e.OccurredAt).Take(100).ToListAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return entries.Select(e => new EntryRecord(e.Id, e.Category, e.TextKey, e.Parameters(), e.Target, e.OccurredAt, e.ReadAt)).ToList();
    }

    public async Task<bool> MarkReadAsync(Guid entryId, CancellationToken cancellationToken)
    {
        var (tenantId, person) = Require();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var entry = await db.Entries.SingleOrDefaultAsync(e => e.TenantId == tenantId && e.PersonId == person && e.Id == entryId, cancellationToken);
        if (entry is null)
        {
            return false;
        }

        entry.MarkRead(clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<int> MarkAllReadAsync(CancellationToken cancellationToken)
    {
        var (tenantId, person) = Require();
        var now = clock.GetUtcNow();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var count = await db.Entries.Where(e => e.TenantId == tenantId && e.PersonId == person && e.ReadAt == null).ExecuteUpdateAsync(s => s.SetProperty(e => e.ReadAt, now), cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return count;
    }

    public async Task<TenantSettingsRecord> GetTenantAsync(CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var tenant = await LoadTenantAsync(tenantId, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return ToRecord(tenant);
    }

    public async Task<TenantSettingsRecord> UpdateTenantAsync(bool pushEnabled, bool emailEnabled, bool aushangEnabled, TimeOnly quietStart, TimeOnly quietEnd, CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var tenant = await db.TenantSettings.SingleOrDefaultAsync(s => s.TenantId == tenantId, cancellationToken);
        if (tenant is null)
        {
            tenant = TenantNotificationSettings.Default(tenantId);
            db.TenantSettings.Add(tenant);
        }

        tenant.Update(pushEnabled, emailEnabled, aushangEnabled, quietStart, quietEnd, clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditEntry("notifications.tenant.settings", tenantId.ToString(), $"push={pushEnabled} email={emailEnabled} aushang={aushangEnabled} ruhe={quietStart:HH:mm}-{quietEnd:HH:mm}"), cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return ToRecord(tenant);
    }

    private (TenantId TenantId, PersonId Person) Require()
    {
        var current = context.Require();
        return (current.RequireTenant(), current.RequirePerson());
    }

    private async Task<TenantNotificationSettings> LoadTenantAsync(TenantId tenantId, CancellationToken cancellationToken) =>
        await db.TenantSettings.AsNoTracking().SingleOrDefaultAsync(s => s.TenantId == tenantId, cancellationToken) ?? TenantNotificationSettings.Default(tenantId);

    private static PersonSettingsRecord ToRecord(PersonNotificationSettings s, TenantNotificationSettings tenant) =>
        new(s.PushChallenge, s.PushProgress, s.EmailChallenge, s.EmailProgress, s.QuietStart, s.QuietEnd, s.EmailPausedAt is not null, ToRecord(tenant));

    private static TenantSettingsRecord ToRecord(TenantNotificationSettings t) => new(t.PushEnabled, t.EmailEnabled, t.AushangEnabled, t.QuietStart, t.QuietEnd);

    private static SubscriptionRecord ToRecord(PushSubscription s) => new(s.Id, s.DeviceLabel, s.CreatedAt, s.LastSuccessAt, !s.Active);
}

internal sealed class UnsubscribeService(IUnsubscribeTokens tokens, ITenantScopeFactory scopes, TimeProvider clock) : IUnsubscribeService
{
    public async Task<NotificationCategory?> UnsubscribeAsync(string token, CancellationToken cancellationToken)
    {
        var parsed = tokens.Unprotect(token);
        if (parsed is null)
        {
            return null;
        }

        await scopes.RunAsync(TenantContext.ForTenant(parsed.TenantId), async (sp, ct) =>
        {
            var db = sp.GetRequiredService<NotificationsDbContext>();
            var transaction = sp.GetRequiredService<IContextTransaction>();
            await using var tx = await transaction.BeginAsync(ct);
            var settings = await db.PersonSettings.SingleOrDefaultAsync(s => s.TenantId == parsed.TenantId && s.PersonId == parsed.PersonId, ct);
            if (settings is null)
            {
                settings = PersonNotificationSettings.Default(parsed.TenantId, parsed.PersonId);
                db.PersonSettings.Add(settings);
            }

            settings.UnsubscribeEmail(parsed.Category, clock.GetUtcNow());
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }, cancellationToken);
        return parsed.Category;
    }
}
