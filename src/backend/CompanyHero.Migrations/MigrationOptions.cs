namespace CompanyHero.Migrations;

/// <summary>Eingaben des Migrationslaufs. Der Container erhält sie aus Umgebungsvariablen, die Tests direkt.</summary>
public sealed record MigrationOptions
{
    /// <summary>Verbindung mit der Migrationsrolle (Besitzer der Schemata).</summary>
    public required string MigratorConnectionString { get; init; }

    /// <summary>Laufzeitrolle von API und Worker, erhält nur Datenrechte, keinen Besitz.</summary>
    public string RuntimeRole { get; init; } = "ch_app";

    public required string ReleaseVersion { get; init; }

    public string? ImageDigest { get; init; }
}
