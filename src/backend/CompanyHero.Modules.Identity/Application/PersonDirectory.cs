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
}
