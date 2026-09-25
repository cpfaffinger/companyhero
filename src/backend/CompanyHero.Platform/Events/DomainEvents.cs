using CompanyHero.Platform.Jobs;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyHero.Platform.Events;

/// <summary>Abonnement: Ereignisse eines Typs werden dem Jobtyp als Job zugestellt (Backend 6.4).</summary>
public sealed record EventSubscription(string EventType, string JobType);

/// <summary>
/// Zustellung von Fachereignissen zwischen Modulen (Backend 6.4, A-006): Ein Modul speichert das Ereignis in seiner
/// eigenen Ereignistabelle und ruft in derselben Transaktion die Zustellung auf; je Abonnent entsteht ein Job mit der
/// Ereigniskennung als Referenz. Abonnenten lesen das Ereignis über die öffentliche Schnittstelle des veröffentlichenden
/// Moduls, nie über dessen Tabellen. Ereignisse sind versioniert; Abonnenten sind idempotent.
/// </summary>
public interface IDomainEventDispatcher
{
    /// <summary>Reiht je Abonnent des Ereignistyps einen Job ein; liefert die Zahl der eingereihten Jobs.</summary>
    Task<int> DispatchAsync(string eventType, Guid eventId, CancellationToken cancellationToken);
}

internal sealed class DomainEventDispatcher(IJobQueue queue, IEnumerable<EventSubscription> subscriptions) : IDomainEventDispatcher
{
    private readonly ILookup<string, string> _subscriptions = subscriptions.ToLookup(s => s.EventType, s => s.JobType, StringComparer.Ordinal);

    public async Task<int> DispatchAsync(string eventType, Guid eventId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        var reference = eventId.ToString("D");
        var count = 0;
        foreach (var jobType in _subscriptions[eventType])
        {
            var result = await queue.EnqueueAsync(new JobRequest(jobType, reference, $"{eventType}:{reference}"), cancellationToken);
            if (result.Outcome == EnqueueOutcome.Enqueued)
            {
                count++;
            }
        }

        return count;
    }
}

public static class DomainEventExtensions
{
    public static IServiceCollection AddDomainEvents(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
        return services;
    }

    /// <summary>Ein Modul abonniert einen Ereignistyp; die Zustellung läuft über den Handler des Jobtyps.</summary>
    public static IServiceCollection AddEventSubscription<THandler>(this IServiceCollection services, string eventType)
        where THandler : class, IJobHandler
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        services.AddJobHandler<THandler>();
        services.AddSingleton(new EventSubscription(eventType, THandler.JobType));
        return services;
    }
}
