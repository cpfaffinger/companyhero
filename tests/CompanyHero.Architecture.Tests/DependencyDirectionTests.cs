using System.Xml.Linq;
using NetArchTest.Rules;

namespace CompanyHero.Architecture.Tests;

/// <summary>
/// Erzwingt die erlaubte Abhängigkeitsrichtung aus der Domänenkarte, Abschnitt 6 (A-012, Domänenkarte 7).
/// Geprüft werden die deklarierten Projektreferenzen der csproj-Dateien, nicht nur die vom Compiler behaltenen.
/// </summary>
public sealed class DependencyDirectionTests
{
    private const string Platform = "CompanyHero.Platform";
    private const string Catalog = "CompanyHero.ModuleCatalog";
    private const string Privacy = "CompanyHero.Modules.Privacy";
    private const string Progress = "CompanyHero.Modules.Progress";
    private const string Challenges = "CompanyHero.Modules.Challenges";
    private const string Feed = "CompanyHero.Modules.Feed";
    private const string Notifications = "CompanyHero.Modules.Notifications";
    private const string Metering = "CompanyHero.Modules.Metering";

    private static readonly string[] Tier1 = ["CompanyHero.Modules.Organisation", "CompanyHero.Modules.Identity", "CompanyHero.Modules.Entitlements", "CompanyHero.Modules.Branding"];
    private static readonly string[] Hosts = ["CompanyHero.Api", "CompanyHero.Worker", "CompanyHero.Migrations"];

    /// <summary>Erlaubte Projektreferenzen je Projekt. Alles andere ist ein Architekturfehler.</summary>
    private static readonly Dictionary<string, string[]> Allowed = new(StringComparer.Ordinal)
    {
        [Platform] = [],
        ["CompanyHero.Modules.Organisation"] = [Platform],
        ["CompanyHero.Modules.Identity"] = [Platform, "CompanyHero.Modules.Organisation"],
        ["CompanyHero.Modules.Entitlements"] = [Platform, "CompanyHero.Modules.Organisation"],
        ["CompanyHero.Modules.Branding"] = [Platform, "CompanyHero.Modules.Organisation"],
        [Privacy] = [Platform, .. Tier1],
        [Progress] = [Platform, .. Tier1, Privacy],
        [Challenges] = [Platform, .. Tier1, Privacy, Progress],
        [Feed] = [Platform, .. Tier1, Privacy, Progress, Challenges],
        // Benachrichtigungen abonnieren Ereignisse von Challenges und Feed; keine Projektreferenz (Domänenkarte 6).
        [Notifications] = [Platform, .. Tier1, Privacy, Progress],
        // Metering abonniert Metering-Ereignisse aller Domänen; Projektreferenzen nur auf Organisation und Entitlements (Domänenkarte 2).
        [Metering] = [Platform, .. Tier1, Privacy],
        [Catalog] = [Platform, .. Tier1, Privacy, Progress, Challenges, Feed, Notifications, Metering],
        ["CompanyHero.Api"] = [Platform, Catalog],
        ["CompanyHero.Worker"] = [Platform, Catalog],
        ["CompanyHero.Migrations"] = [Platform, Catalog],
    };

    public static TheoryData<string> Projects() => [.. Allowed.Keys];

    [Theory]
    [MemberData(nameof(Projects))]
    public void Projektreferenzen_bleiben_innerhalb_der_erlaubten_Richtung(string project)
    {
        var references = DeclaredProjectReferences(project);
        var forbidden = references.Except(Allowed[project], StringComparer.Ordinal).ToList();

        Assert.True(forbidden.Count == 0, $"{project} referenziert unerlaubt: {string.Join(", ", forbidden)}");
    }

    [Fact]
    public void Jedes_Backendprojekt_ist_in_der_Matrix_erfasst()
    {
        var backend = new DirectoryInfo(Path.Combine(RepositoryRoot.Find().FullName, "src", "backend"));
        var projects = backend.GetDirectories().Select(d => d.Name).Where(n => n.StartsWith("CompanyHero.", StringComparison.Ordinal)).ToList();
        var unknown = projects.Except(Allowed.Keys, StringComparer.Ordinal).ToList();

        Assert.True(unknown.Count == 0, $"Nicht in der Abhängigkeitsmatrix: {string.Join(", ", unknown)}");
    }

    [Fact]
    public void Kein_Modul_referenziert_Hosts_oder_Katalog()
    {
        foreach (var module in Allowed.Keys.Where(k => k.StartsWith("CompanyHero.Modules.", StringComparison.Ordinal)))
        {
            var references = DeclaredProjectReferences(module);
            Assert.DoesNotContain(references, r => Hosts.Contains(r, StringComparer.Ordinal) || r == Catalog);
        }
    }

    [Fact]
    public void Fachregeln_haben_keine_Abhaengigkeit_auf_EFCore_oder_AspNetCore()
    {
        // Backend 3.2: Innerhalb eines Moduls sind Fachregeln (Namensraum *.Domain) frei von Infrastruktur.
        var assemblies = typeof(CompanyHero.ModuleCatalog.AllModules).Assembly.GetReferencedAssemblies()
            .Where(a => a.Name!.StartsWith("CompanyHero.Modules.", StringComparison.Ordinal))
            .Select(System.Reflection.Assembly.Load)
            .ToList();

        Assert.NotEmpty(assemblies);

        foreach (var assembly in assemblies)
        {
            var result = Types.InAssembly(assembly)
                .That().ResideInNamespaceEndingWith(".Domain")
                .ShouldNot().HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Npgsql")
                .GetResult();

            Assert.True(result.IsSuccessful, $"{assembly.GetName().Name}: {string.Join(", ", result.FailingTypeNames ?? [])}");
        }
    }

    private static List<string> DeclaredProjectReferences(string project)
    {
        var csproj = Path.Combine(RepositoryRoot.Find().FullName, "src", "backend", project, project + ".csproj");
        var doc = XDocument.Load(csproj);
        return doc.Descendants("ProjectReference")
            .Select(e => Path.GetFileNameWithoutExtension(e.Attribute("Include")!.Value.Replace('\\', '/')))
            .ToList();
    }
}
