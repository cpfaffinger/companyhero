using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CompanyHero.Platform.Jobs;

/// <summary>Registrierung eines Handlers für einen Jobtyp (Modul registriert beim Start).</summary>
public sealed record JobHandlerRegistration(string JobType, Type HandlerType);

/// <summary>Löst den Handler eines Jobtyps im Scope des Jobs auf. Unbekannte Jobtypen sind ein Fehler des Jobs, kein Absturz des Workers.</summary>
public sealed class JobHandlerRegistry
{
    private readonly Dictionary<string, Type> _handlers;

    public JobHandlerRegistry(IEnumerable<JobHandlerRegistration> registrations)
    {
        _handlers = new Dictionary<string, Type>(StringComparer.Ordinal);
        foreach (var registration in registrations)
        {
            if (!_handlers.TryAdd(registration.JobType, registration.HandlerType))
            {
                throw new InvalidOperationException($"Jobtyp '{registration.JobType}' ist mehrfach registriert.");
            }
        }
    }

    public IReadOnlyCollection<string> JobTypes => _handlers.Keys;

    public IJobHandler Resolve(IServiceProvider services, string jobType)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (!_handlers.TryGetValue(jobType, out var type))
        {
            throw new InvalidOperationException($"Kein Handler für Jobtyp '{jobType}' registriert.");
        }

        return (IJobHandler)services.GetRequiredService(type);
    }
}

public static class JobRegistrationExtensions
{
    /// <summary>Registriert den Handler eines Jobtyps; Handler sind scoped und laufen im Scope des Jobs.</summary>
    public static IServiceCollection AddJobHandler<THandler>(this IServiceCollection services)
        where THandler : class, IJobHandler
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<THandler>();
        services.AddSingleton(new JobHandlerRegistration(THandler.JobType, typeof(THandler)));
        return services;
    }

    /// <summary>Registriert eine zeitgesteuerte Aufgabe; der Worker führt sie fällig und gegen Doppelausführung gesperrt aus.</summary>
    public static IServiceCollection AddScheduledTask<TTask>(this IServiceCollection services)
        where TTask : class, IScheduledTask
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<TTask>();
        services.AddSingleton(new ScheduledTaskRegistration(TTask.Name, TTask.Interval, typeof(TTask)));
        return services;
    }
}
