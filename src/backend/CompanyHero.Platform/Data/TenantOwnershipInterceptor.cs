using CompanyHero.Platform.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CompanyHero.Platform.Data;

/// <summary>
/// Vor jedem Speichern: jede tenantbezogene Entität trägt die <c>tenant_id</c> des aktiven Kontexts (Backend 5.1 Nr. 3).
/// Im Plattformkontext werden tenantbezogene Entitäten nicht geschrieben.
/// </summary>
internal sealed class TenantOwnershipInterceptor(ITenantContextAccessor accessor) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Check(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Check(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Check(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var current = accessor.Require();
        foreach (var entry in context.ChangeTracker.Entries<ITenantOwned>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            if (current.Kind != TenantContextKind.Tenant)
            {
                throw new TenantMismatchException($"'{entry.Metadata.ClrType.Name}' ist tenantbezogen und wird im Plattformkontext nicht geschrieben.");
            }

            if (entry.Entity.TenantId != current.TenantId)
            {
                throw new TenantMismatchException($"'{entry.Metadata.ClrType.Name}' gehört zu Tenant {entry.Entity.TenantId}, aktiv ist {current.TenantId}.");
            }
        }
    }
}
