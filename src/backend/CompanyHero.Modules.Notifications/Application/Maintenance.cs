using System.Globalization;
using System.Text.Json.Nodes;
using CompanyHero.Modules.Notifications.Domain;
using CompanyHero.Modules.Notifications.Infrastructure;
using CompanyHero.Modules.Organisation.Application;
using CompanyHero.Platform.Data;
using CompanyHero.Platform.Jobs;
using CompanyHero.Platform.Privacy;
using CompanyHero.Platform.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyHero.Modules.Notifications.Application;

/// <summary>Fristen (Benachrichtigungen 6.3, 7.2, 7.3): Einträge 90 Tage, Zustellstatus 30 Tage, Aushänge 12 Wochen.</summary>
internal sealed class NotificationRetentionTask(ITenantScopeFactory scopes, IOrganisationDirectory organisations, TimeProvider clock) : IScheduledTask
{
    public static string Name => "notifications.retention";

    public static TimeSpan Interval => TimeSpan.FromHours(24);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        foreach (var tenant in await organisations.ListActiveTenantsAsync(cancellationToken))
        {
            await scopes.RunAsync(TenantContext.ForTenant(tenant), async (sp, ct) =>
            {
                var db = sp.GetRequiredService<NotificationsDbContext>();
                var transaction = sp.GetRequiredService<IContextTransaction>();
                await using var tx = await transaction.BeginAsync(ct);
                await db.Deliveries.Where(d => d.TenantId == tenant && d.CreatedAt < now - NotificationRules.DeliveryRetention).ExecuteDeleteAsync(ct);
                await db.Entries.Where(e => e.TenantId == tenant && e.OccurredAt < now - NotificationRules.EntryRetention).ExecuteDeleteAsync(ct);
                await db.Aushaenge.Where(a => a.TenantId == tenant && a.GeneratedAt < now - TimeSpan.FromDays(84)).ExecuteDeleteAsync(ct);
                await tx.CommitAsync(ct);
            }, cancellationToken);
        }
    }
}

/// <summary>Auskunft (Datenschutz 6.4): Einstellungen, Geräte, Einträge; Löschung (5.2, Benachrichtigungen 8): Abonnements, Einstellungen, Einträge und Zustellungen sofort entfernt.</summary>
internal sealed class NotificationsPersonalData(NotificationsDbContext db, IContextTransaction transaction, ITenantContextAccessor context) : IPersonalDataExporter, IPersonalDataEraser
{
    public string Section => "benachrichtigungen";

    public async Task<JsonNode> ExportAsync(PersonId personId, CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var settings = await db.PersonSettings.AsNoTracking().SingleOrDefaultAsync(s => s.TenantId == tenantId && s.PersonId == personId, cancellationToken);
        var subscriptions = await db.Subscriptions.AsNoTracking().Where(s => s.TenantId == tenantId && s.PersonId == personId).ToListAsync(cancellationToken);
        var entries = await db.Entries.AsNoTracking().Where(e => e.TenantId == tenantId && e.PersonId == personId).OrderBy(e => e.OccurredAt).ToListAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return new JsonObject
        {
            ["einstellungen"] = settings is null ? null : new JsonObject
            {
                ["pushChallenge"] = settings.PushChallenge,
                ["pushFortschritt"] = settings.PushProgress,
                ["emailChallenge"] = settings.EmailChallenge,
                ["emailFortschritt"] = settings.EmailProgress,
                ["ruhezeit"] = settings.QuietStart is null ? null : $"{settings.QuietStart:HH:mm}-{settings.QuietEnd:HH:mm}",
            },
            ["geraete"] = new JsonArray(subscriptions.Select(s => (JsonNode)new JsonObject { ["bezeichnung"] = s.DeviceLabel, ["seit"] = s.CreatedAt }).ToArray()),
            ["eintraege"] = new JsonArray(entries.Select(e => (JsonNode)new JsonObject { ["zeitpunkt"] = e.OccurredAt, ["kategorie"] = PushPayload.CategoryName(e.Category), ["text"] = e.TextKey, ["gelesen"] = e.ReadAt }).ToArray()),
        };
    }

    public async Task EraseAsync(PersonId personId, CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        await db.Deliveries.Where(d => d.TenantId == tenantId && d.PersonId == personId).ExecuteDeleteAsync(cancellationToken);
        await db.Entries.Where(e => e.TenantId == tenantId && e.PersonId == personId).ExecuteDeleteAsync(cancellationToken);
        await db.Subscriptions.Where(s => s.TenantId == tenantId && s.PersonId == personId).ExecuteDeleteAsync(cancellationToken);
        await db.PersonSettings.Where(s => s.TenantId == tenantId && s.PersonId == personId).ExecuteDeleteAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }
}

internal static class TimeFormat
{
    public static string Hm(TimeOnly time) => time.ToString("HH:mm", CultureInfo.InvariantCulture);

    public static bool TryParse(string? value, out TimeOnly time) => TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
}
