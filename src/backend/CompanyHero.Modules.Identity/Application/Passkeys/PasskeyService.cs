using System.Globalization;
using System.Text.Json;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace CompanyHero.Modules.Identity.Application.Passkeys;

/// <summary>Eine begonnene WebAuthn-Zeremonie: Optionen für den Browser und ein geschützter Zustand, der die Antwort bindet.</summary>
public sealed record PasskeyCeremony(JsonElement Options, string State);

/// <summary>Ergebnis einer geprüften Registrierung: das, was gespeichert wird (Zugang 3.1), nie mehr.</summary>
public sealed record VerifiedPasskey(byte[] CredentialId, byte[] PublicKey, uint SignCount, Guid AaGuid);

/// <summary>Gespeicherter Passkey, wie ihn die Anmeldung zum Prüfen braucht.</summary>
public sealed record StoredPasskey(byte[] PublicKey, uint SignCount, byte[] UserHandle);

public sealed class PasskeyRejectedException(string message) : InvalidOperationException(message);

/// <summary>
/// WebAuthn über Fido2NetLib (A-111): Registrierung und Anmeldung mit Passkeys. Der Zustand der Zeremonie (Optionen mit
/// Challenge, Benutzerkennung, Ablauf) wird mit Data Protection geschützt an den Client gegeben und mit der Antwort
/// zurückerwartet; so bleibt der Server zustandslos und die Zeremonie ist auf jeder Instanz abschließbar (A-007 Betrieb).
/// </summary>
public interface IPasskeyService
{
    PasskeyCeremony BeginRegistration(byte[] userHandle, string displayName, IReadOnlyList<byte[]> excludeCredentialIds);

    /// <summary>Prüft Attestation, Challenge, Origin und RP-ID; liefert die zu speichernden Daten und die Benutzerkennung der Zeremonie.</summary>
    Task<(VerifiedPasskey Passkey, byte[] UserHandle)> CompleteRegistrationAsync(string state, JsonElement credential, CancellationToken cancellationToken);

    PasskeyCeremony BeginAssertion();

    /// <summary>Prüft die Assertion gegen den gespeicherten Schlüssel; liefert Credential-ID und neuen Zähler.</summary>
    Task<(byte[] CredentialId, uint SignCount)> CompleteAssertionAsync(string state, JsonElement credential, Func<byte[], CancellationToken, Task<StoredPasskey?>> lookup, CancellationToken cancellationToken);

    /// <summary>Credential-ID aus der Antwort des Browsers, um den Passkey vor der Prüfung zu finden.</summary>
    byte[] CredentialIdOf(JsonElement credential);
}

internal sealed class PasskeyService(IOptions<IdentityOptions> options, IDataProtectionProvider dataProtection, TimeProvider clock) : IPasskeyService
{
    private static readonly TimeSpan CeremonyLifetime = TimeSpan.FromMinutes(5);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IDataProtector _protector = dataProtection.CreateProtector("CompanyHero.Identity.Passkey");
    private readonly Fido2 _fido2 = new(new Fido2Configuration
    {
        RPID = options.Value.RelyingPartyId,
        RPName = options.Value.ServerName,
        Origins = new HashSet<string>(StringComparer.Ordinal) { options.Value.PublicOriginUri.GetLeftPart(UriPartial.Authority) },
    }, null!);

    public PasskeyCeremony BeginRegistration(byte[] userHandle, string displayName, IReadOnlyList<byte[]> excludeCredentialIds)
    {
        ArgumentNullException.ThrowIfNull(userHandle);
        var created = _fido2.RequestNewCredential(new RequestNewCredentialParams
        {
            // Name und Anzeigename gehen nur an den Authenticator der Person; gespeichert wird beides nicht (Zugang 3.1).
            User = new Fido2User { Id = userHandle, Name = displayName, DisplayName = displayName },
            ExcludeCredentials = excludeCredentialIds.Select(id => new PublicKeyCredentialDescriptor(id)).ToList(),
            AuthenticatorSelection = new AuthenticatorSelection { ResidentKey = ResidentKeyRequirement.Required, UserVerification = UserVerificationRequirement.Preferred },
            AttestationPreference = AttestationConveyancePreference.None,
        });
        var json = created.ToJson();
        return new PasskeyCeremony(JsonDocument.Parse(json).RootElement.Clone(), Protect("reg", json, userHandle));
    }

    public async Task<(VerifiedPasskey Passkey, byte[] UserHandle)> CompleteRegistrationAsync(string state, JsonElement credential, CancellationToken cancellationToken)
    {
        var (json, userHandle) = Unprotect("reg", state);
        var original = CredentialCreateOptions.FromJson(json);
        var response = Deserialize<AuthenticatorAttestationRawResponse>(credential);
        try
        {
            var result = await _fido2.MakeNewCredentialAsync(new MakeNewCredentialParams
            {
                AttestationResponse = response,
                OriginalOptions = original,
                IsCredentialIdUniqueToUserCallback = (_, _) => Task.FromResult(true),
            }, cancellationToken);
            return (new VerifiedPasskey(result.Id, result.PublicKey, result.SignCount, result.AaGuid), userHandle);
        }
        catch (Fido2VerificationException ex)
        {
            throw new PasskeyRejectedException(ex.Message);
        }
    }

    public PasskeyCeremony BeginAssertion()
    {
        var assertion = _fido2.GetAssertionOptions(new GetAssertionOptionsParams { AllowedCredentials = [], UserVerification = UserVerificationRequirement.Preferred });
        var json = assertion.ToJson();
        return new PasskeyCeremony(JsonDocument.Parse(json).RootElement.Clone(), Protect("auth", json, []));
    }

    public async Task<(byte[] CredentialId, uint SignCount)> CompleteAssertionAsync(string state, JsonElement credential, Func<byte[], CancellationToken, Task<StoredPasskey?>> lookup, CancellationToken cancellationToken)
    {
        var (json, _) = Unprotect("auth", state);
        var original = AssertionOptions.FromJson(json);
        var response = Deserialize<AuthenticatorAssertionRawResponse>(credential);
        var stored = await lookup(response.RawId, cancellationToken) ?? throw new PasskeyRejectedException("Unbekannter Passkey.");
        try
        {
            var result = await _fido2.MakeAssertionAsync(new MakeAssertionParams
            {
                AssertionResponse = response,
                OriginalOptions = original,
                StoredPublicKey = stored.PublicKey,
                StoredSignatureCounter = stored.SignCount,
                IsUserHandleOwnerOfCredentialIdCallback = (p, _) => Task.FromResult(p.UserHandle.AsSpan().SequenceEqual(stored.UserHandle)),
            }, cancellationToken);
            return (result.CredentialId, result.SignCount);
        }
        catch (Fido2VerificationException ex)
        {
            throw new PasskeyRejectedException(ex.Message);
        }
    }

    public byte[] CredentialIdOf(JsonElement credential)
    {
        if (credential.ValueKind != JsonValueKind.Object || !credential.TryGetProperty("rawId", out var rawId) || rawId.ValueKind != JsonValueKind.String)
        {
            throw new PasskeyRejectedException("Antwort ohne Credential-ID.");
        }

        return System.Buffers.Text.Base64Url.DecodeFromChars(rawId.GetString());
    }

    private static T Deserialize<T>(JsonElement credential)
    {
        try
        {
            return credential.Deserialize<T>(Json) ?? throw new PasskeyRejectedException("Leere Antwort des Authenticators.");
        }
        catch (JsonException ex)
        {
            throw new PasskeyRejectedException("Antwort des Authenticators nicht lesbar: " + ex.Message);
        }
    }

    private string Protect(string purpose, string optionsJson, byte[] userHandle)
    {
        var expires = (clock.GetUtcNow() + CeremonyLifetime).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        return _protector.Protect(string.Join('\n', purpose, expires, Convert.ToBase64String(userHandle), optionsJson));
    }

    private (string OptionsJson, byte[] UserHandle) Unprotect(string purpose, string state)
    {
        string plain;
        try
        {
            plain = _protector.Unprotect(state ?? string.Empty);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            throw new PasskeyRejectedException("Zustand der Zeremonie ungültig.");
        }

        var parts = plain.Split('\n', 4);
        if (parts.Length != 4 || parts[0] != purpose)
        {
            throw new PasskeyRejectedException("Zustand der Zeremonie passt nicht zum Vorgang.");
        }

        if (long.Parse(parts[1], CultureInfo.InvariantCulture) < clock.GetUtcNow().ToUnixTimeSeconds())
        {
            throw new PasskeyRejectedException("Die Zeremonie ist abgelaufen.");
        }

        return (parts[3], Convert.FromBase64String(parts[2]));
    }
}
