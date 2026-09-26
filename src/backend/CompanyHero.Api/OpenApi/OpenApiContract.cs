using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace CompanyHero.Api.OpenApi;

/// <summary>
/// Der verbindliche API-Vertrag (A-009, K12): C#-DTO → tatsächliches JSON → OpenAPI → generierter TypeScript-Client.
/// Die Beschreibung wird aus derselben Endpunktregistrierung erzeugt, die der Host bedient, in eine Datei exportiert
/// (<c>Contracts/openapi.json</c>) und eingecheckt; CI prüft, dass Export und eingecheckte Datei übereinstimmen und dass
/// der generierte Client der eingecheckten Datei entspricht. Der Vertrag ist offline reproduzierbar.
/// </summary>
public static class OpenApiContract
{
    public const string DocumentName = "v1";

    /// <summary>Pfad der eingecheckten Vertragsdatei relativ zur Repository-Wurzel.</summary>
    public const string RelativePath = "src/backend/CompanyHero.Api/Contracts/openapi.json";

    private const string ExportArgument = "--export-openapi";

    public static IServiceCollection AddCompanyHeroOpenApi(this IServiceCollection services)
    {
        // K13: Zahlen sind Zahlen, Strings sind Strings. Ohne diese Einstellung nähme der Serializer Zahlen auch als Strings an,
        // und das Schema müsste jede Ganzzahl als "integer oder string" beschreiben.
        services.ConfigureHttpJsonOptions(o => o.SerializerOptions.NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.Strict);
        services.AddOpenApi(DocumentName, options =>
        {
            options.OpenApiVersion = OpenApiSpecVersion.OpenApi3_1;
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info.Title = "CompanyHero API";
                document.Info.Description = "Verbindlicher Vertrag nach A-009 und K13: Kennungen und Dezimalwerte als Strings, Zeitpunkte mit Offset, Null und fehlendes Feld unterscheidbar.";
                document.Info.Version = "1";
                document.Servers?.Clear();
                return Task.CompletedTask;
            });
            // K13: Nicht-nullbare Eigenschaften sind Pflicht, damit der generierte Client keine optionalen Felder erfindet.
            options.AddSchemaTransformer((schema, context, _) =>
            {
                if (schema.Properties is { Count: > 0 } && context.JsonTypeInfo.Kind == System.Text.Json.Serialization.Metadata.JsonTypeInfoKind.Object)
                {
                    schema.Required ??= new HashSet<string>(StringComparer.Ordinal);
                    foreach (var (name, property) in schema.Properties)
                    {
                        var nullable = property is OpenApiSchema { Type: { } type } && type.HasFlag(JsonSchemaType.Null);
                        if (!nullable)
                        {
                            schema.Required.Add(name);
                        }
                    }
                }

                return Task.CompletedTask;
            });
        });
        return services;
    }

    /// <summary>Erzeugt die OpenAPI-Beschreibung des laufenden Hosts als JSON (OpenAPI 3.1, eingerückt, LF).</summary>
    public static async Task<string> ExportAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider.GetRequiredKeyedService<IOpenApiDocumentProvider>(DocumentName);
        var document = await provider.GetOpenApiDocumentAsync(cancellationToken);
        var json = await document.SerializeAsJsonAsync(OpenApiSpecVersion.OpenApi3_1, cancellationToken);
        // Stabile, formatierte Darstellung: sortierte Schlüssel wären eine Vertragsänderung; nur Einrückung und Zeilenenden werden normiert.
        var node = JsonNode.Parse(json) ?? throw new InvalidOperationException("OpenAPI-Dokument leer.");
        return node.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
    }

    /// <summary>
    /// <c>dotnet run --project src/backend/CompanyHero.Api -- --export-openapi &lt;datei&gt;</c>: startet den Host auf einem
    /// zufälligen lokalen Port (die Endpunkte werden erst beim Start registriert), schreibt den Vertrag, beendet den Host
    /// und liefert <c>true</c>. Es wird keine Anfrage bedient und keine Datenbankverbindung geöffnet.
    /// </summary>
    public static async Task<bool> TryExportAsync(string[] args, WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(app);
        var index = Array.IndexOf(args, ExportArgument);
        if (index < 0)
        {
            return false;
        }

        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"{ExportArgument} erwartet einen Dateipfad.");
        }

        var path = Path.GetFullPath(args[index + 1]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        app.Urls.Clear();
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();
        try
        {
            await File.WriteAllTextAsync(path, await ExportAsync(app.Services, CancellationToken.None), new System.Text.UTF8Encoding(false));
        }
        finally
        {
            await app.StopAsync();
        }

        return true;
    }
}
