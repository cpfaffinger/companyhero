using System.Globalization;
using CompanyHero.Platform.Tenancy;

namespace CompanyHero.Modules.Privacy.Domain;

/// <summary>Art der Zustimmung im Zustimmungsprotokoll (Datenschutz 6.1): Sichtbarkeitsstufe, Arena, Buddy.</summary>
public static class ConsentKinds
{
    public const string Visibility = "visibility";
}

/// <summary>
/// Eintrag des Zustimmungsprotokolls (Datenschutz 6.1, A-025): unveränderlich, mit Zeitpunkt, vorherigem und neuem
/// Zustand. Der Bezug ist die Personenkennung; nach dem Austritt bleibt der Eintrag 3 Jahre mit einer nicht rückführbaren
/// Kennung (Datenschutz 5.1, 5.2).
/// </summary>
public sealed class ConsentEntry : ITenantOwned
{
    private ConsentEntry(TenantId tenantId, Guid id, string subjectRef, string kind, string? previous, string next, DateTimeOffset occurredAt)
    {
        TenantId = tenantId;
        Id = id;
        SubjectRef = subjectRef;
        Kind = kind;
        Previous = previous;
        Next = next;
        OccurredAt = occurredAt;
    }

    public TenantId TenantId { get; }

    public Guid Id { get; }

    /// <summary>Personenkennung als String; nach dem Austritt <c>anon:</c> plus zufällige Kennung.</summary>
    public string SubjectRef { get; private set; }

    public string Kind { get; }

    public string? Previous { get; }

    public string Next { get; }

    public DateTimeOffset OccurredAt { get; }

    public static ConsentEntry Record(TenantId tenantId, PersonId personId, string kind, string? previous, string next, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(next);
        return new ConsentEntry(tenantId, Guid.CreateVersion7(), personId.ToString(), kind, previous, next, now);
    }

    public static string SubjectRefOf(PersonId personId) => personId.ToString();

    /// <summary>Nicht rückführbare Kennung nach dem Austritt: dieselbe Zufallskennung für alle Einträge der Person, ohne Bezug zur Personenkennung.</summary>
    public static string AnonymousRef(Guid random) => string.Create(CultureInfo.InvariantCulture, $"anon:{random:N}");

    public void Anonymise(string anonymousRef) => SubjectRef = anonymousRef;
}
