namespace CompanyHero.Platform.Tenancy;

/// <summary>
/// Zugriff auf den Kontext des laufenden Requests oder Jobs. Ein Scope (Request oder Job) hat genau einen Kontext.
/// Module lesen; setzen darf nur die Plattforminfrastruktur (Middleware und <see cref="ITenantScopeFactory"/>).
/// </summary>
public interface ITenantContextAccessor
{
    /// <summary>Der geprüfte Kontext oder <c>null</c>, wenn noch keiner gesetzt ist.</summary>
    TenantContext? Current { get; }

    /// <summary>Der geprüfte Kontext; wirft <see cref="TenantContextMissingException"/>, wenn keiner gesetzt ist.</summary>
    TenantContext Require();
}

/// <summary>Scoped; ein Exemplar je Request oder Job.</summary>
public sealed class TenantContextAccessor : ITenantContextAccessor
{
    public TenantContext? Current { get; private set; }

    public TenantContext Require() => Current ?? throw new TenantContextMissingException();

    /// <summary>Nur für die Plattforminfrastruktur: setzt oder ersetzt den Kontext dieses Scopes.</summary>
    internal void Set(TenantContext? context) => Current = context;
}
