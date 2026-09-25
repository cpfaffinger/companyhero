using Microsoft.EntityFrameworkCore;

namespace CompanyHero.Platform.Data;

/// <summary>
/// Basisklasse jedes Modulkontexts: arbeitet auf der Verbindung des Scopes und beginnt nie selbst eine Transaktion.
/// Transaktionen beginnt ausschließlich die Kontexttransaktion der Plattform (<see cref="IContextTransaction"/>);
/// <c>SaveChanges</c> läuft innerhalb der laufenden Kontexttransaktion.
/// </summary>
public abstract class ModuleDbContext : DbContext
{
    protected ModuleDbContext(DbContextOptions options)
        : base(options)
    {
        Database.AutoTransactionBehavior = AutoTransactionBehavior.Never;
    }
}
