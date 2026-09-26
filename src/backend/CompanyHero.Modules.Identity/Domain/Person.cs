using CompanyHero.Platform.Tenancy;

namespace CompanyHero.Modules.Identity.Domain;

/// <summary>Zustand einer Person aus Sicht des Zugangs (Zugang 8): aktiv oder ausgetreten.</summary>
public enum PersonState
{
    Active = 1,
    Left = 2,
}

/// <summary>
/// Person eines Tenants (Zugang 4): trägt den frei gewählten Anzeigenamen; Funktionsrollen zusätzlich den Klarnamen, der
/// beim Einlösen des Rollencodes zum Anzeigenamen wird (Zugang 2.3). Kein Klarname, keine E-Mail und kein Geburtsdatum sind
/// Voraussetzung (Datenschutz 2 Regel 10). Anmeldewege hängen als eigene Entitäten an der Person (A-016).
/// </summary>
public sealed class Person : ITenantOwned
{
    private Person(TenantId tenantId, PersonId id, string displayName, DateTimeOffset createdAt)
    {
        TenantId = tenantId;
        Id = id;
        DisplayName = displayName;
        CreatedAt = createdAt;
    }

    public TenantId TenantId { get; }

    public PersonId Id { get; }

    public string DisplayName { get; private set; }

    /// <summary>Klarname einer Funktionsrolle; für Mitglieder immer <c>null</c> (Zugang 1).</summary>
    public string? RealName { get; private set; }

    public PersonState State { get; private set; } = PersonState.Active;

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset? LeftAt { get; private set; }

    public static Person Create(TenantId tenantId, string displayName, DateTimeOffset createdAt) =>
        Create(tenantId, PersonId.New(), displayName, createdAt);

    /// <summary>Anlage mit vorgegebener Kennung, z. B. dem User-Handle einer bereits abgeschlossenen Passkey-Zeremonie (Zugang 2.2).</summary>
    public static Person Create(TenantId tenantId, PersonId id, string displayName, DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        return new Person(tenantId, id, displayName.Trim(), createdAt);
    }

    /// <summary>Funktionsrolle eingelöst (Zugang 2.3): Klarname gesetzt, Anzeigename auf den Klarnamen gesetzt.</summary>
    public void AssumeRealName(string realName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(realName);
        RealName = realName.Trim();
        DisplayName = RealName;
    }

    /// <summary>Austritt (Zugang 8): die Person verliert Zugang und Identitäten; die Datenfolgen regelt Datenschutz 5.2.</summary>
    public void Leave(DateTimeOffset now)
    {
        State = PersonState.Left;
        LeftAt = now;
    }
}
