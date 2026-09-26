using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CompanyHero.Modules.Identity.Application.Sessions;
using CompanyHero.Modules.Identity.Infrastructure.Http;
using CompanyHero.Modules.Identity.Infrastructure.Oidc;
using CompanyHero.Platform.Messaging;
using CompanyHero.Platform.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace CompanyHero.Integration.Tests;

/// <summary>Cookie-Ablage eines Testbrowsers: nimmt Set-Cookie an, sendet Cookie; die Tests steuern damit auch Instanzwechsel.</summary>
public sealed class CookieJar
{
    private readonly Dictionary<string, string> _cookies = new(StringComparer.Ordinal);

    public string? this[string name] => _cookies.GetValueOrDefault(name);

    public IReadOnlyDictionary<string, string> All => _cookies;

    public void Absorb(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values))
        {
            return;
        }

        foreach (var raw in values)
        {
            var parts = raw.Split(';', StringSplitOptions.TrimEntries);
            var kv = parts[0].Split('=', 2);
            var name = kv[0];
            var value = kv.Length > 1 ? kv[1] : string.Empty;
            var expired = parts.Skip(1).Any(p => p.StartsWith("expires=", StringComparison.OrdinalIgnoreCase) && DateTimeOffset.TryParse(p[8..], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at) && at < DateTimeOffset.UtcNow.AddYears(-1));
            if (string.IsNullOrEmpty(value) || expired || parts.Skip(1).Any(p => p.Equals("max-age=0", StringComparison.OrdinalIgnoreCase)))
            {
                _cookies.Remove(name);
            }
            else
            {
                _cookies[name] = value;
            }
        }
    }

    public void Apply(HttpRequestMessage request)
    {
        if (_cookies.Count > 0)
        {
            request.Headers.Add("Cookie", string.Join("; ", _cookies.Select(c => $"{c.Key}={c.Value}")));
        }

        if (_cookies.TryGetValue(SessionCookies.Csrf, out var csrf) && request.Method != HttpMethod.Get)
        {
            request.Headers.Remove(SessionCookies.CsrfHeader);
            request.Headers.Add(SessionCookies.CsrfHeader, csrf);
        }
    }
}

/// <summary>Testbrowser gegen eine API-Instanz: Cookies, CSRF-Header, kein automatisches Folgen von Weiterleitungen.</summary>
public sealed class TestBrowser(HttpClient client) : IDisposable
{
    public CookieJar Cookies { get; } = new();

    public HttpClient Client => client;

    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        Cookies.Apply(request);
        var response = await client.SendAsync(request, ct);
        Cookies.Absorb(response);
        return response;
    }

    public Task<HttpResponseMessage> GetAsync(string path, CancellationToken ct) => SendAsync(HttpMethod.Get, path, null, ct);

    public Task<HttpResponseMessage> PostAsync(string path, object? body, CancellationToken ct) => SendAsync(HttpMethod.Post, path, body, ct);

    public Task<HttpResponseMessage> PutAsync(string path, object? body, CancellationToken ct) => SendAsync(HttpMethod.Put, path, body, ct);

    public Task<HttpResponseMessage> DeleteAsync(string path, CancellationToken ct) => SendAsync(HttpMethod.Delete, path, null, ct);

    public async Task<T> PostJsonAsync<T>(string path, object? body, CancellationToken ct, HttpStatusCode expected = HttpStatusCode.OK)
    {
        using var response = await PostAsync(path, body, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        Assert.True(response.StatusCode == expected, $"{path}: {response.StatusCode} statt {expected}: {text}");
        return JsonSerializer.Deserialize<T>(text, Json)!;
    }

    public async Task<T> GetJsonAsync<T>(string path, CancellationToken ct)
    {
        using var response = await GetAsync(path, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{path}: {response.StatusCode}: {text}");
        return JsonSerializer.Deserialize<T>(text, Json)!;
    }

    public static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web);

    public void Dispose() => client.Dispose();
}

/// <summary>Zeichnet Konto-Nachrichten auf, statt sie zu senden (Magic-Link, Rollencode).</summary>
public sealed class CapturingMailSender : IAccountMailSender
{
    public List<AccountMail> Sent { get; } = [];

    public Task SendAsync(AccountMail mail, CancellationToken cancellationToken)
    {
        lock (Sent)
        {
            Sent.Add(mail);
        }

        return Task.CompletedTask;
    }

    public string? LastTokenFor(string recipient, string key)
    {
        lock (Sent)
        {
            var link = Sent.LastOrDefault(m => string.Equals(m.Recipient, recipient, StringComparison.OrdinalIgnoreCase))?.Link;
            return link is null ? null : System.Web.HttpUtility.ParseQueryString(new Uri(link).Query)[key];
        }
    }
}

/// <summary>
/// In-Process-OpenID-Connect-Anbieter (Durchstich 4: externe Anbieter in Tests über lokalen Anbieter): mehrere Issuer unter
/// einem Testserver, Discovery-Dokument, Authorization Code Flow mit PKCE (S256), RS256-signierte ID-Tokens mit Name und
/// E-Mail als Claims, die im Datenbestand nie ankommen dürfen. Ein absichtlich fehlerhafter Issuer prüft die Discovery-Validierung.
/// </summary>
public sealed class FakeIdentityProvider : IAsyncDisposable
{
    public const string Host = "https://idp.test";

    private readonly RSA _key = RSA.Create(2048);
    private readonly Dictionary<string, (string Issuer, string Subject, string Nonce, string RedirectUri, string Challenge, string ClientId)> _codes = new(StringComparer.Ordinal);
    private WebApplication? _app;

    public string KeyId { get; } = Guid.NewGuid().ToString("N");

    public int TokenRequests { get; private set; }

    public static string Issuer(string name) => $"{Host}/{name}";

    public HttpMessageHandler Handler => _app!.GetTestServer().CreateHandler();

    public async Task StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        var app = builder.Build();

        app.MapGet("/{issuer}/.well-known/openid-configuration", (string issuer) =>
        {
            var baseUrl = Issuer(issuer);
            var broken = issuer == "broken";
            return Results.Json(new
            {
                issuer = broken ? Host + "/someone-else" : baseUrl,
                authorization_endpoint = baseUrl + "/authorize",
                token_endpoint = baseUrl + "/token",
                jwks_uri = baseUrl + "/jwks",
                response_types_supported = new[] { "code" },
                subject_types_supported = new[] { "public" },
                id_token_signing_alg_values_supported = new[] { "RS256" },
                code_challenge_methods_supported = broken ? new[] { "plain" } : new[] { "S256" },
                scopes_supported = new[] { "openid", "profile", "email" },
                token_endpoint_auth_methods_supported = new[] { "client_secret_post", "client_secret_basic" },
                claims_supported = new[] { "sub", "name", "email" },
            });
        });

        app.MapGet("/{issuer}/jwks", (string issuer) =>
        {
            var p = _key.ExportParameters(false);
            return Results.Json(new { keys = new[] { new { kty = "RSA", use = "sig", kid = KeyId, alg = "RS256", n = Base64UrlEncoder.Encode(p.Modulus), e = Base64UrlEncoder.Encode(p.Exponent) } } });
        });

        // Der „Login“ am Anbieter: der Test benennt das Subjekt über sub=; der Anbieter stellt den Code aus und leitet zurück.
        app.MapGet("/{issuer}/authorize", (string issuer, HttpRequest request) =>
        {
            var q = request.Query;
            if (q["response_type"] != "code" || string.IsNullOrEmpty(q["code_challenge"]) || q["code_challenge_method"] != "S256" || string.IsNullOrEmpty(q["nonce"]) || string.IsNullOrEmpty(q["state"]))
            {
                return Results.BadRequest("Kein Code-Flow mit PKCE und Nonce.");
            }

            var code = Guid.NewGuid().ToString("N");
            lock (_codes)
            {
                _codes[code] = (Issuer(issuer), q["sub"].ToString() is { Length: > 0 } s ? s : "unbekannt", q["nonce"]!, q["redirect_uri"]!, q["code_challenge"]!, q["client_id"]!);
            }

            return Results.Redirect($"{q["redirect_uri"]}?code={code}&state={Uri.EscapeDataString(q["state"]!)}");
        });

        app.MapPost("/{issuer}/token", async (string issuer, HttpRequest request) =>
        {
            var form = await request.ReadFormAsync();
            TokenRequests++;
            (string Issuer, string Subject, string Nonce, string RedirectUri, string Challenge, string ClientId) entry;
            lock (_codes)
            {
                if (!_codes.Remove(form["code"].ToString(), out entry))
                {
                    return Results.BadRequest(new { error = "invalid_grant" });
                }
            }

            var verifier = form["code_verifier"].ToString();
            var expected = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
            if (expected != entry.Challenge || form["grant_type"] != "authorization_code" || form["redirect_uri"] != entry.RedirectUri)
            {
                return Results.BadRequest(new { error = "invalid_grant", error_description = "PKCE oder redirect_uri passen nicht" });
            }

            var clientId = form["client_id"].ToString();
            if (string.IsNullOrEmpty(clientId) && request.Headers.Authorization.ToString().StartsWith("Basic ", StringComparison.Ordinal))
            {
                clientId = Encoding.UTF8.GetString(Convert.FromBase64String(request.Headers.Authorization.ToString()[6..])).Split(':')[0];
                clientId = Uri.UnescapeDataString(clientId);
            }

            var now = DateTimeOffset.UtcNow;
            var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
            {
                Issuer = entry.Issuer,
                Audience = clientId,
                IssuedAt = now.UtcDateTime,
                NotBefore = now.UtcDateTime,
                Expires = now.AddMinutes(5).UtcDateTime,
                Claims = new Dictionary<string, object>
                {
                    ["sub"] = entry.Subject,
                    ["nonce"] = entry.Nonce,
                    ["name"] = "Klara Klarname " + entry.Subject,
                    ["email"] = entry.Subject + "@anbieter.example",
                    ["preferred_username"] = entry.Subject,
                },
                SigningCredentials = new SigningCredentials(new RsaSecurityKey(_key) { KeyId = KeyId }, SecurityAlgorithms.RsaSha256),
            });
            return Results.Json(new { id_token = token, access_token = Guid.NewGuid().ToString("N"), token_type = "Bearer", expires_in = 300 });
        });

        await app.StartAsync();
        _app = app;
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }

        _key.Dispose();
    }
}

/// <summary>Rückkanal der API zum In-Process-Anbieter: Discovery, Token und JWKS laufen über den Testserver.</summary>
public sealed class TestOidcBackchannel(FakeIdentityProvider idp) : IOidcBackchannel
{
    public HttpMessageHandler CreateHandler() => idp.Handler;
}

/// <summary>
/// Software-Authenticator für WebAuthn (Durchstich 4: serverseitige Referenzvektoren): ES256 auf P-256, Attestation „none“,
/// Assertion mit Zähler. Er erzeugt genau die JSON-Antworten, die <c>PublicKeyCredential.toJSON()</c> im Browser liefert.
/// </summary>
public sealed class SoftwareAuthenticator : IDisposable
{
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private byte[] _userHandle = [];
    private uint _counter;

    public byte[] CredentialId { get; } = RandomNumberGenerator.GetBytes(32);

    public string Origin { get; init; } = "https://localhost";

    /// <summary>Ein zweites Gerät erhält denselben Schlüssel nie; für Klon-Tests kann der Zähler zurückgesetzt werden.</summary>
    public void RewindCounter(uint to) => _counter = to;

    public JsonElement CreateAttestation(JsonElement options)
    {
        var challenge = Base64UrlEncoder.DecodeBytes(options.GetProperty("challenge").GetString()!);
        var rpId = options.GetProperty("rp").GetProperty("id").GetString()!;
        _userHandle = Base64UrlEncoder.DecodeBytes(options.GetProperty("user").GetProperty("id").GetString()!);

        var clientData = ClientData("webauthn.create", challenge);
        var p = _key.ExportParameters(false);
        var cose = Cbor.Map(
            (Cbor.Int(1), Cbor.Int(2)),          // kty: EC2
            (Cbor.Int(3), Cbor.Int(-7)),         // alg: ES256
            (Cbor.Int(-1), Cbor.Int(1)),         // crv: P-256
            (Cbor.Int(-2), Cbor.Bytes(p.Q.X!)),
            (Cbor.Int(-3), Cbor.Bytes(p.Q.Y!)));
        var authData = AuthData(rpId, flags: 0x45, includeCredential: true, cose);
        var attestation = Cbor.Map(
            (Cbor.Text("fmt"), Cbor.Text("none")),
            (Cbor.Text("attStmt"), Cbor.Map()),
            (Cbor.Text("authData"), Cbor.Bytes(authData)));

        return JsonSerializer.SerializeToElement(new
        {
            id = Base64UrlEncoder.Encode(CredentialId),
            rawId = Base64UrlEncoder.Encode(CredentialId),
            type = "public-key",
            authenticatorAttachment = "platform",
            clientExtensionResults = new { },
            response = new
            {
                clientDataJSON = Base64UrlEncoder.Encode(clientData),
                attestationObject = Base64UrlEncoder.Encode(attestation),
                transports = new[] { "internal" },
            },
        });
    }

    public JsonElement CreateAssertion(JsonElement options, byte[]? userHandle = null)
    {
        var challenge = Base64UrlEncoder.DecodeBytes(options.GetProperty("challenge").GetString()!);
        var rpId = options.GetProperty("rpId").GetString()!;
        var clientData = ClientData("webauthn.get", challenge);
        _counter++;
        var authData = AuthData(rpId, flags: 0x05, includeCredential: false, []);
        var signature = _key.SignData([.. authData, .. SHA256.HashData(clientData)], HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        return JsonSerializer.SerializeToElement(new
        {
            id = Base64UrlEncoder.Encode(CredentialId),
            rawId = Base64UrlEncoder.Encode(CredentialId),
            type = "public-key",
            authenticatorAttachment = "platform",
            clientExtensionResults = new { },
            response = new
            {
                clientDataJSON = Base64UrlEncoder.Encode(clientData),
                authenticatorData = Base64UrlEncoder.Encode(authData),
                signature = Base64UrlEncoder.Encode(signature),
                userHandle = Base64UrlEncoder.Encode(userHandle ?? _userHandle),
            },
        });
    }

    private byte[] ClientData(string type, byte[] challenge) =>
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { type, challenge = Base64UrlEncoder.Encode(challenge), origin = Origin, crossOrigin = false }));

    private byte[] AuthData(string rpId, byte flags, bool includeCredential, byte[] cose)
    {
        var buffer = new List<byte>(SHA256.HashData(Encoding.UTF8.GetBytes(rpId))) { flags };
        var counter = BitConverter.GetBytes(_counter);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(counter);
        }

        buffer.AddRange(counter);
        if (includeCredential)
        {
            buffer.AddRange(new byte[16]);
            buffer.Add((byte)(CredentialId.Length >> 8));
            buffer.Add((byte)(CredentialId.Length & 0xFF));
            buffer.AddRange(CredentialId);
            buffer.AddRange(cose);
        }

        return [.. buffer];
    }

    public void Dispose() => _key.Dispose();

    /// <summary>Minimaler CBOR-Kodierer (RFC 8949) für Attestation und COSE-Schlüssel.</summary>
    private static class Cbor
    {
        public static byte[] Int(long value) => value >= 0 ? Head(0, (ulong)value) : Head(1, (ulong)(-1 - value));

        public static byte[] Bytes(byte[] value) => [.. Head(2, (ulong)value.Length), .. value];

        public static byte[] Text(string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            return [.. Head(3, (ulong)bytes.Length), .. bytes];
        }

        public static byte[] Map(params (byte[] Key, byte[] Value)[] entries)
        {
            var result = new List<byte>(Head(5, (ulong)entries.Length));
            foreach (var (key, value) in entries)
            {
                result.AddRange(key);
                result.AddRange(value);
            }

            return [.. result];
        }

        private static byte[] Head(byte major, ulong value)
        {
            var type = (byte)(major << 5);
            return value switch
            {
                < 24 => [(byte)(type | value)],
                < 0x100 => [(byte)(type | 24), (byte)value],
                < 0x10000 => [(byte)(type | 25), (byte)(value >> 8), (byte)value],
                _ => [(byte)(type | 26), (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value],
            };
        }
    }
}

/// <summary>Sitzungen für Testpersonen: über den Sitzungsdienst ausgestellt, wie es ein Anmeldeweg täte.</summary>
public static class TestSessions
{
    public static Task<IssuedSession> IssueAsync(IServiceProvider services, TenantId tenant, PersonId person, CancellationToken ct) =>
        services.GetRequiredService<ITenantScopeFactory>().RunAsync(TenantContext.ForTenant(tenant), (sp, c) => sp.GetRequiredService<ISessionService>().IssueAsync(person, c), ct);

    public static void Apply(HttpClient client, IssuedSession session)
    {
        client.DefaultRequestHeaders.Remove("Cookie");
        client.DefaultRequestHeaders.Remove(SessionCookies.CsrfHeader);
        client.DefaultRequestHeaders.Add("Cookie", $"{SessionCookies.Session}={session.Token}; {SessionCookies.Csrf}={session.CsrfToken}");
        client.DefaultRequestHeaders.Add(SessionCookies.CsrfHeader, session.CsrfToken);
    }

    public static JsonNode Node(JsonElement element) => JsonNode.Parse(element.GetRawText())!;

    public static MediaTypeHeaderValue JsonMedia { get; } = new("application/json");
}
