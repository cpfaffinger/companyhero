using CompanyHero.ModuleCatalog;
using CompanyHero.Platform.Data;
using CompanyHero.Platform.Hosting;
using CompanyHero.Platform.Modules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NetArchTest.Rules;

namespace CompanyHero.Architecture.Tests;

/// <summary>
/// A-012, Domänenkarte 1 und 7: Ein Modul besitzt seine Tabellen in seinem Schema und verwendet keinen fremden DbContext.
/// Zusammen mit den Datenbanktests belegt das die Trockenübung „Modul herauslösen“ (Backend 11.9).
/// </summary>
public sealed class ModuleBoundaryTests
{
    private static readonly IReadOnlyList<IModule> Modules = AllModules.Create();

    public static TheoryData<string> ModuleNames() => [.. Modules.Select(m => m.Descriptor.Name)];

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void Jeder_Modulkontext_bildet_ausschliesslich_Tabellen_seines_eigenen_Schemas_ab(string moduleName)
    {
        var module = Modules.Single(m => m.Descriptor.Name == moduleName);
        using var provider = BuildProvider(module);
        var registrations = provider.GetServices<ModuleDbContextRegistration>().ToList();

        var contextTypes = module.GetType().Assembly.GetTypes().Where(t => t.IsSubclassOf(typeof(DbContext))).ToList();
        Assert.Equal(contextTypes.Order(TypeNameComparer.Instance), registrations.Select(r => r.ContextType).Order(TypeNameComparer.Instance));

        foreach (var registration in registrations)
        {
            Assert.Equal(module.Descriptor.Schema, registration.Schema);
            using var scope = provider.CreateScope();
            var context = (DbContext)scope.ServiceProvider.GetRequiredService(registration.ContextType);
            foreach (var entity in context.Model.GetEntityTypes())
            {
                Assert.True(
                    entity.GetSchema() == module.Descriptor.Schema,
                    $"{registration.ContextType.Name}: {entity.ClrType.Name} liegt in Schema '{entity.GetSchema()}', erwartet '{module.Descriptor.Schema}'");
            }
        }
    }

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void Kein_Modul_verwendet_Infrastruktur_oder_Tabellen_eines_anderen_Moduls(string moduleName)
    {
        var module = Modules.Single(m => m.Descriptor.Name == moduleName);
        var foreignInfrastructure = Modules
            .Where(m => m.Descriptor.Name != moduleName)
            .Select(m => $"{m.GetType().Namespace}.Infrastructure")
            .Append(typeof(PlatformDbContext).FullName!)
            .ToArray();

        var result = Types.InAssembly(module.GetType().Assembly)
            .ShouldNot().HaveDependencyOnAny(foreignInfrastructure)
            .GetResult();

        Assert.True(result.IsSuccessful, $"{moduleName}: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void Modulschemata_sind_eindeutig_und_im_Katalog_der_Plattform_bekannt()
    {
        var schemas = Modules.Select(m => m.Descriptor.Schema).ToList();
        Assert.Equal(schemas.Count, schemas.Distinct(StringComparer.Ordinal).Count());
        Assert.All(schemas, s => Assert.Contains(s, ModuleSchemas.All));
        Assert.DoesNotContain(ModuleSchemas.Platform, schemas);
    }

    private static ServiceProvider BuildProvider(IModule module)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPlatformData(new PlatformDataOptions { ConnectionString = "Host=localhost;Database=architektur", EnforceTenantContext = false });
        module.AddModule(services, new ConfigurationBuilder().Build());
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private sealed class TypeNameComparer : IComparer<Type>
    {
        public static TypeNameComparer Instance { get; } = new();

        public int Compare(Type? x, Type? y) => string.CompareOrdinal(x?.FullName, y?.FullName);
    }
}
