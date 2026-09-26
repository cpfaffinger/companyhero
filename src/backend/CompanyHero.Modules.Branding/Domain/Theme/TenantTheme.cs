using CompanyHero.Platform.Tenancy;

namespace CompanyHero.Modules.Branding.Domain.Theme;

/// <summary>
/// Veröffentlichte Theme-Version eines Tenants (Marke 3.4, A-077): Dokument und abgeleiteter Tokensatz als JSON, versioniert,
/// zwölf Monate abrufbar. Serverseitig erzeugte Dokumente verwenden die zum Erzeugungszeitpunkt gültige Version.
/// </summary>
public sealed class TenantTheme : ITenantOwned
{
    private TenantTheme(TenantId tenantId, int version, string document, string tokens, DateTimeOffset publishedAt, PersonId? publishedBy)
    {
        TenantId = tenantId;
        Version = version;
        Document = document;
        Tokens = tokens;
        PublishedAt = publishedAt;
        PublishedBy = publishedBy;
    }

    public TenantId TenantId { get; }

    public int Version { get; }

    /// <summary>Theme-Dokument (Schema 1) als JSON.</summary>
    public string Document { get; }

    /// <summary>Tokensatz beider Modi mit Ersetzungsliste als JSON.</summary>
    public string Tokens { get; }

    public DateTimeOffset PublishedAt { get; }

    public PersonId? PublishedBy { get; }

    public static TimeSpan History { get; } = TimeSpan.FromDays(365);

    public static TenantTheme Publish(TenantId tenantId, int version, string document, string tokens, DateTimeOffset now, PersonId? publishedBy)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(version, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(tokens);
        return new TenantTheme(tenantId, version, document, tokens, now.ToUniversalTime(), publishedBy);
    }
}
