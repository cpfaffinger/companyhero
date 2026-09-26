using CompanyHero.Platform.Tenancy;

namespace CompanyHero.Modules.Identity.Domain;

/// <summary>
/// Tenant-eigener OpenID-Connect-Anbieter (Zugang 3.2, A-015): Issuer, Client-ID und geschütztes Client-Secret; aktiv erst
/// nach erfolgreicher Validierung über das Discovery-Dokument. Der Schlüssel benennt das Anmeldeschema und den Callback.
/// </summary>
public sealed class ExternalProvider : ITenantOwned
{
    // EF Core: Materialisierung ohne Konstruktorbindung.
    private ExternalProvider()
    {
        DisplayName = string.Empty;
        Issuer = string.Empty;
        ClientId = string.Empty;
        ClientSecretProtected = string.Empty;
    }

    private ExternalProvider(TenantId tenantId, Guid id, string displayName, string issuer, string clientId, string clientSecretProtected, DateTimeOffset now)
    {
        TenantId = tenantId;
        Id = id;
        DisplayName = displayName;
        Issuer = issuer;
        ClientId = clientId;
        ClientSecretProtected = clientSecretProtected;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public TenantId TenantId { get; }

    public Guid Id { get; }

    /// <summary>Schlüssel des Anbieters für Schema und Callback-Pfad.</summary>
    public string Key => Id.ToString("N");

    public string DisplayName { get; }

    public string Issuer { get; }

    public string ClientId { get; }

    /// <summary>Client-Secret, mit Data Protection geschützt; nie im Klartext im Datenbestand.</summary>
    public string ClientSecretProtected { get; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? ValidatedAt { get; private set; }

    public DateTimeOffset? DisabledAt { get; private set; }

    public bool IsActive => ValidatedAt is not null && DisabledAt is null;

    public static ExternalProvider Configure(TenantId tenantId, string displayName, string issuer, string clientId, string clientSecretProtected, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientSecretProtected);
        if (!Uri.TryCreate(issuer, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("Der Issuer muss eine https-URL sein.", nameof(issuer));
        }

        return new ExternalProvider(tenantId, Guid.CreateVersion7(), displayName.Trim(), issuer.TrimEnd('/'), clientId.Trim(), clientSecretProtected, now);
    }

    /// <summary>Discovery-Dokument geprüft: Issuer stimmt, Endpunkte vorhanden, Code-Flow mit PKCE (S256) unterstützt.</summary>
    public void MarkValidated(DateTimeOffset now)
    {
        ValidatedAt = now;
        UpdatedAt = now;
    }

    public void Disable(DateTimeOffset now)
    {
        DisabledAt ??= now;
        UpdatedAt = now;
    }
}

/// <summary>Prüfung eines Discovery-Dokuments (Zugang 3.2): reine Fachregel über die gelesenen Werte.</summary>
public static class DiscoveryValidation
{
    public sealed record Document(string? Issuer, string? AuthorizationEndpoint, string? TokenEndpoint, string? JwksUri, IReadOnlyList<string> ResponseTypes, IReadOnlyList<string> CodeChallengeMethods, IReadOnlyList<string> Scopes);

    /// <summary>Gründe, warum der Anbieter nicht aktiv wird; leer bedeutet gültig.</summary>
    public static IReadOnlyList<string> Problems(string expectedIssuer, Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var problems = new List<string>();
        if (!string.Equals(document.Issuer?.TrimEnd('/'), expectedIssuer?.TrimEnd('/'), StringComparison.Ordinal))
        {
            problems.Add("issuer_mismatch");
        }

        if (!IsHttps(document.AuthorizationEndpoint))
        {
            problems.Add("authorization_endpoint_missing");
        }

        if (!IsHttps(document.TokenEndpoint))
        {
            problems.Add("token_endpoint_missing");
        }

        if (!IsHttps(document.JwksUri))
        {
            problems.Add("jwks_uri_missing");
        }

        if (!document.ResponseTypes.Contains("code", StringComparer.Ordinal))
        {
            problems.Add("code_flow_unsupported");
        }

        if (!document.CodeChallengeMethods.Contains("S256", StringComparer.Ordinal))
        {
            problems.Add("pkce_s256_unsupported");
        }

        if (document.Scopes.Count > 0 && !document.Scopes.Contains("openid", StringComparer.Ordinal))
        {
            problems.Add("openid_scope_unsupported");
        }

        return problems;
    }

    private static bool IsHttps(string? url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
}
