using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace CompanyHero.Platform.Configuration;

/// <summary>
/// Liest Anwendungsgeheimnisse beim Start aus dem KV-v2-Speicher von OpenBao (A-029, Betrieb 3.1).
/// Der Zugriffstoken ist ein Bootstrap-Geheimnis und kommt als Datei (Docker-Secret).
/// Schlüssel der Geheimnisse werden in die Konfiguration übernommen; ein doppelter Unterstrich trennt Abschnitte.
/// </summary>
public sealed class OpenBaoConfigurationSource : IConfigurationSource
{
    public required Uri Address { get; init; }

    public required string Token { get; init; }

    public required string Mount { get; init; }

    public required IReadOnlyList<string> Paths { get; init; }

    public IConfigurationProvider Build(IConfigurationBuilder builder) => new OpenBaoConfigurationProvider(this);
}

internal sealed class OpenBaoConfigurationProvider(OpenBaoConfigurationSource source) : ConfigurationProvider
{
    public override void Load()
    {
        using var http = new HttpClient { BaseAddress = source.Address, Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.Add("X-Vault-Token", source.Token);
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        foreach (var path in source.Paths)
        {
            using var response = http.GetAsync(new Uri($"v1/{source.Mount}/data/{path}", UriKind.Relative)).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"OpenBao: Geheimnis {source.Mount}/{path} nicht lesbar (HTTP {(int)response.StatusCode}).");
            }

            using var stream = response.Content.ReadAsStream();
            using var json = JsonDocument.Parse(stream);
            var data = json.RootElement.GetProperty("data").GetProperty("data");
            foreach (var property in data.EnumerateObject())
            {
                var key = property.Name.Replace("__", ConfigurationPath.KeyDelimiter, StringComparison.Ordinal);
                Data[key] = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : property.Value.GetRawText();
            }
        }
    }
}

public static class OpenBaoConfigurationExtensions
{
    /// <summary>
    /// Bindet OpenBao ein, wenn <c>CH_OPENBAO_ADDR</c> und <c>CH_OPENBAO_TOKEN_FILE</c> gesetzt sind.
    /// Ohne diese Variablen (lokale Tests) bleibt die Konfiguration bei Umgebungsvariablen.
    /// </summary>
    public static IConfigurationBuilder AddOpenBaoIfConfigured(this IConfigurationBuilder builder, IReadOnlyList<string> paths)
    {
        var address = Environment.GetEnvironmentVariable("CH_OPENBAO_ADDR");
        var tokenFile = Environment.GetEnvironmentVariable("CH_OPENBAO_TOKEN_FILE");
        if (string.IsNullOrWhiteSpace(address) || string.IsNullOrWhiteSpace(tokenFile))
        {
            return builder;
        }

        var token = File.ReadAllText(tokenFile).Trim();
        var mount = Environment.GetEnvironmentVariable("CH_OPENBAO_KV_MOUNT") ?? "companyhero";
        return builder.Add(new OpenBaoConfigurationSource
        {
            Address = new Uri(address),
            Token = token,
            Mount = mount,
            Paths = paths,
        });
    }
}
