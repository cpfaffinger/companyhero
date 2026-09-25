using Microsoft.Extensions.DependencyInjection;

namespace CompanyHero.Platform.Tenancy;

/// <summary>
/// Erzeugt für einen Job oder einen Systemablauf einen eigenen Scope mit genau einem Kontext (Backend 5.1 Nr. 2:
/// „Worker setzen für jeden Job einen neuen Kontext“). Der Worker der Stufe 3 verwendet diese Fabrik je Job.
/// </summary>
public interface ITenantScopeFactory
{
    /// <summary>Neuer Scope mit gesetztem Kontext. Der Aufrufer entsorgt den Scope; danach existiert der Kontext nicht mehr.</summary>
    TenantScope Create(TenantContext context);

    /// <summary>Führt eine Arbeit in einem frischen Scope mit dem gegebenen Kontext aus.</summary>
    Task<TResult> RunAsync<TResult>(TenantContext context, Func<IServiceProvider, CancellationToken, Task<TResult>> work, CancellationToken cancellationToken);

    /// <summary>Führt eine Arbeit ohne Ergebnis in einem frischen Scope mit dem gegebenen Kontext aus.</summary>
    Task RunAsync(TenantContext context, Func<IServiceProvider, CancellationToken, Task> work, CancellationToken cancellationToken);
}

/// <summary>Ein Scope mit Kontext; Dienste daraus sehen genau diesen Kontext.</summary>
public sealed class TenantScope : IAsyncDisposable, IDisposable
{
    private readonly AsyncServiceScope _scope;

    internal TenantScope(AsyncServiceScope scope, TenantContext context)
    {
        _scope = scope;
        Context = context;
    }

    public TenantContext Context { get; }

    public IServiceProvider Services => _scope.ServiceProvider;

    public ValueTask DisposeAsync() => _scope.DisposeAsync();

    public void Dispose() => _scope.Dispose();
}

internal sealed class TenantScopeFactory(IServiceScopeFactory scopeFactory) : ITenantScopeFactory
{
    public TenantScope Create(TenantContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var scope = scopeFactory.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContextAccessor>().Set(context);
        return new TenantScope(scope, context);
    }

    public async Task<TResult> RunAsync<TResult>(TenantContext context, Func<IServiceProvider, CancellationToken, Task<TResult>> work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);
        await using var scope = Create(context);
        return await work(scope.Services, cancellationToken);
    }

    public async Task RunAsync(TenantContext context, Func<IServiceProvider, CancellationToken, Task> work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);
        await using var scope = Create(context);
        await work(scope.Services, cancellationToken);
    }
}
