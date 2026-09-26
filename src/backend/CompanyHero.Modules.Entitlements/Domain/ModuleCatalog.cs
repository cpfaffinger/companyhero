using CompanyHero.Platform.Entitlements;

namespace CompanyHero.Modules.Entitlements.Domain;

/// <summary>Bereich der Mitglieder-Navigation, den ein Modul öffnet (Entitlements 2.2: höchstens fünf Bereiche).</summary>
public enum NavigationArea
{
    None = 0,
    Challenges = 1,
    Discover = 2,
    Company = 3,
}

/// <summary>Ein Modul des Katalogs (Entitlements 2.2, A-064): Code, Abhängigkeiten, Bereich, Datenschutzhinweis als Textschlüssel.</summary>
public sealed record ModuleDefinition(string Code, string NameKey, IReadOnlyList<string> Dependencies, NavigationArea Area, string PrivacyHintKey);

/// <summary>Bündel sind Vertriebsvorschläge, keine Entitlements (Entitlements 2.3); die Buchung erzeugt einzelne Entitlements.</summary>
public sealed record BundleDefinition(string Key, IReadOnlyList<string> Modules);

/// <summary>Der Modulkatalog (Entitlements 2, A-064): zwölf Module mit Abhängigkeiten M2 → M1 und M9 → M1.</summary>
public static class ModuleCatalog
{
    /// <summary>Dauer einer Testphase in Tagen (A-065 Voreinstellung).</summary>
    public const int TrialDays = 30;

    /// <summary>Lesbarkeit inaktiver Module für den Tenant-Export (Entitlements 4.3, A-024).</summary>
    public static TimeSpan ExportWindow { get; } = TimeSpan.FromDays(90);

    /// <summary>Voreinstellung des Operators bei Tenant-Anlage (Durchstich 1.1: Kern plus M1; Entitlements 3.3 Quelle „Voreinstellung“).</summary>
    public static IReadOnlyList<string> Preset { get; } = [ModuleCodes.M1Challenges];

    public static IReadOnlyList<ModuleDefinition> Modules { get; } =
    [
        new(ModuleCodes.M1Challenges, "modul.m1", [], NavigationArea.Challenges, "modul.m1.datenschutz"),
        new(ModuleCodes.M2Arena, "modul.m2", [ModuleCodes.M1Challenges], NavigationArea.None, "modul.m2.datenschutz"),
        new(ModuleCodes.M3Movement, "modul.m3", [], NavigationArea.Discover, "modul.m3.datenschutz"),
        new(ModuleCodes.M4Ergonomics, "modul.m4", [], NavigationArea.Discover, "modul.m4.datenschutz"),
        new(ModuleCodes.M5Knowledge, "modul.m5", [], NavigationArea.Discover, "modul.m5.datenschutz"),
        new(ModuleCodes.M6Regeneration, "modul.m6", [], NavigationArea.Discover, "modul.m6.datenschutz"),
        new(ModuleCodes.M7Nutrition, "modul.m7", [], NavigationArea.Discover, "modul.m7.datenschutz"),
        new(ModuleCodes.M8Wearables, "modul.m8", [], NavigationArea.None, "modul.m8.datenschutz"),
        new(ModuleCodes.M9Events, "modul.m9", [ModuleCodes.M1Challenges], NavigationArea.Company, "modul.m9.datenschutz"),
        new(ModuleCodes.M10Channel, "modul.m10", [], NavigationArea.Company, "modul.m10.datenschutz"),
        new(ModuleCodes.M11Languages, "modul.m11", [], NavigationArea.None, "modul.m11.datenschutz"),
        new(ModuleCodes.M12Directory, "modul.m12", [], NavigationArea.None, "modul.m12.datenschutz"),
    ];

    public static IReadOnlyList<BundleDefinition> Bundles { get; } =
    [
        new("einstieg", [ModuleCodes.M1Challenges]),
        new("aktiv", [ModuleCodes.M1Challenges, ModuleCodes.M2Arena, ModuleCodes.M3Movement]),
        new("belegschaft", [ModuleCodes.M1Challenges, ModuleCodes.M2Arena, ModuleCodes.M3Movement, ModuleCodes.M4Ergonomics, ModuleCodes.M11Languages, ModuleCodes.M12Directory]),
        new("redaktion", [ModuleCodes.M5Knowledge, ModuleCodes.M10Channel]),
        new("voll", Modules.Select(m => m.Code).ToList()),
    ];

    public static ModuleDefinition? Find(string code) => Modules.FirstOrDefault(m => string.Equals(m.Code, code, StringComparison.Ordinal));

    public static bool IsKnown(string code) => Find(code) is not null;

    /// <summary>Fehlende Voraussetzungen eines Moduls gegenüber der Menge aktiv nutzbarer Module (Entitlements 4.2 Nr. 3).</summary>
    public static IReadOnlyList<string> MissingDependencies(string code, IReadOnlySet<string> active)
    {
        ArgumentNullException.ThrowIfNull(active);
        var definition = Find(code) ?? throw new ArgumentException($"Unbekanntes Modul '{code}'.", nameof(code));
        return definition.Dependencies.Where(d => !active.Contains(d)).ToList();
    }

    /// <summary>
    /// Bündelvorschlag statt Ablehnung (Entitlements 4.2 Nr. 3, A-066): das gewünschte Modul mit seinen fehlenden
    /// Voraussetzungen in Katalogreihenfolge.
    /// </summary>
    public static IReadOnlyList<string> SuggestBundle(string code, IReadOnlySet<string> active)
    {
        var wanted = new HashSet<string>(MissingDependencies(code, active), StringComparer.Ordinal) { code };
        return Modules.Where(m => wanted.Contains(m.Code)).Select(m => m.Code).ToList();
    }

    /// <summary>Aktive Module, die (auch mittelbar) von <paramref name="code"/> abhängen; sie enden zum selben Termin (Entitlements 4.3).</summary>
    public static IReadOnlyList<string> Dependents(string code, IReadOnlySet<string> active)
    {
        ArgumentNullException.ThrowIfNull(active);
        var result = new List<string>();
        var frontier = new Queue<string>([code]);
        while (frontier.TryDequeue(out var current))
        {
            foreach (var module in Modules.Where(m => m.Dependencies.Contains(current, StringComparer.Ordinal) && active.Contains(m.Code) && !result.Contains(m.Code, StringComparer.Ordinal)))
            {
                result.Add(module.Code);
                frontier.Enqueue(module.Code);
            }
        }

        return result;
    }

    /// <summary>
    /// Navigation der Mitglieder-App ausschließlich aus aktiven Entitlements (Entitlements 2.2, 5; A-064, A-067): Start und Ich
    /// immer; Challenges mit M1; Entdecken mit einem von M3 bis M7; Firma mit M9 oder M10. Kein Eintrag für inaktive Module.
    /// </summary>
    public static IReadOnlyList<string> NavigationAreas(IReadOnlySet<string> active)
    {
        ArgumentNullException.ThrowIfNull(active);
        var areas = new List<string> { "start" };
        if (Modules.Any(m => m.Area == NavigationArea.Challenges && active.Contains(m.Code)))
        {
            areas.Add("challenges");
        }

        if (Modules.Any(m => m.Area == NavigationArea.Discover && active.Contains(m.Code)))
        {
            areas.Add("entdecken");
        }

        if (Modules.Any(m => m.Area == NavigationArea.Company && active.Contains(m.Code)))
        {
            areas.Add("firma");
        }

        areas.Add("ich");
        return areas;
    }
}
