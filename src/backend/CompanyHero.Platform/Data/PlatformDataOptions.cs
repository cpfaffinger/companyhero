namespace CompanyHero.Platform.Data;

/// <summary>
/// Datenzugriff eines Prozesses. API und Worker laufen mit der Laufzeitrolle und erzwingen den Tenant-Kontext;
/// der Migrations-Container läuft mit der Migrationsrolle ohne Kontext (er bewegt keine Tenant-Daten).
/// </summary>
public sealed record PlatformDataOptions
{
    public required string ConnectionString { get; init; }

    /// <summary>
    /// Wahr für API und Worker: jede Operation eines Modulkontexts braucht einen geprüften Kontext und eine Transaktion,
    /// in der der Kontext gesetzt wurde. Falsch nur für den Migrationslauf.
    /// </summary>
    public bool EnforceTenantContext { get; init; } = true;
}

/// <summary>Ein registrierter Modulkontext; der Migrationslauf iteriert diese Liste (ein Schema, eine Historie je Modul).</summary>
public sealed record ModuleDbContextRegistration(Type ContextType, string Schema);
