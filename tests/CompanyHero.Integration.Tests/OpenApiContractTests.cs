using System.Text.Json;
using System.Text.Json.Nodes;
using CompanyHero.Api.OpenApi;

namespace CompanyHero.Integration.Tests;

/// <summary>
/// A-009, K12, K13: Der eingecheckte Vertrag ist der Export der laufenden API (offline reproduzierbar); Kennungen und
/// Dezimalwerte sind Strings, Zeitpunkte tragen <c>date-time</c>, Ganzzahlen sind reine Ganzzahlen, jede Operation hat
/// eine typisierte Erfolgsantwort. Eine Vertragsänderung ohne neuen Export wird hier rot, bevor der Client generiert wird.
/// </summary>
[Collection(PostgresTests.Name)]
public sealed class OpenApiContractTests(PostgresFixture pg)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string ContractPath => Path.Combine(RepositoryRoot.Find().FullName, OpenApiContract.RelativePath.Replace('/', Path.DirectorySeparatorChar));

    [Fact]
    public async Task Eingecheckter_Vertrag_entspricht_dem_Export_der_laufenden_API()
    {
        var exported = await OpenApiContract.ExportAsync(pg.Api.Services, Ct);
        var checkedIn = (await File.ReadAllTextAsync(ContractPath, Ct)).Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.True(exported == checkedIn, $"Der Vertrag {OpenApiContract.RelativePath} ist veraltet. Neu exportieren: npm run api:export (src/frontend) und den Client mit npm run api:generate regenerieren.");
    }

    [Fact]
    public async Task Vertrag_folgt_K13_Kennungen_und_Dezimalwerte_als_Strings_Zeitpunkte_mit_Format_Ganzzahlen_rein()
    {
        var document = JsonNode.Parse(await OpenApiContract.ExportAsync(pg.Api.Services, Ct))!.AsObject();
        Assert.Equal("3.1.1", document["openapi"]!.GetValue<string>());
        var schemas = document["components"]!["schemas"]!.AsObject();
        var problems = new List<string>();

        foreach (var (schemaName, schemaNode) in schemas)
        {
            if (schemaNode!["properties"] is not JsonObject properties)
            {
                continue;
            }

            var required = schemaNode["required"]?.AsArray().Select(r => r!.GetValue<string>()).ToHashSet(StringComparer.Ordinal) ?? [];
            foreach (var (name, property) in properties)
            {
                var types = Types(property!);
                if (name.EndsWith("Id", StringComparison.Ordinal) && !types.SetEquals(["string"]))
                {
                    problems.Add($"{schemaName}.{name}: Kennung muss string sein, ist {string.Join("|", types)}");
                }

                if ((name is "total" or "value" or "target" or "quantity") && !types.SetEquals(["string"]))
                {
                    problems.Add($"{schemaName}.{name}: Dezimalwert muss string sein, ist {string.Join("|", types)}");
                }

                if (types.Contains("number"))
                {
                    problems.Add($"{schemaName}.{name}: binäre Gleitkommazahl im Vertrag (K13)");
                }

                if (types.Contains("integer") && types.Count != 1)
                {
                    problems.Add($"{schemaName}.{name}: Ganzzahl muss rein sein, ist {string.Join("|", types)}");
                }

                if ((name.EndsWith("At", StringComparison.Ordinal) || name.EndsWith("Am", StringComparison.Ordinal)) && property!["format"]?.GetValue<string>() != "date-time")
                {
                    problems.Add($"{schemaName}.{name}: Zeitpunkt ohne Format date-time");
                }

                if (!required.Contains(name) && !schemaName.Contains("ProblemDetails", StringComparison.Ordinal))
                {
                    problems.Add($"{schemaName}.{name}: nicht als Pflichtfeld markiert; Null und fehlendes Feld wären nicht unterscheidbar");
                }
            }
        }

        // Binärantworten (Icons, Aushang-PDF, Verbrauchsexport als CSV) und Weiterleitungen (OIDC-Start) tragen kein JSON-Schema.
        string[] untypedAllowed = ["/api/branding/tenants/{tenantId}/icons/{name}", "/api/auth/oidc/{key}/start", "/api/notifications/aushang", "/api/billing/usage/export"];
        foreach (var (path, item) in document["paths"]!.AsObject())
        {
            foreach (var (method, operation) in item!.AsObject())
            {
                if (untypedAllowed.Contains(path, StringComparer.Ordinal))
                {
                    continue;
                }

                var responses = operation!["responses"]!.AsObject();
                var success = responses.FirstOrDefault(r => r.Key.StartsWith('2'));
                if (success.Value is null)
                {
                    problems.Add($"{method.ToUpperInvariant()} {path}: keine Erfolgsantwort");
                    continue;
                }

                var content = success.Value["content"]?.AsObject();
                // 202 (angenommen) und 204 (kein Inhalt) haben nach HTTP keinen Körper; der Client erhält den Status.
                if (success.Key is not ("202" or "204") && (content is null || content.Count == 0))
                {
                    problems.Add($"{method.ToUpperInvariant()} {path}: Erfolgsantwort ohne Schema; der generierte Client wäre untypisiert");
                }
            }
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    private static HashSet<string> Types(JsonNode property)
    {
        if (property["$ref"] is not null)
        {
            return ["$ref"];
        }

        return property["type"] switch
        {
            JsonArray array => array.Select(t => t!.GetValue<string>()).Where(t => t != "null").ToHashSet(StringComparer.Ordinal),
            JsonValue value => [value.GetValue<string>()],
            _ => [],
        };
    }
}
