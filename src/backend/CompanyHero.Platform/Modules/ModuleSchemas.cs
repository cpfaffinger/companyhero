namespace CompanyHero.Platform.Modules;

/// <summary>
/// Alle Datenbankschemata des Monolithen. Der Migrations-Container erteilt der Laufzeitrolle
/// genau auf diesen Schemata Rechte; ein Schema außerhalb dieser Liste bleibt für die Laufzeit unsichtbar.
/// </summary>
public static class ModuleSchemas
{
    public const string Platform = "platform";
    public const string Identity = "identity";
    public const string Organisation = "organisation";
    public const string Branding = "branding";
    public const string Entitlements = "entitlements";
    public const string Challenges = "challenges";
    public const string Progress = "progress";
    public const string Feed = "feed";
    public const string Notifications = "notifications";
    public const string Metering = "metering";
    public const string Privacy = "privacy";

    public static IReadOnlyList<string> All { get; } =
    [
        Platform, Identity, Organisation, Branding, Entitlements,
        Challenges, Progress, Feed, Notifications, Metering, Privacy,
    ];
}
