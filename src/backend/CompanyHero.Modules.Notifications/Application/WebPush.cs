using System.Buffers.Binary;
using System.Buffers.Text;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CompanyHero.Modules.Notifications.Domain;
using Microsoft.Extensions.Options;

namespace CompanyHero.Modules.Notifications.Application;

/// <summary>
/// Verschlüsselung nach Web-Push-Standard (RFC 8291 mit Content-Encoding <c>aes128gcm</c> nach RFC 8188): ECDH auf P-256 mit
/// dem Schlüssel des Abonnements, HKDF-SHA256, AES-128-GCM. Der Push-Dienst sieht nur die verschlüsselte Nutzlast.
/// Die Entschlüsselung dient Tests und Werkzeugen; der Browser entschlüsselt selbst.
/// </summary>
public static class WebPushCrypto
{
    private const int RecordSize = 4096;

    public static byte[] Encrypt(ReadOnlySpan<byte> plaintext, string clientPublicKeyBase64Url, string authSecretBase64Url)
    {
        var clientPublic = Base64Url.DecodeFromChars(clientPublicKeyBase64Url);
        var auth = Base64Url.DecodeFromChars(authSecretBase64Url);
        using var server = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var serverPublic = ExportRaw(server.PublicKey);
        var salt = RandomNumberGenerator.GetBytes(16);
        var (key, nonce) = DeriveKeyAndNonce(server, clientPublic, auth, salt, clientPublic, serverPublic);

        var padded = new byte[plaintext.Length + 1];
        plaintext.CopyTo(padded);
        padded[^1] = 0x02; // Trennzeichen des letzten Datensatzes (RFC 8188 Abschnitt 2)
        var ciphertext = new byte[padded.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(key, 16))
        {
            aes.Encrypt(nonce, padded, ciphertext, tag);
        }

        var body = new byte[16 + 4 + 1 + serverPublic.Length + ciphertext.Length + tag.Length];
        var offset = 0;
        salt.CopyTo(body, offset);
        offset += 16;
        BinaryPrimitives.WriteUInt32BigEndian(body.AsSpan(offset, 4), RecordSize);
        offset += 4;
        body[offset++] = (byte)serverPublic.Length;
        serverPublic.CopyTo(body, offset);
        offset += serverPublic.Length;
        ciphertext.CopyTo(body, offset);
        offset += ciphertext.Length;
        tag.CopyTo(body, offset);
        return body;
    }

    /// <summary>Gegenstück für Tests: entschlüsselt mit dem privaten Schlüssel des Abonnements.</summary>
    public static byte[] Decrypt(ReadOnlySpan<byte> body, ECDiffieHellman clientKey, string authSecretBase64Url)
    {
        ArgumentNullException.ThrowIfNull(clientKey);
        var auth = Base64Url.DecodeFromChars(authSecretBase64Url);
        var salt = body[..16].ToArray();
        var keyLength = body[20];
        var serverPublic = body.Slice(21, keyLength).ToArray();
        var clientPublic = ExportRaw(clientKey.PublicKey);
        var (key, nonce) = DeriveKeyAndNonce(clientKey, serverPublic, auth, salt, clientPublic, serverPublic);
        var payload = body[(21 + keyLength)..];
        var ciphertext = payload[..^16];
        var tag = payload[^16..];
        var plain = new byte[ciphertext.Length];
        using (var aes = new AesGcm(key, 16))
        {
            aes.Decrypt(nonce, ciphertext, tag, plain);
        }

        var end = Array.LastIndexOf(plain, (byte)0x02);
        return end < 0 ? plain : plain[..end];
    }

    public static byte[] ExportRaw(ECDiffieHellmanPublicKey publicKey)
    {
        ArgumentNullException.ThrowIfNull(publicKey);
        var p = publicKey.ExportParameters();
        var raw = new byte[65];
        raw[0] = 0x04;
        p.Q.X!.CopyTo(raw, 1);
        p.Q.Y!.CopyTo(raw, 33);
        return raw;
    }

    public static ECDiffieHellmanPublicKey ImportRaw(ReadOnlySpan<byte> raw)
    {
        if (raw.Length != 65 || raw[0] != 0x04)
        {
            throw new ArgumentException("Unkomprimierter P-256-Punkt (65 Byte) erwartet.", nameof(raw));
        }

        using var ecdh = ECDiffieHellman.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = raw[1..33].ToArray(), Y = raw[33..].ToArray() } });
        return ecdh.PublicKey;
    }

    private static (byte[] Key, byte[] Nonce) DeriveKeyAndNonce(ECDiffieHellman own, byte[] peerPublic, byte[] auth, byte[] salt, byte[] clientPublic, byte[] serverPublic)
    {
        using var peer = ImportRaw(peerPublic);
        var shared = own.DeriveRawSecretAgreement(peer);
        var info = Concat("WebPush: info\0"u8.ToArray(), clientPublic, serverPublic);
        var ikm = HKDF.DeriveKey(HashAlgorithmName.SHA256, shared, 32, auth, info);
        var key = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 16, salt, "Content-Encoding: aes128gcm\0"u8.ToArray());
        var nonce = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 12, salt, "Content-Encoding: nonce\0"u8.ToArray());
        return (key, nonce);
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var result = new byte[parts.Sum(p => p.Length)];
        var offset = 0;
        foreach (var part in parts)
        {
            part.CopyTo(result, offset);
            offset += part.Length;
        }

        return result;
    }
}

/// <summary>VAPID (RFC 8292): signiertes JWT mit ES256 je Push-Dienst-Origin; der öffentliche Schlüssel geht an die App für das Abonnement.</summary>
public sealed class VapidSigner : IDisposable
{
    private readonly ECDsa _key;

    public VapidSigner(string privateKeyPem, string subject)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(privateKeyPem);
        _key = ECDsa.Create();
        _key.ImportFromPem(privateKeyPem);
        var p = _key.ExportParameters(false);
        var raw = new byte[65];
        raw[0] = 0x04;
        p.Q.X!.CopyTo(raw, 1);
        p.Q.Y!.CopyTo(raw, 33);
        PublicKey = Base64Url.EncodeToString(raw);
        Subject = subject;
    }

    /// <summary>Öffentlicher Schlüssel als Base64url des unkomprimierten Punkts (<c>applicationServerKey</c>).</summary>
    public string PublicKey { get; }

    public string Subject { get; }

    public static string GeneratePrivateKeyPem()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return key.ExportECPrivateKeyPem();
    }

    public string Authorization(Uri endpoint, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        var header = Base64Url.EncodeToString(Encoding.UTF8.GetBytes("{\"typ\":\"JWT\",\"alg\":\"ES256\"}"));
        var claims = JsonSerializer.Serialize(new { aud = endpoint.GetLeftPart(UriPartial.Authority), exp = now.AddHours(12).ToUnixTimeSeconds(), sub = Subject });
        var payload = Base64Url.EncodeToString(Encoding.UTF8.GetBytes(claims));
        var signingInput = Encoding.ASCII.GetBytes(header + "." + payload);
        var signature = _key.SignData(signingInput, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return $"vapid t={header}.{payload}.{Base64Url.EncodeToString(signature)}, k={PublicKey}";
    }

    public void Dispose() => _key.Dispose();
}

/// <summary>Zugriff auf das VAPID-Schlüsselpaar; ohne Konfiguration ist Push nicht verfügbar und die API meldet das der App.</summary>
public interface IVapidKeys
{
    bool Configured { get; }

    string PublicKey { get; }

    string Authorization(Uri endpoint, DateTimeOffset now);
}

internal sealed class VapidKeys : IVapidKeys, IDisposable
{
    private readonly VapidSigner? _signer;

    public VapidKeys(IOptions<NotificationsOptions> options)
    {
        var vapid = options.Value.Vapid;
        _signer = vapid.Configured ? new VapidSigner(vapid.PrivateKeyPem, vapid.Subject) : null;
    }

    public bool Configured => _signer is not null;

    public string PublicKey => _signer?.PublicKey ?? string.Empty;

    public string Authorization(Uri endpoint, DateTimeOffset now) => (_signer ?? throw new InvalidOperationException("VAPID ist nicht konfiguriert.")).Authorization(endpoint, now);

    public void Dispose() => _signer?.Dispose();
}

/// <summary>Eine Nachricht an den Push-Dienst: Endpunkt, verschlüsselter Körper, Kopfzeilen; nie Inhalt im Klartext.</summary>
public sealed record PushMessage(Uri Endpoint, byte[] Body, string Authorization, int TimeToLiveSeconds, string Topic, string Urgency);

public sealed record PushSendResult(int StatusCode);

/// <summary>Transport zum Push-Dienst des Browserherstellers; Tests ersetzen ihn.</summary>
public interface IWebPushTransport
{
    Task<PushSendResult> SendAsync(PushMessage message, CancellationToken cancellationToken);
}

internal sealed class HttpWebPushTransport(IHttpClientFactory httpClientFactory) : IWebPushTransport
{
    public const string ClientName = "webpush";

    public async Task<PushSendResult> SendAsync(PushMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        using var client = httpClientFactory.CreateClient(ClientName);
        using var request = new HttpRequestMessage(HttpMethod.Post, message.Endpoint);
        request.Content = new ByteArrayContent(message.Body);
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        request.Content.Headers.ContentEncoding.Add("aes128gcm");
        request.Headers.TryAddWithoutValidation("Authorization", message.Authorization);
        request.Headers.TryAddWithoutValidation("TTL", message.TimeToLiveSeconds.ToString(CultureInfo.InvariantCulture));
        request.Headers.TryAddWithoutValidation("Topic", message.Topic);
        request.Headers.TryAddWithoutValidation("Urgency", message.Urgency);
        using var response = await client.SendAsync(request, cancellationToken);
        return new PushSendResult((int)response.StatusCode);
    }
}

/// <summary>Deutung der Antwort des Push-Dienstes (Benachrichtigungen 3.1): 404 und 410 löschen das Abonnement.</summary>
public static class PushResponses
{
    public static bool Accepted(int status) => status is >= 200 and < 300;

    public static bool SubscriptionGone(int status) => status is (int)HttpStatusCode.NotFound or (int)HttpStatusCode.Gone;
}
