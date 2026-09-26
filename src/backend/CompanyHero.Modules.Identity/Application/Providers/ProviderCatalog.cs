using CompanyHero.Modules.Identity.Domain;
using CompanyHero.Modules.Identity.Infrastructure;
using CompanyHero.Platform.Data;
using CompanyHero.Platform.Tenancy;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CompanyHero.Modules.Identity.Application.Providers;

/// <summary>Ein Anbieter, wie ihn Anmeldeseite und OIDC-Schema kennen; plattformweit (Tenant <c>null</c>) oder tenant-eigen.</summary>
public sealed record ProviderRecord(string Key, string DisplayName, string Authority, string ClientId, string ClientSecret, string? IssuerPattern, TenantId? TenantId, DateTimeOffset? UpdatedAt)
{
    public bool IsPlatformWide => TenantId is null;
}

/// <summary>Plattformweite Anbieter aus der Konfiguration und tenant-eigene Anbieter aus dem Datenbestand (Zugang 3.2).</summary>
public interface IProviderCatalog
{
    IReadOnlyList<ProviderRecord> Platform();

    /// <summary>Beliebiger Kontext: Anbieter zum Schlüssel; tenant-eigene werden im Plattformkontext gelesen (Callback vor Tenant-Zuordnung).</summary>
    Task<ProviderRecord?> FindAsync(string key, CancellationToken cancellationToken);

    /// <summary>Plattformkontext: aktive tenant-eigene Anbieter eines Tenants zusätzlich zu den plattformweiten, gefiltert nach der Festlegung des Tenants.</summary>
    Task<IReadOnlyList<ProviderRecord>> ListForTenantAsync(TenantId tenantId, CancellationToken cancellationToken);

    /// <summary>Client-Secret eines tenant-eigenen Anbieters schützen (Data Protection); nie im Klartext im Datenbestand.</summary>
    string ProtectSecret(string clientSecret);
}

internal sealed class ProviderCatalog(IOptions<IdentityOptions> options, IdentityDbContext db, IContextTransaction transaction, IDataProtectionProvider dataProtection) : IProviderCatalog
{
    private readonly IDataProtector _protector = dataProtection.CreateProtector("CompanyHero.Identity.ClientSecret");

    public IReadOnlyList<ProviderRecord> Platform() =>
        options.Value.Providers
            .Where(p => !string.IsNullOrWhiteSpace(p.Value.Authority) && !string.IsNullOrWhiteSpace(p.Value.ClientId))
            .Select(p => new ProviderRecord(p.Key, p.Value.DisplayName is { Length: > 0 } n ? n : p.Key, p.Value.Authority.TrimEnd('/'), p.Value.ClientId, p.Value.ClientSecret, p.Value.IssuerPattern, null, null))
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .ToList();

    public async Task<ProviderRecord?> FindAsync(string key, CancellationToken cancellationToken)
    {
        var platform = Platform().SingleOrDefault(p => string.Equals(p.Key, key, StringComparison.Ordinal));
        if (platform is not null)
        {
            return platform;
        }

        if (!Guid.TryParseExact(key, "N", out var id))
        {
            return null;
        }

        await using var tx = await transaction.BeginAsync(cancellationToken);
        var provider = await db.ExternalProviders.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return provider is null || !provider.IsActive ? null : ToRecord(provider);
    }

    public async Task<IReadOnlyList<ProviderRecord>> ListForTenantAsync(TenantId tenantId, CancellationToken cancellationToken)
    {
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var own = await db.ExternalProviders.AsNoTracking().Where(p => p.TenantId == tenantId && p.ValidatedAt != null && p.DisabledAt == null).OrderBy(p => p.Id).ToListAsync(cancellationToken);
        var policy = await db.LoginPolicies.AsNoTracking().SingleOrDefaultAsync(p => p.TenantId == tenantId, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        var all = Platform().Concat(own.Select(ToRecord)).ToList();
        return policy is null ? all : all.Where(p => policy.ProviderAllowed(p.Key)).ToList();
    }

    public string ProtectSecret(string clientSecret) => _protector.Protect(clientSecret);

    private ProviderRecord ToRecord(ExternalProvider provider) =>
        new(provider.Key, provider.DisplayName, provider.Issuer, provider.ClientId, _protector.Unprotect(provider.ClientSecretProtected), null, provider.TenantId, provider.UpdatedAt);
}
