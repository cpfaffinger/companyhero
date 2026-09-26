using CompanyHero.Platform.Entitlements;
using CompanyHero.Platform.Tenancy;

namespace CompanyHero.Modules.Entitlements.Domain;

/// <summary>Zustand eines Entitlements (Entitlements 3.2, A-065).</summary>
public enum EntitlementState
{
    Trial = 1,
    Active = 2,
    Expiring = 3,
    Inactive = 4,
}

/// <summary>Quelle der Buchung (Entitlements 3.1).</summary>
public enum EntitlementSource
{
    TenantAdmin = 1,
    PartnerAdmin = 2,
    OperatorAdmin = 3,
    Preset = 4,
}

/// <summary>Ergebnis eines zeitgesteuerten Übergangs (Entitlements 3.2).</summary>
public enum EntitlementTransition
{
    None = 0,

    /// <summary>Testphase zu Ende, Modul aktiv mit Bewertung ab <c>test_bis</c>.</summary>
    TrialEnded = 1,

    /// <summary>Kündigung in der Testphase: inaktiv zum Testende ohne Kosten.</summary>
    TrialCancelled = 2,

    /// <summary><c>aktiv_bis</c> erreicht: inaktiv, Daten 90 Tage lesbar.</summary>
    Expired = 3,
}

/// <summary>
/// Je Tenant und Modul höchstens ein Datensatz (Entitlements 3.1, A-065) mit Zustand, <c>aktiv_ab</c>, <c>aktiv_bis</c>,
/// <c>test_bis</c>, Quelle und dem Merker, ob die einmalige Testphase verbraucht ist. Jede Zustandsänderung erzeugt einen
/// Eintrag der append-only Historie (<see cref="EntitlementHistoryEntry"/>). Entitlements prüfen nie Zahlungen.
/// </summary>
public sealed class Entitlement : ITenantOwned
{
    private Entitlement(TenantId tenantId, string module)
    {
        TenantId = tenantId;
        Module = module;
        State = EntitlementState.Inactive;
    }

    public TenantId TenantId { get; }

    public string Module { get; }

    public EntitlementState State { get; private set; }

    /// <summary>Beginn der Nutzung, sofort bei Buchung; nach der Testphase der Beginn der Bewertung.</summary>
    public DateTimeOffset? ActiveFrom { get; private set; }

    /// <summary>Leer oder Monatsende nach Kündigung; in der Testphase das Testende einer gekündigten Testphase.</summary>
    public DateTimeOffset? ActiveUntil { get; private set; }

    public DateTimeOffset? TrialUntil { get; private set; }

    public EntitlementSource Source { get; private set; } = EntitlementSource.TenantAdmin;

    /// <summary>Eine Testphase je Tenant und Modul höchstens einmal (Entitlements 3.2).</summary>
    public bool TrialUsed { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Entitlement Create(TenantId tenantId, string module)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(module);
        if (!ModuleCatalog.IsKnown(module))
        {
            throw new EntitlementException("unknown_module", $"Unbekanntes Modul '{module}'.");
        }

        return new Entitlement(tenantId, module);
    }

    /// <summary>Zugriff zum Zeitpunkt <paramref name="now"/> (Entitlements 5): Testphase, aktiv und auslaufend bis <c>aktiv_bis</c> nutzen voll.</summary>
    public ModuleAccess AccessAt(DateTimeOffset now)
    {
        if (State == EntitlementState.Inactive)
        {
            return ActiveUntil is { } until && now < until + ModuleCatalog.ExportWindow ? ModuleAccess.ExportOnly : ModuleAccess.None;
        }

        if (ActiveUntil is { } end && now >= end)
        {
            // Termin erreicht, zeitgesteuerter Übergang steht noch aus: fachlich bereits inaktiv.
            return now < end + ModuleCatalog.ExportWindow ? ModuleAccess.ExportOnly : ModuleAccess.None;
        }

        return ModuleAccess.Active;
    }

    public bool IsUsableAt(DateTimeOffset now) => AccessAt(now) == ModuleAccess.Active;

    /// <summary>Testphase zum fachlichen Zeitpunkt (Metering 2.2): Ereignisse werden erfasst und nicht bewertet; auch für Zeitpunkte vor „jetzt“ innerhalb der laufenden Testphase.</summary>
    public bool IsTrialAt(DateTimeOffset at) => State == EntitlementState.Trial && TrialUntil is { } until && ActiveFrom is { } from && from <= at && at < until && (ActiveUntil is null || at < ActiveUntil);

    /// <summary>Buchung (Entitlements 4.2): Wirkung sofort; eine erneute Buchung nach Inaktivität erhält keine neue Testphase.</summary>
    public void Book(DateTimeOffset now, EntitlementSource source)
    {
        if (IsUsableAt(now) && State != EntitlementState.Trial)
        {
            throw new EntitlementException("already_active", "Das Modul ist bereits aktiv.");
        }

        State = EntitlementState.Active;
        ActiveFrom = now;
        ActiveUntil = null;
        TrialUntil = null;
        Source = source;
        UpdatedAt = now;
    }

    /// <summary>Testphase (Entitlements 3.2, 4.1): einmal je Modul; volle Nutzung, keine Bewertung; endet automatisch mit Aktivierung.</summary>
    public void StartTrial(DateTimeOffset now, EntitlementSource source, int days = ModuleCatalog.TrialDays)
    {
        if (IsUsableAt(now))
        {
            throw new EntitlementException("already_active", "Das Modul ist bereits aktiv.");
        }

        if (TrialUsed)
        {
            throw new EntitlementException("trial_used", "Eine Testphase gibt es je Modul höchstens einmal.");
        }

        State = EntitlementState.Trial;
        ActiveFrom = now;
        ActiveUntil = null;
        TrialUntil = now.AddDays(days);
        TrialUsed = true;
        Source = source;
        UpdatedAt = now;
    }

    /// <summary>Kündigung (Entitlements 4.3): wirksam zum Termin (Monatsende); in der Testphase zum Testende ohne Kosten.</summary>
    public void Cancel(DateTimeOffset now, DateTimeOffset effectiveAt)
    {
        if (!IsUsableAt(now))
        {
            throw new EntitlementException("not_active", "Nur ein aktives Modul kann gekündigt werden.");
        }

        if (State == EntitlementState.Trial)
        {
            ActiveUntil = TrialUntil;
        }
        else
        {
            State = EntitlementState.Expiring;
            ActiveUntil = effectiveAt;
        }

        UpdatedAt = now;
    }

    /// <summary>Rücknahme der Kündigung vor <c>aktiv_bis</c> (Entitlements 3.2): wieder aktiv beziehungsweise Testphase ohne Ende.</summary>
    public void RevokeCancellation(DateTimeOffset now)
    {
        if (ActiveUntil is null || !IsUsableAt(now))
        {
            throw new EntitlementException("not_cancelled", "Es liegt keine Kündigung vor, die zurückgenommen werden kann.");
        }

        ActiveUntil = null;
        if (State == EntitlementState.Expiring)
        {
            State = EntitlementState.Active;
        }

        UpdatedAt = now;
    }

    /// <summary>Erzwungene Deaktivierung durch den Operator (Entitlements 4.4) zum Termin, bei rechtlicher Notwendigkeit sofort.</summary>
    public void ForceDeactivate(DateTimeOffset now, DateTimeOffset effectiveAt)
    {
        if (effectiveAt <= now)
        {
            State = EntitlementState.Inactive;
            ActiveUntil = now;
        }
        else
        {
            State = EntitlementState.Expiring;
            ActiveUntil = effectiveAt;
        }

        UpdatedAt = now;
    }

    /// <summary>Zeitgesteuerter Übergang (Entitlements 3.2): Testende oder <c>aktiv_bis</c> erreicht.</summary>
    public EntitlementTransition Advance(DateTimeOffset now)
    {
        if (State == EntitlementState.Trial && TrialUntil is { } trialUntil && now >= trialUntil)
        {
            if (ActiveUntil is not null)
            {
                State = EntitlementState.Inactive;
                ActiveUntil = trialUntil;
                UpdatedAt = now;
                return EntitlementTransition.TrialCancelled;
            }

            State = EntitlementState.Active;
            ActiveFrom = trialUntil;
            TrialUntil = null;
            UpdatedAt = now;
            return EntitlementTransition.TrialEnded;
        }

        if (State == EntitlementState.Expiring && ActiveUntil is { } until && now >= until)
        {
            State = EntitlementState.Inactive;
            UpdatedAt = now;
            return EntitlementTransition.Expired;
        }

        return EntitlementTransition.None;
    }
}

/// <summary>Append-only Historie (Entitlements 3.1): jede Zustandsänderung mit Zeitpunkt, Rolle und Grund; Grundlage für Rechnung, Einsichtsrolle und Prüfprotokoll.</summary>
public sealed class EntitlementHistoryEntry : ITenantOwned
{
    private EntitlementHistoryEntry(TenantId tenantId, Guid id, string module, DateTimeOffset occurredAt, EntitlementState? fromState, EntitlementState toState, string actorRoles, EntitlementSource source, string reason)
    {
        TenantId = tenantId;
        Id = id;
        Module = module;
        OccurredAt = occurredAt;
        FromState = fromState;
        ToState = toState;
        ActorRoles = actorRoles;
        Source = source;
        Reason = reason;
    }

    public TenantId TenantId { get; }

    public Guid Id { get; }

    public string Module { get; }

    public DateTimeOffset OccurredAt { get; }

    public EntitlementState? FromState { get; }

    public EntitlementState ToState { get; }

    /// <summary>Rollen der handelnden Person, alphabetisch; leer für zeitgesteuerte Übergänge. Kein Personenbezug.</summary>
    public string ActorRoles { get; }

    public EntitlementSource Source { get; }

    /// <summary>Fester Bezeichner: <c>booked</c>, <c>trial_started</c>, <c>cancelled</c>, <c>cancellation_revoked</c>, <c>trial_ended</c>, <c>expired</c>, <c>preset</c>, <c>forced:&lt;Grund&gt;</c>.</summary>
    public string Reason { get; }

    public static EntitlementHistoryEntry Create(TenantId tenantId, string module, DateTimeOffset occurredAt, EntitlementState? fromState, EntitlementState toState, IEnumerable<string> roles, EntitlementSource source, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new EntitlementHistoryEntry(tenantId, Guid.CreateVersion7(), module, occurredAt, fromState, toState, string.Join(',', roles.Order(StringComparer.Ordinal)), source, reason.Length > 200 ? reason[..200] : reason);
    }
}

/// <summary>Grenzwert je Tenant (Entitlements 6.2, A-068) als Betriebsschutz mit Standardwert; der Operator ändert ihn je Tenant.</summary>
public sealed class TenantLimit : ITenantOwned
{
    private TenantLimit(TenantId tenantId, string name, int value, DateTimeOffset updatedAt)
    {
        TenantId = tenantId;
        Name = name;
        Value = value;
        UpdatedAt = updatedAt;
    }

    public TenantId TenantId { get; }

    public string Name { get; }

    public int Value { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Standardwerte (Entitlements 6.2): Kiosk-Geräte 20.</summary>
    public static int DefaultFor(string name) => name switch
    {
        TenantLimitNames.KioskDevices => 20,
        _ => throw new EntitlementException("unknown_limit", $"Unbekannter Grenzwert '{name}'."),
    };

    public static TenantLimit Create(TenantId tenantId, string name, int value, DateTimeOffset now)
    {
        _ = DefaultFor(name);
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Grenzwerte sind nicht negativ.");
        }

        return new TenantLimit(tenantId, name, value, now);
    }

    public void Set(int value, DateTimeOffset now)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Grenzwerte sind nicht negativ.");
        }

        Value = value;
        UpdatedAt = now;
    }
}

public sealed class EntitlementException : InvalidOperationException
{
    public EntitlementException()
        : this("entitlement", "Entitlement-Regel verletzt.")
    {
    }

    public EntitlementException(string message)
        : this("entitlement", message)
    {
    }

    public EntitlementException(string message, Exception innerException)
        : base(message, innerException)
    {
        Reason = "entitlement";
    }

    public EntitlementException(string reason, string message)
        : base(message)
    {
        Reason = reason;
    }

    /// <summary>Neutraler Grund für den Vertrag: <c>unknown_module</c>, <c>already_active</c>, <c>trial_used</c>, <c>not_active</c>, <c>not_cancelled</c>.</summary>
    public string Reason { get; }
}
