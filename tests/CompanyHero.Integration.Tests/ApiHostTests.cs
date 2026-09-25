using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;

namespace CompanyHero.Integration.Tests;

/// <summary>API-Host mit Laufzeitrolle: Gesundheitsendpunkte für Caddy und Deploy (Betrieb 4), Logs ohne Personenbezug (Betrieb 9.7).</summary>
[Collection(PostgresTests.Name)]
public sealed class ApiHostTests(PostgresFixture pg)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Live_und_Ready_antworten_mit_200_wenn_die_Datenbank_erreichbar_ist()
    {
        using var factory = CreateFactory(pg.AppConnectionString);
        using var client = factory.CreateClient();

        using var live = await client.GetAsync(new Uri("/api/health/live", UriKind.Relative), Ct);
        using var ready = await client.GetAsync(new Uri("/api/health/ready", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
    }

    [Fact]
    public async Task Ready_meldet_503_ohne_Datenbank_Live_bleibt_200()
    {
        using var factory = CreateFactory("Host=127.0.0.1;Port=1;Username=x;Password=y;Database=z;Timeout=1");
        using var client = factory.CreateClient();

        using var live = await client.GetAsync(new Uri("/api/health/live", UriKind.Relative), Ct);
        using var ready = await client.GetAsync(new Uri("/api/health/ready", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
    }

    [Fact]
    public async Task Version_liefert_den_Release_Stand_als_JSON()
    {
        using var factory = CreateFactory(pg.AppConnectionString);
        using var client = factory.CreateClient();

        var version = await client.GetFromJsonAsync<VersionResponse>(new Uri("/api/version", UriKind.Relative), Ct);

        Assert.NotNull(version);
        Assert.False(string.IsNullOrWhiteSpace(version.Version));
    }

    [Fact]
    public async Task Logs_enthalten_weder_Query_noch_Cookie_noch_Authorization()
    {
        var sink = new LogSink();
        using var factory = CreateFactory(pg.AppConnectionString, sink);
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/version?email=max.muster%40example.org&token=GEHEIMESQUERYTOKEN", UriKind.Relative));
        request.Headers.Add("Cookie", "ch_session=GEHEIMERCOOKIEWERT");
        request.Headers.Add("Authorization", "Bearer GEHEIMESBEARERTOKEN");
        using var response = await client.SendAsync(request, Ct);
        using var missing = await client.GetAsync(new Uri("/api/gibt-es-nicht?email=max.muster%40example.org", UriKind.Relative), Ct);

        var all = string.Join("\n", sink.Entries);
        Assert.DoesNotContain("example.org", all, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GEHEIMESQUERYTOKEN", all, StringComparison.Ordinal);
        Assert.DoesNotContain("GEHEIMERCOOKIEWERT", all, StringComparison.Ordinal);
        Assert.DoesNotContain("GEHEIMESBEARERTOKEN", all, StringComparison.Ordinal);
    }

    private static WebApplicationFactory<Program> CreateFactory(string connectionString, LogSink? sink = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:Default", connectionString);
            if (sink is not null)
            {
                b.ConfigureLogging(l => l.AddProvider(new LogSinkProvider(sink)));
            }
        });

    private sealed record VersionResponse(string Version);

    private sealed class LogSink
    {
        public List<string> Entries { get; } = [];
    }

    private sealed class LogSinkProvider(LogSink sink) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new SinkLogger(sink, categoryName);

        public void Dispose()
        {
        }

        private sealed class SinkLogger(LogSink sink, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (sink.Entries)
                {
                    sink.Entries.Add($"{category}: {formatter(state, exception)} | {state} | {exception}");
                }
            }
        }
    }
}
