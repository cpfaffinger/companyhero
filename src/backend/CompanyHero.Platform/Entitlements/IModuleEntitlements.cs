using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyHero.Platform.Entitlements;

/// <summary>Modulcodes des Katalogs (Entitlements 2, A-064); der Kern ist immer aktiv.</summary>
public static class ModuleCodes
{
    public const string Core = "Kern";
    public const string M1Challenges = "M1";
    public const string M2Arena = "M2";
    public const string M3Movement = "M3";
    public const string M4Ergonomics = "M4";
    public const string M5Knowledge = "M5";
    public const string M6Regeneration = "M6";
    public const string M7Nutrition = "M7";
    public const string M8Wearables = "M8";
    public const string M9Events = "M9";
    public const string M10Channel = "M10";
    public const string M11Languages = "M11";
    public const string M12Directory = "M12";
}

/// <summary>Zugriff eines Tenants auf ein Modul zum Zeitpunkt der Prüfung (Entitlements 3.2, 5).</summary>
public enum ModuleAccess
{
    /// <summary>Kein Entitlement oder inaktiv außerhalb der Exportfrist: die API antwortet „nicht gefunden“ (A-067).</summary>
    None = 0,

    /// <summary>Testphase, aktiv oder auslaufend bis <c>aktiv_bis</c>: volle Nutzung.</summary>
    Active = 1,

    /// <summary>Inaktiv innerhalb der 90 Tage nach <c>aktiv_bis</c>: nur lesender Tenant-Export (Entitlements 4.3, A-024).</summary>
    ExportOnly = 2,
}

/// <summary>
/// Querschnitt „Entitlement-Prüfung“ (Entitlements 5, A-067): Eigentümer ist Entitlements, verwendet wird die
/// Schnittstelle vom Autorisierungsquerschnitt der API, von Jobs und von Metering (Bewertung der Testphase).
/// Je Request einmal ausgewertet; Buchung und Kündigung wirken beim nächsten Request (Entitlements 8.10).
/// </summary>
public interface IModuleEntitlements
{
    Task<ModuleAccess> CheckAsync(string moduleCode, CancellationToken cancellationToken);

    /// <summary>Alle Module mit voller Nutzung im aktiven Tenant; der Kern ist immer enthalten.</summary>
    Task<IReadOnlySet<string>> ActiveModulesAsync(CancellationToken cancellationToken);

    /// <summary>Wahr, wenn das Modul zum fachlichen Zeitpunkt <paramref name="at"/> in der Testphase ist: Ereignisse werden erfasst und nicht bewertet (Metering 2.2).</summary>
    Task<bool> IsTrialAsync(string moduleCode, DateTimeOffset at, CancellationToken cancellationToken);
}

/// <summary>Namen der Grenzwerte je Tenant (Entitlements 6.2, A-068); Betriebsschutz, keine Preisstufen.</summary>
public static class TenantLimitNames
{
    public const string KioskDevices = "kiosk_devices";
}

/// <summary>Grenzwerte je Tenant (Entitlements 6.2): Eigentümer Entitlements; geprüft von der Domäne, die den Vorgang ausführt.</summary>
public interface ITenantLimits
{
    Task<int> GetAsync(string name, CancellationToken cancellationToken);
}

/// <summary>Metadatum: Modul, dessen Entitlement der Endpunkt voraussetzt (Entitlements 5).</summary>
public sealed record RequiredModuleMetadata(string Module, bool AllowExportWindow);

public static class ModuleEndpointExtensions
{
    /// <summary>
    /// Endpunkt einer Moduloperation (Entitlements 5, A-067): ohne aktives Entitlement antwortet die API mit „nicht gefunden“,
    /// nie mit „verboten“, damit über die API keine Katalogauskunft entsteht. Mit <paramref name="allowExportWindow"/>
    /// bleibt der Endpunkt in den 90 Tagen nach <c>aktiv_bis</c> für den Tenant-Export erreichbar.
    /// Läuft nach <see cref="Hosting.TenantContextEndpointExtensions.RequireTenantContext{TBuilder}"/>.
    /// </summary>
    public static TBuilder RequireModule<TBuilder>(this TBuilder builder, string module, bool allowExportWindow = false) where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(module);
        builder.WithMetadata(new RequiredModuleMetadata(module, allowExportWindow));
        builder.AddEndpointFilter(async (invocation, next) =>
        {
            var access = await invocation.HttpContext.RequestServices.GetRequiredService<IModuleEntitlements>().CheckAsync(module, invocation.HttpContext.RequestAborted);
            var allowed = access == ModuleAccess.Active || (allowExportWindow && access == ModuleAccess.ExportOnly);
            return allowed ? await next(invocation) : Results.NotFound();
        });
        return builder;
    }
}
