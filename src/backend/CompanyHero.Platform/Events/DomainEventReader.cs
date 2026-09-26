using CompanyHero.Platform.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyHero.Platform.Events;

/// <summary>
/// Ein Fachereignis, wie ein Abonnent es liest (Backend 6.4): Typ, Version, Zeitpunkt, versionierte Nutzdaten als JSON,
/// auslösende Person (falls vorhanden). Abonnenten lesen nie Tabellen des veröffentlichenden Moduls.
/// </summary>
public sealed record DomainEventRecord(Guid Id, string Type, int Version, DateTimeOffset OccurredAt, string PayloadJson, PersonId? CausedBy);

/// <summary>
/// Quelle von Fachereignissen eines Moduls: das Modul registriert sie für sein Typpräfix (etwa <c>challenges.</c>).
/// Damit können Module ohne Projektreferenz auf den Veröffentlicher abonnieren (Domänenkarte 6: Benachrichtigungen
/// abonnieren Challenges und Feed ohne Referenz).
/// </summary>
public interface IDomainEventSource
{
    /// <summary>Typpräfix der Ereignisse dieser Quelle, etwa <c>challenges.</c>.</summary>
    string TypePrefix { get; }

    Task<DomainEventRecord?> ReadAsync(Guid eventId, CancellationToken cancellationToken);
}

/// <summary>Liest ein Fachereignis über die Quelle seines Typs; Tenant-Kontext des Jobs.</summary>
public interface IDomainEventReader
{
    Task<DomainEventRecord?> ReadAsync(string eventType, Guid eventId, CancellationToken cancellationToken);
}

internal sealed class DomainEventReader(IEnumerable<IDomainEventSource> sources) : IDomainEventReader
{
    private readonly List<IDomainEventSource> _sources = sources.ToList();

    public Task<DomainEventRecord?> ReadAsync(string eventType, Guid eventId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        var source = _sources.FirstOrDefault(s => eventType.StartsWith(s.TypePrefix, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Keine Ereignisquelle für '{eventType}' registriert.");
        return source.ReadAsync(eventId, cancellationToken);
    }
}

public static class DomainEventSourceExtensions
{
    /// <summary>Ein Modul stellt seine Ereignistabelle als Quelle bereit (scoped, im Kontext des Lesers).</summary>
    public static IServiceCollection AddDomainEventSource<TSource>(this IServiceCollection services)
        where TSource : class, IDomainEventSource
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<IDomainEventSource, TSource>();
        return services;
    }
}
