using CompanyHero.Modules.Identity.Domain;
using CompanyHero.Modules.Identity.Infrastructure;
using CompanyHero.Platform.Data;
using CompanyHero.Platform.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace CompanyHero.Modules.Identity.Application;

internal sealed class PersonDirectory(IdentityDbContext db, IContextTransaction transaction, ITenantContextAccessor context, TimeProvider clock) : IPersonDirectory
{
    public async Task<PersonId> CreatePersonAsync(string displayName, CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var person = Person.Create(tenantId, displayName, clock.GetUtcNow());
        db.Persons.Add(person);
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return person.Id;
    }

    public async Task<PersonRecord?> GetAsync(PersonId personId, CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var person = await db.Persons.SingleOrDefaultAsync(p => p.TenantId == tenantId && p.Id == personId, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return person is null ? null : new PersonRecord(person.Id, person.DisplayName);
    }

    public async Task<IReadOnlyDictionary<PersonId, string>> GetDisplayNamesAsync(IEnumerable<PersonId> personIds, CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        var ids = personIds.ToList();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var persons = await db.Persons
            .Where(p => p.TenantId == tenantId && ids.Contains(p.Id))
            .Select(p => new { p.Id, p.DisplayName })
            .ToListAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return persons.ToDictionary(p => p.Id, p => p.DisplayName);
    }

    public async Task<string?> GetEmailAsync(PersonId personId, CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var email = await db.EmailLogins.Where(e => e.TenantId == tenantId && e.PersonId == personId).Select(e => e.Email).SingleOrDefaultAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return email;
    }

    public async Task<IReadOnlyList<PersonId>> ListOrphanedAsync(DateTimeOffset since, CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var lastSeen = await db.Sessions
            .Where(s => s.TenantId == tenantId && s.PersonId != null)
            .GroupBy(s => s.PersonId!.Value)
            .Select(g => new { PersonId = g.Key, LastSeen = g.Max(s => s.LastSeenAt) })
            .ToDictionaryAsync(g => g.PersonId, g => g.LastSeen, cancellationToken);
        var persons = await db.Persons.Where(p => p.TenantId == tenantId && p.State == PersonState.Active).Select(p => new { p.Id, p.CreatedAt }).ToListAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return persons
            .Where(p => (lastSeen.TryGetValue(p.Id, out var seen) ? seen : p.CreatedAt) < since)
            .Select(p => p.Id)
            .ToList();
    }
}
