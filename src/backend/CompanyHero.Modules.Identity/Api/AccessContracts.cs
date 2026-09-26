using System.Text.Json;
using System.Text.Json.Serialization;

namespace CompanyHero.Modules.Identity.Api;

/// <summary>Sitzungsart im Vertrag (Zugang 5).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SessionKindDto>))]
public enum SessionKindDto
{
    [JsonStringEnumMemberName("member")] Member,
    [JsonStringEnumMemberName("privileged")] Privileged,
    [JsonStringEnumMemberName("kiosk_device")] KioskDevice,
    [JsonStringEnumMemberName("kiosk_person")] KioskPerson,
}

[JsonConverter(typeof(JsonStringEnumConverter<VisibilityDto>))]
public enum VisibilityDto
{
    [JsonStringEnumMemberName("only_me")] OnlyMe,
    [JsonStringEnumMemberName("team")] Team,
    [JsonStringEnumMemberName("company")] Company,
}

/// <summary>Die geprüfte Sitzung des Requests: Art, Laufzeiten, Frische; keine Geheimnisse.</summary>
public sealed record SessionResponse(SessionKindDto Kind, string TenantId, string? PersonId, DateTimeOffset AuthenticatedAt, DateTimeOffset? SlidingUntil, DateTimeOffset? AbsoluteUntil, DateTimeOffset FreshUntil, string? KioskDeviceId);

public sealed record ProviderResponse(string Key, string DisplayName);

/// <summary>WebAuthn-Zeremonie: Optionen für <c>navigator.credentials</c> (JSON nach WebAuthn Level 3) und geschützter Zustand.</summary>
public sealed record PasskeyCeremonyResponse(JsonElement Options, string State);

/// <summary>Antwort des Browsers (<c>PublicKeyCredential.toJSON()</c>) mit dem Zustand der Zeremonie.</summary>
public sealed record PasskeyAnswerRequest(string State, JsonElement Credential, string? DeviceName);

public sealed record RecoveryLoginRequest(string Code);

public sealed record MagicLinkRequest(string Email);

public sealed record TokenRequest(string Token);

/// <summary>Ergebnis einer Anmeldung; die Sitzung selbst liegt im Cookie. Der neue Wiederherstellungscode erscheint genau einmal.</summary>
public sealed record LoginResponse(string TenantId, string PersonId, SessionKindDto Kind, string? NewRecoveryCode, bool MustSetUpAccess);

public sealed record JoinWaysResponse(bool Passkey, bool MagicLink, bool Kiosk, IReadOnlyList<ProviderResponse> Providers, IReadOnlyList<string> ForcedProviderKeys);

public sealed record PendingExternalResponse(string ProviderKey, string DisplayName);

public sealed record JoinGroupResponse(string GroupId, string Name);

/// <summary>Dimension mit wählbaren Gruppen für den Beitritt (Zugang 2.2 Schritt 4, Organisation 2.1).</summary>
public sealed record JoinDimensionResponse(string DimensionId, string Name, IReadOnlyList<JoinGroupResponse> Groups);

/// <summary>Gewählte Gruppe je Dimension beim Beitritt; höchstens eine je Dimension.</summary>
public sealed record JoinGroupChoiceRequest(string DimensionId, string GroupId);

/// <summary>Tenant-Vorschau beim Beitritt (Zugang 2.1): Name, Wege und wählbare Gruppen; Logo über die Marke des Tenants nach Zuordnung.</summary>
public sealed record JoinPreviewResponse(string TenantId, string TenantName, JoinWaysResponse Ways, PendingExternalResponse? PendingExternal, string? Role, IReadOnlyList<JoinDimensionResponse> Dimensions);

public sealed record JoinPasskeyOptionsRequest(string DisplayName);

/// <summary>Beitritt auf dem eigenen Gerät: genau die Wege, die die Person wählt; <c>useExternal</c> nimmt die geprüfte Anbieteridentität aus der Übergabe.</summary>
public sealed record JoinRequest(string DisplayName, VisibilityDto Visibility, PasskeyAnswerRequest? Passkey, string? Email, bool UseExternal, string? KioskPin, IReadOnlyList<JoinGroupChoiceRequest>? Groups);

public sealed record KioskJoinRequest(string DisplayName, VisibilityDto Visibility, string Pin, IReadOnlyList<JoinGroupChoiceRequest>? Groups);

public sealed record RoleJoinRequest(string RealName, VisibilityDto Visibility, PasskeyAnswerRequest? Passkey, string? Email, bool UseExternal);

/// <summary>Ergebnis des Beitritts: Kiosk-Kennung immer, Wiederherstellungscode einmalig (ohne E-Mail und Anbieter), Sitzung im Cookie außer am Kiosk.</summary>
public sealed record JoinResponse(string TenantId, string PersonId, string KioskId, string? RecoveryCode, SessionKindDto? SessionKind);

public sealed record KioskRegisterRequest(string Code);

public sealed record KioskDeviceResponse(string TenantId, string DeviceId, string Name, int IdleSeconds);

public sealed record KioskLoginRequest(string KioskId, string Pin);

/// <summary>Kiosk-Personensitzung: Anzeigename, Auto-Logout in Sekunden, absolutes Ende; die Oberfläche zeigt den Countdown (Zugang 6.5).</summary>
public sealed record KioskLoginResponse(string PersonId, string DisplayName, int IdleSeconds, DateTimeOffset IdleUntil, DateTimeOffset AbsoluteUntil);

public sealed record PinRequest(string Pin);

public sealed record PinResetRequest(string KioskId, string RecoveryCode, string Pin);

public sealed record TransferResponse(string Url, DateTimeOffset ExpiresAt);

public sealed record PasskeyResponse(string PasskeyId, string DeviceName, DateTimeOffset CreatedAt, DateTimeOffset? LastUsedAt);

public sealed record LinkedProviderResponse(string LinkId, string ProviderKey, string DisplayName, DateTimeOffset CreatedAt);

/// <summary>Anmeldewege der Person für die Profilansicht (Zugang 4); Kiosk-Kennung für den druckbaren QR-Code.</summary>
public sealed record AccessOverviewResponse(IReadOnlyList<PasskeyResponse> Passkeys, string? Email, IReadOnlyList<LinkedProviderResponse> Providers, bool RecoveryCodeActive, string KioskId, bool KioskPinSet, IReadOnlyList<ProviderResponse> AvailableProviders);

public sealed record EmailRequest(string Email);

public sealed record RecoveryCodeResponse(string Code);

public sealed record CountResponse(int Count);

public sealed record JoinCodeResponse(string JoinCodeId, string Code, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, int? UsageLimit, int UsedCount, DateTimeOffset? RevokedAt, string JoinUrl);

public sealed record CreateJoinCodeRequest(int? UsageLimit, int? ValidDays);

public sealed record IssueRoleCodeRequest(string Role, string? Email);

public sealed record RoleCodeResponse(string RoleCodeId, string Code, string Role, DateTimeOffset ExpiresAt);

public sealed record RedeemRoleCodeRequest(string Code, string? RealName);

public sealed record LoginPolicyResponse(bool MagicLink, bool Passkey, bool Kiosk, IReadOnlyList<string> DisabledProviderKeys, IReadOnlyList<string> ForcedProviderKeys, int KioskIdleSeconds, IReadOnlyList<ProviderResponse> AvailableProviders);

public sealed record LoginPolicyRequest(bool MagicLink, bool Passkey, bool Kiosk, IReadOnlyList<string> DisabledProviderKeys, IReadOnlyList<string> ForcedProviderKeys, int KioskIdleSeconds);

public sealed record LoginPolicyImpactResponse(int PersonsWithoutWay);

public sealed record ConfigureProviderRequest(string DisplayName, string Issuer, string ClientId, string ClientSecret);

public sealed record TenantProviderResponse(string ProviderId, string Key, string DisplayName, string Issuer, string ClientId, DateTimeOffset? ValidatedAt, DateTimeOffset? DisabledAt);

public sealed record CreateKioskDeviceRequest(string Name);

/// <summary>Kiosk-Gerät aus Sicht der Verwaltung: letzte Aktivität und Anzahl der Anmeldungen, nie Personen (Zugang 6.1).</summary>
public sealed record KioskDeviceAdminResponse(string DeviceId, string Name, DateTimeOffset CreatedAt, DateTimeOffset? RegisteredAt, DateTimeOffset? RevokedAt, DateTimeOffset? LastSeenAt, int LoginCount, string? RegistrationCode, DateTimeOffset? RegistrationExpiresAt);
