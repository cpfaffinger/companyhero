using CompanyHero.Modules.Entitlements.Domain;
using CompanyHero.Modules.Entitlements.Infrastructure;
using CompanyHero.Modules.Organisation.Application;
using CompanyHero.Modules.Organisation.Domain;
using CompanyHero.Platform.Audit;
using CompanyHero.Platform.Data;
using CompanyHero.Platform.Entitlements;
using CompanyHero.Platform.Jobs;
using CompanyHero.Platform.Metering;
using CompanyHero.Platform.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyHero.Modules.Entitlements.Application;

/// <summary>Ein Modul des Katalogs mit seinem Zustand im aktiven Tenant (Entitlements 2.2, 3.1).</summary>
public sealed record ModuleStatus(string Module, string NameKey, EntitlementState State, DateTimeOffset? ActiveFrom, DateTimeOffset? ActiveUntil, DateTimeOffset? TrialUntil, bool TrialAvailable, IReadOnlyList<string> Dependencies, string PrivacyHintKey);

public enum BookingResult
{
    Booked = 1,

    /// <summary>Abhängigkeit unerfüllt: Bündelvorschlag statt Ablehnung (Entitlements 4.2 Nr. 3).</summary>
    BundleSuggested = 2,
    AlreadyActive = 3,
}

/// <summary>Ergebnis einer Buchung: gebuchte Module oder der Bündelvorschlag.</summary>
public sealed record BookingOutcome(BookingResult Result, IReadOnlyList<string> Suggestion, IReadOnlyList<string> Booked);

public enum TrialResult
{
    Started = 1,
    TrialUsed = 2,
    AlreadyActive = 3,
    DependencyMissing = 4,
}

public sealed record TrialOutcome(TrialResult Result, DateTimeOffset? TrialUntil);

/// <summary>Kündigung oder Rücknahme: betroffene Module einschließlich abhängiger (Entitlements 4.3) und der Termin.</summary>
public sealed record CancellationOutcome(IReadOnlyList<string> Affected, DateTimeOffset? ActiveUntil);

public sealed record EntitlementHistoryRecord(Guid Id, string Module, DateTimeOffset OccurredAt, EntitlementState? FromState, EntitlementState ToState, string ActorRoles, EntitlementSource Source, string Reason);

/// <summary>Öffentliche Anwendungsfunktionen der Domäne Entitlements (Domänenkarte 2); Tenant-Kontext.</summary>
public interface IEntitlementService
{
    Task<IReadOnlyList<ModuleStatus>> CatalogAsync(CancellationToken cancellationToken);

    /// <summary>Buchung eines Moduls (Entitlements 4.2): sofort wirksam; bei fehlender Voraussetzung ein Bündelvorschlag; Metering erhält „Modul aktiviert“.</summary>
    Task<BookingOutcome> BookAsync(string moduleCode, CancellationToken cancellationToken);

    /// <summary>Buchung eines Bündels als einzelne Entitlements (Entitlements 2.3) in Katalogreihenfolge.</summary>
    Task<BookingOutcome> BookBundleAsync(IReadOnlyList<string> modules, CancellationToken cancellationToken);

    Task<TrialOutcome> StartTrialAsync(string moduleCode, CancellationToken cancellationToken);

    /// <summary>Kündigung zum Monatsende; abhängige Module enden zum selben Termin (Entitlements 4.3).</summary>
    Task<CancellationOutcome> CancelAsync(string moduleCode, CancellationToken cancellationToken);

    /// <summary>Rücknahme vor <c>aktiv_bis</c> stellt das Modul und die mitgekündigten abhängigen Module wieder her.</summary>
    Task<CancellationOutcome> RevokeCancellationAsync(string moduleCode, CancellationToken cancellationToken);

    Task<IReadOnlyList<EntitlementHistoryRecord>> HistoryAsync(CancellationToken cancellationToken);
}

/// <summary>Operator-Anwendungsfälle (Entitlements 4.1, 4.4, 6.2) im Tenant-Kontext des betroffenen Tenants.</summary>
public interface IEntitlementAdministration
{
    /// <summary>Erzwungene Deaktivierung mit Grund im Protokoll; <paramref name="effectiveAt"/> in der Vergangenheit oder jetzt wirkt sofort (rechtliche Notwendigkeit).</summary>
    Task ForceDeactivateAsync(string moduleCode, string reason, DateTimeOffset effectiveAt, CancellationToken cancellationToken);

    Task SetLimitAsync(string name, int value, CancellationToken cancellationToken);
}

internal static class EntitlementConstants
{
    public const string TenantMonthMetric = "tenant.month";
    public const string TrialDayMetric = "module.trial_day";
}

/// <summary>
/// Lädt die Entitlements des aktiven Tenants innerhalb der laufenden Kontexttransaktion und wendet beim ersten Kontakt die
/// Voreinstellung des Operators an (Entitlements 3.3: Voreinstellungen erzeugen aktive Entitlements mit Quelle „Voreinstellung“).
/// </summary>
internal sealed class EntitlementRegistry(EntitlementsDbContext db, ITenantContextAccessor context, ITenantTimeZone timeZone, IServiceProvider services, TimeProvider clock)
{
    // Metering prüft bei jeder Emission die Testphase über IModuleEntitlements; die Emission wird deshalb erst beim Aufruf aufgelöst (kein Zyklus im Container).
    private IMeteringEmitter Metering => services.GetRequiredService<IMeteringEmitter>();

    public async Task<List<Entitlement>> LoadAsync(CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        var entitlements = await db.Entitlements.Where(e => e.TenantId == tenantId).ToListAsync(cancellationToken);
        if (entitlements.Count > 0 || await db.History.AnyAsync(h => h.TenantId == tenantId, cancellationToken))
        {
            return entitlements;
        }

        var now = clock.GetUtcNow();
        var zone = await timeZone.GetAsync(cancellationToken);
        var presets = new List<EntitlementHistoryEntry>();
        foreach (var module in ModuleCatalog.Preset)
        {
            var entitlement = Entitlement.Create(tenantId, module);
            entitlement.Book(now, EntitlementSource.Preset);
            db.Entitlements.Add(entitlement);
            var entry = EntitlementHistoryEntry.Create(tenantId, module, now, null, EntitlementState.Active, [], EntitlementSource.Preset, "preset");
            db.History.Add(entry);
            presets.Add(entry);
            entitlements.Add(entitlement);
        }

        // Erst speichern, dann melden: die Emission prüft die Testphase über diese Schnittstelle und liest die Zeilen zurück.
        await db.SaveChangesAsync(cancellationToken);
        foreach (var entry in presets)
        {
            await EmitActivationAsync(entry.Module, now, zone, entry.Id, cancellationToken);
        }

        return entitlements;
    }

    public Entitlement GetOrCreate(List<Entitlement> entitlements, string module)
    {
        var existing = entitlements.FirstOrDefault(e => string.Equals(e.Module, module, StringComparison.Ordinal));
        if (existing is not null)
        {
            return existing;
        }

        var created = Entitlement.Create(context.Require().RequireTenant(), module);
        db.Entitlements.Add(created);
        entitlements.Add(created);
        return created;
    }

    public static IReadOnlySet<string> ActiveModules(IEnumerable<Entitlement> entitlements, DateTimeOffset now) =>
        new HashSet<string>(entitlements.Where(e => e.IsUsableAt(now)).Select(e => e.Module).Append(ModuleCodes.Core), StringComparer.Ordinal);

    /// <summary>„Modul aktiviert“ an Metering (Entitlements 4.2 Nr. 6): Monatsmenge ab dem Aktivierungstag, tagesgenau (Metering 4.5).</summary>
    public Task EmitActivationAsync(string module, DateTimeOffset activatedAt, TimeZoneInfo zone, Guid activationId, CancellationToken cancellationToken)
    {
        var period = TenantTimeZone.PeriodOf(activatedAt, zone);
        var fraction = Proration.RemainingMonthFraction(activatedAt, zone);
        // Fachereignis ist der Historieneintrag der Aktivierung (Metering 2.1: Idempotenzschlüssel aus Fachereignis-ID und Metrik).
        return Metering.EmitAsync(new MeteringEmission(module, EntitlementConstants.TenantMonthMetric, "module:" + module, fraction, MeteringSource.Administration, activatedAt, $"{module}:{period}:{EntitlementConstants.TenantMonthMetric}:{activationId:D}"), cancellationToken);
    }

    /// <summary>Sofortige Deaktivierung mitten im Monat (Entitlements 4.4): der ungenutzte Rest des Monats wird als Gegenbuchung zurückgenommen (Metering 3.3).</summary>
    public Task EmitDeactivationAsync(string module, DateTimeOffset deactivatedAt, TimeZoneInfo zone, Guid deactivationId, CancellationToken cancellationToken)
    {
        var period = TenantTimeZone.PeriodOf(deactivatedAt, zone);
        var nextDay = TenantTimeZone.StartOfDay(TenantTimeZone.DayOf(deactivatedAt, zone).AddDays(1), zone);
        if (TenantTimeZone.PeriodOf(nextDay, zone) != period)
        {
            return Task.CompletedTask;
        }

        var unused = Proration.RemainingMonthFraction(nextDay, zone);
        return Metering.EmitAsync(new MeteringEmission(module, EntitlementConstants.TenantMonthMetric, "module:" + module, -unused, MeteringSource.Administration, deactivatedAt, $"{module}:{period}:{EntitlementConstants.TenantMonthMetric}:forced:{deactivationId:D}"), cancellationToken);
    }

    public Task EmitTrialStartAsync(string module, DateTimeOffset startedAt, TimeZoneInfo zone, CancellationToken cancellationToken)
    {
        var day = TenantTimeZone.DayOf(startedAt, zone);
        return Metering.EmitAsync(new MeteringEmission(module, EntitlementConstants.TrialDayMetric, "module:" + module, 1m, MeteringSource.Administration, startedAt, $"{module}:{day:yyyy-MM-dd}:{EntitlementConstants.TrialDayMetric}"), cancellationToken);
    }
}

internal sealed class EntitlementService(EntitlementsDbContext db, IContextTransaction transaction, ITenantContextAccessor context, ITenantTimeZone timeZone, EntitlementRegistry registry, IAuditLog audit, TimeProvider clock) : IEntitlementService
{
    public async Task<IReadOnlyList<ModuleStatus>> CatalogAsync(CancellationToken cancellationToken)
    {
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var entitlements = await registry.LoadAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        var now = clock.GetUtcNow();
        return ModuleCatalog.Modules.Select(m =>
        {
            var e = entitlements.FirstOrDefault(x => x.Module == m.Code);
            var state = e is null ? EntitlementState.Inactive : e.IsUsableAt(now) ? e.State : EntitlementState.Inactive;
            return new ModuleStatus(m.Code, m.NameKey, state, e?.ActiveFrom, e?.ActiveUntil, e?.IsUsableAt(now) == true ? e.TrialUntil : null, e?.TrialUsed != true, m.Dependencies, m.PrivacyHintKey);
        }).ToList();
    }

    public Task<BookingOutcome> BookAsync(string module, CancellationToken cancellationToken) => BookBundleAsync([module], cancellationToken);

    public async Task<BookingOutcome> BookBundleAsync(IReadOnlyList<string> modules, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(modules);
        var requested = ModuleCatalog.Modules.Select(m => m.Code).Where(c => modules.Contains(c, StringComparer.Ordinal)).ToList();
        if (requested.Count != modules.Distinct(StringComparer.Ordinal).Count())
        {
            throw new EntitlementException("unknown_module", "Unbekanntes Modul.");
        }

        var now = clock.GetUtcNow();
        var zone = await timeZone.GetAsync(cancellationToken);
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var entitlements = await registry.LoadAsync(cancellationToken);
        var active = new HashSet<string>(EntitlementRegistry.ActiveModules(entitlements, now), StringComparer.Ordinal);
        active.UnionWith(requested);

        // Bündelvorschlag statt Ablehnung (Entitlements 4.2 Nr. 3): alle fehlenden Voraussetzungen des Wunsches, nichts wird gebucht.
        var missing = requested.SelectMany(r => ModuleCatalog.MissingDependencies(r, active)).Distinct(StringComparer.Ordinal).ToList();
        if (missing.Count > 0)
        {
            await tx.CommitAsync(cancellationToken);
            var suggestion = ModuleCatalog.Modules.Select(m => m.Code).Where(c => requested.Contains(c, StringComparer.Ordinal) || missing.Contains(c, StringComparer.Ordinal)).ToList();
            return new BookingOutcome(BookingResult.BundleSuggested, suggestion, []);
        }

        var booked = new List<string>();
        var roles = context.Require().Roles;
        foreach (var module in requested)
        {
            var entitlement = registry.GetOrCreate(entitlements, module);
            if (entitlement.IsUsableAt(now) && entitlement.State != EntitlementState.Trial)
            {
                if (entitlement.ActiveUntil is not null)
                {
                    // Buchung eines auslaufenden Moduls ist die Rücknahme der Kündigung.
                    var before = entitlement.State;
                    entitlement.RevokeCancellation(now);
                    db.History.Add(EntitlementHistoryEntry.Create(entitlement.TenantId, module, now, before, entitlement.State, roles, EntitlementSource.TenantAdmin, "cancellation_revoked"));
                    booked.Add(module);
                }

                continue;
            }

            var from = entitlement.State;
            var wasTrial = entitlement.IsTrialAt(now);
            entitlement.Book(now, EntitlementSource.TenantAdmin);
            var entry = EntitlementHistoryEntry.Create(entitlement.TenantId, module, now, from, entitlement.State, roles, EntitlementSource.TenantAdmin, wasTrial ? "trial_ended_by_booking" : "booked");
            db.History.Add(entry);
            await registry.EmitActivationAsync(module, now, zone, entry.Id, cancellationToken);
            await audit.RecordAsync(new AuditEntry("entitlements.module.booked", module, wasTrial ? "from_trial" : null), cancellationToken);
            booked.Add(module);
        }

        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return new BookingOutcome(booked.Count > 0 ? BookingResult.Booked : BookingResult.AlreadyActive, [], booked);
    }

    public async Task<TrialOutcome> StartTrialAsync(string module, CancellationToken cancellationToken)
    {
        if (!ModuleCatalog.IsKnown(module))
        {
            throw new EntitlementException("unknown_module", "Unbekanntes Modul.");
        }

        var now = clock.GetUtcNow();
        var zone = await timeZone.GetAsync(cancellationToken);
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var entitlements = await registry.LoadAsync(cancellationToken);
        var active = EntitlementRegistry.ActiveModules(entitlements, now);
        if (ModuleCatalog.MissingDependencies(module, active).Count > 0)
        {
            await tx.CommitAsync(cancellationToken);
            return new TrialOutcome(TrialResult.DependencyMissing, null);
        }

        var entitlement = registry.GetOrCreate(entitlements, module);
        if (entitlement.IsUsableAt(now))
        {
            await tx.CommitAsync(cancellationToken);
            return new TrialOutcome(TrialResult.AlreadyActive, null);
        }

        if (entitlement.TrialUsed)
        {
            await tx.CommitAsync(cancellationToken);
            return new TrialOutcome(TrialResult.TrialUsed, null);
        }

        var from = entitlement.State;
        entitlement.StartTrial(now, EntitlementSource.TenantAdmin);
        db.History.Add(EntitlementHistoryEntry.Create(entitlement.TenantId, module, now, from, entitlement.State, context.Require().Roles, EntitlementSource.TenantAdmin, "trial_started"));
        await db.SaveChangesAsync(cancellationToken);
        // „Testphase gestartet“ an Metering (Entitlements 4.2 Nr. 6): erster Testtag; weitere Testtage meldet der zeitgesteuerte Lauf.
        await registry.EmitTrialStartAsync(module, now, zone, cancellationToken);
        await audit.RecordAsync(new AuditEntry("entitlements.trial.started", module, null), cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return new TrialOutcome(TrialResult.Started, entitlement.TrialUntil);
    }

    public async Task<CancellationOutcome> CancelAsync(string module, CancellationToken cancellationToken)
    {
        if (!ModuleCatalog.IsKnown(module))
        {
            throw new EntitlementException("unknown_module", "Unbekanntes Modul.");
        }

        var now = clock.GetUtcNow();
        var zone = await timeZone.GetAsync(cancellationToken);
        var effectiveAt = Proration.CancellationEffectiveAt(now, zone);
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var entitlements = await registry.LoadAsync(cancellationToken);
        var active = EntitlementRegistry.ActiveModules(entitlements, now);
        var target = entitlements.FirstOrDefault(e => e.Module == module);
        if (target is null || !target.IsUsableAt(now))
        {
            throw new EntitlementException("not_active", "Nur ein aktives Modul kann gekündigt werden.");
        }

        // Kaskade (Entitlements 4.3): abhängige Module enden zum selben Termin; die Verwaltung zeigt das vorher.
        var affected = new List<string> { module };
        affected.AddRange(ModuleCatalog.Dependents(module, active));
        var roles = context.Require().Roles;
        DateTimeOffset? until = null;
        foreach (var code in affected)
        {
            var entitlement = entitlements.Single(e => e.Module == code);
            if (entitlement.ActiveUntil is not null)
            {
                continue;
            }

            var from = entitlement.State;
            entitlement.Cancel(now, effectiveAt);
            until ??= entitlement.ActiveUntil;
            db.History.Add(EntitlementHistoryEntry.Create(entitlement.TenantId, code, now, from, entitlement.State, roles, EntitlementSource.TenantAdmin, code == module ? "cancelled" : "cancelled_with:" + module));
            await audit.RecordAsync(new AuditEntry("entitlements.module.cancelled", code, code == module ? null : "with:" + module), cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return new CancellationOutcome(affected, until ?? target.ActiveUntil);
    }

    public async Task<CancellationOutcome> RevokeCancellationAsync(string module, CancellationToken cancellationToken)
    {
        if (!ModuleCatalog.IsKnown(module))
        {
            throw new EntitlementException("unknown_module", "Unbekanntes Modul.");
        }

        var now = clock.GetUtcNow();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var entitlements = await registry.LoadAsync(cancellationToken);
        var target = entitlements.FirstOrDefault(e => e.Module == module);
        if (target is null || target.ActiveUntil is null || !target.IsUsableAt(now))
        {
            throw new EntitlementException("not_cancelled", "Es liegt keine Kündigung vor.");
        }

        // Die Rücknahme stellt auch die mitgekündigten abhängigen Module wieder her (Entitlements 8.4).
        var affected = new List<string> { module };
        affected.AddRange(entitlements.Where(e => e.ActiveUntil is not null && e.IsUsableAt(now) && ModuleCatalog.Find(e.Module)!.Dependencies.Contains(module, StringComparer.Ordinal)).Select(e => e.Module));
        var roles = context.Require().Roles;
        foreach (var code in affected)
        {
            var entitlement = entitlements.Single(e => e.Module == code);
            var from = entitlement.State;
            entitlement.RevokeCancellation(now);
            db.History.Add(EntitlementHistoryEntry.Create(entitlement.TenantId, code, now, from, entitlement.State, roles, EntitlementSource.TenantAdmin, "cancellation_revoked"));
            await audit.RecordAsync(new AuditEntry("entitlements.cancellation.revoked", code, null), cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return new CancellationOutcome(affected, null);
    }

    public async Task<IReadOnlyList<EntitlementHistoryRecord>> HistoryAsync(CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var rows = await db.History.AsNoTracking().Where(h => h.TenantId == tenantId).OrderBy(h => h.OccurredAt).ThenBy(h => h.Id).ToListAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return rows.Select(h => new EntitlementHistoryRecord(h.Id, h.Module, h.OccurredAt, h.FromState, h.ToState, h.ActorRoles, h.Source, h.Reason)).ToList();
    }
}

internal sealed class EntitlementAdministration(EntitlementsDbContext db, IContextTransaction transaction, ITenantContextAccessor context, ITenantTimeZone timeZone, EntitlementRegistry registry, IAuditLog audit, TimeProvider clock) : IEntitlementAdministration
{
    public async Task ForceDeactivateAsync(string module, string reason, DateTimeOffset effectiveAt, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        var now = clock.GetUtcNow();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var entitlements = await registry.LoadAsync(cancellationToken);
        var entitlement = registry.GetOrCreate(entitlements, module);
        var from = entitlement.State;
        var wasRated = entitlement.IsUsableAt(now) && !entitlement.IsTrialAt(now);
        entitlement.ForceDeactivate(now, effectiveAt);
        var entry = EntitlementHistoryEntry.Create(entitlement.TenantId, module, now, from, entitlement.State, context.Require().Roles, EntitlementSource.OperatorAdmin, "forced:" + reason);
        db.History.Add(entry);
        await db.SaveChangesAsync(cancellationToken);
        if (wasRated && entitlement.State == EntitlementState.Inactive)
        {
            await registry.EmitDeactivationAsync(module, now, await timeZone.GetAsync(cancellationToken), entry.Id, cancellationToken);
        }

        await audit.RecordAsync(new AuditEntry("entitlements.module.forced_deactivation", module, reason.Length > 100 ? reason[..100] : reason), cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    public async Task SetLimitAsync(string name, int value, CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        var now = clock.GetUtcNow();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var limit = await db.Limits.SingleOrDefaultAsync(l => l.TenantId == tenantId && l.Name == name, cancellationToken);
        if (limit is null)
        {
            db.Limits.Add(TenantLimit.Create(tenantId, name, value, now));
        }
        else
        {
            limit.Set(value, now);
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditEntry("entitlements.limit.changed", name, value.ToString(System.Globalization.CultureInfo.InvariantCulture)), cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }
}

/// <summary>Autorisierungsquerschnitt (Entitlements 5, A-067): je Scope einmal ausgewertet; Buchung wirkt beim nächsten Request.</summary>
internal sealed class ModuleEntitlements(IContextTransaction transaction, ITenantContextAccessor context, EntitlementRegistry registry, TimeProvider clock) : IModuleEntitlements
{
    private List<Entitlement>? _loaded;

    public async Task<ModuleAccess> CheckAsync(string module, CancellationToken cancellationToken)
    {
        if (string.Equals(module, ModuleCodes.Core, StringComparison.Ordinal))
        {
            return ModuleAccess.Active;
        }

        var entitlements = await LoadAsync(cancellationToken);
        return entitlements.FirstOrDefault(e => e.Module == module)?.AccessAt(clock.GetUtcNow()) ?? ModuleAccess.None;
    }

    public async Task<IReadOnlySet<string>> ActiveModulesAsync(CancellationToken cancellationToken) =>
        EntitlementRegistry.ActiveModules(await LoadAsync(cancellationToken), clock.GetUtcNow());

    public async Task<bool> IsTrialAsync(string module, DateTimeOffset at, CancellationToken cancellationToken)
    {
        if (string.Equals(module, ModuleCodes.Core, StringComparison.Ordinal) || !ModuleCatalog.IsKnown(module))
        {
            return false;
        }

        var entitlements = await LoadAsync(cancellationToken);
        return entitlements.FirstOrDefault(e => e.Module == module)?.IsTrialAt(at) == true;
    }

    private async Task<List<Entitlement>> LoadAsync(CancellationToken cancellationToken)
    {
        if (_loaded is not null)
        {
            return _loaded;
        }

        if (context.Require().Kind != TenantContextKind.Tenant)
        {
            return _loaded = [];
        }

        await using var tx = await transaction.BeginAsync(cancellationToken);
        _loaded = await registry.LoadAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return _loaded;
    }
}

internal sealed class TenantLimits(EntitlementsDbContext db, IContextTransaction transaction, ITenantContextAccessor context) : ITenantLimits
{
    public async Task<int> GetAsync(string name, CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var limit = await db.Limits.AsNoTracking().SingleOrDefaultAsync(l => l.TenantId == tenantId && l.Name == name, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return limit?.Value ?? TenantLimit.DefaultFor(name);
    }
}

/// <summary>
/// Zeitgesteuert je Stunde (Entitlements 3.2; Metering 3.1): Testende → aktiv mit proratierter Bewertung ab Testende, gekündigte
/// Testphase → inaktiv, <c>aktiv_bis</c> → inaktiv; je Monat <c>tenant.month</c> für Module, die am Monatsersten aktiv waren;
/// je Testtag <c>module.trial_day</c>. Alle Emissionen dedupliziert über Idempotenzschlüssel.
/// </summary>
internal sealed class EntitlementTransitionsTask(ITenantScopeFactory scopes, IOrganisationDirectory organisations, TimeProvider clock) : IScheduledTask
{
    public static string Name => "entitlements.transitions";

    public static TimeSpan Interval => TimeSpan.FromHours(1);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var tenants = await organisations.ListActiveTenantsAsync(cancellationToken);
        foreach (var tenant in tenants)
        {
            await scopes.RunAsync(TenantContext.ForTenant(tenant), (sp, ct) => RunForTenantAsync(sp, ct), cancellationToken);
        }
    }

    private async Task RunForTenantAsync(IServiceProvider sp, CancellationToken cancellationToken)
    {
        var db = sp.GetRequiredService<EntitlementsDbContext>();
        var registry = sp.GetRequiredService<EntitlementRegistry>();
        var metering = sp.GetRequiredService<IMeteringEmitter>();
        var zone = await sp.GetRequiredService<ITenantTimeZone>().GetAsync(cancellationToken);
        var now = clock.GetUtcNow();
        await using var tx = await sp.GetRequiredService<IContextTransaction>().BeginAsync(cancellationToken);
        var entitlements = await registry.LoadAsync(cancellationToken);
        var periodStart = Proration.StartOfPeriod(now, zone);
        var period = TenantTimeZone.PeriodOf(now, zone);
        var today = TenantTimeZone.DayOf(now, zone);

        foreach (var entitlement in entitlements)
        {
            // Testtage bis heute (Metering 3.1 module.trial_day): nur Nachweis, nie bewertet.
            if (entitlement.State == EntitlementState.Trial && entitlement.ActiveFrom is { } trialFrom && entitlement.TrialUntil is { } trialUntil)
            {
                var lastTrialDay = TenantTimeZone.DayOf(trialUntil.AddSeconds(-1), zone);
                var end = today < lastTrialDay ? today : lastTrialDay;
                for (var day = TenantTimeZone.DayOf(trialFrom, zone); day <= end; day = day.AddDays(1))
                {
                    await metering.EmitAsync(new MeteringEmission(entitlement.Module, EntitlementConstants.TrialDayMetric, "module:" + entitlement.Module, 1m, MeteringSource.Automatic, TenantTimeZone.StartOfDay(day, zone), $"{entitlement.Module}:{day:yyyy-MM-dd}:{EntitlementConstants.TrialDayMetric}"), cancellationToken);
                }
            }

            var from = entitlement.State;
            var transition = entitlement.Advance(now);
            switch (transition)
            {
                case EntitlementTransition.TrialEnded:
                    var ended = EntitlementHistoryEntry.Create(entitlement.TenantId, entitlement.Module, now, from, entitlement.State, [], entitlement.Source, "trial_ended");
                    db.History.Add(ended);
                    // Übergang in aktiv proratiert ab Testende (Metering 6.5).
                    await registry.EmitActivationAsync(entitlement.Module, entitlement.ActiveFrom!.Value, zone, ended.Id, cancellationToken);
                    break;
                case EntitlementTransition.TrialCancelled:
                    db.History.Add(EntitlementHistoryEntry.Create(entitlement.TenantId, entitlement.Module, now, from, entitlement.State, [], entitlement.Source, "trial_cancelled"));
                    break;
                case EntitlementTransition.Expired:
                    db.History.Add(EntitlementHistoryEntry.Create(entitlement.TenantId, entitlement.Module, now, from, entitlement.State, [], entitlement.Source, "expired"));
                    break;
                default:
                    break;
            }

            // Flatrate je Modul und Monat (Metering 3.1 tenant.month): voller Monat für Module, die am Monatsersten aktiv oder auslaufend waren.
            if (entitlement.State is EntitlementState.Active or EntitlementState.Expiring && entitlement.ActiveFrom is { } activeFrom && activeFrom < periodStart && entitlement.IsUsableAt(periodStart))
            {
                await metering.EmitAsync(new MeteringEmission(entitlement.Module, EntitlementConstants.TenantMonthMetric, "module:" + entitlement.Module, 1m, MeteringSource.Automatic, periodStart, $"{entitlement.Module}:{period}:{EntitlementConstants.TenantMonthMetric}"), cancellationToken);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }
}
