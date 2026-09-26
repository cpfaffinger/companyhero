using CompanyHero.Modules.Organisation.Domain;
using CompanyHero.Modules.Privacy.Domain;
using CompanyHero.Modules.Privacy.Infrastructure;
using CompanyHero.Platform.Data;
using CompanyHero.Platform.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace CompanyHero.Modules.Privacy.Application;

internal sealed class VisibilityRuleService(PrivacyDbContext db, IContextTransaction transaction, ITenantContextAccessor context) : IVisibilityRule
{
    public async Task<bool> MayReadIndividualValuesAsync(PersonId subject, CancellationToken cancellationToken)
    {
        var current = context.Require();
        var tenantId = current.RequireTenant();
        var reader = current.RequirePerson();
        var employerRole = current.Roles.Any(Role.IsEmployerRole);

        await using var tx = await transaction.BeginAsync(cancellationToken);
        var level = await db.VisibilitySettings
            .Where(v => v.TenantId == tenantId && v.PersonId == subject)
            .Select(v => (VisibilityLevel?)v.Level)
            .SingleOrDefaultAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        // Gruppen entstehen mit dem Fachpfad (Stufe 6, Organisation 2); bis dahin teilt niemand eine Gruppe und
        // „Mein Team“ bleibt geschlossen. Die restriktivere Einstellung gewinnt (Datenschutz 3.3).
        return VisibilityRule.MayReadIndividualValues(reader, employerRole, subject, level, shareGroup: false);
    }
}

internal sealed class VisibilityChoice(PrivacyDbContext db, IContextTransaction transaction, ITenantContextAccessor context, TimeProvider clock) : IVisibilityChoice
{
    public async Task ChooseAsync(PersonId personId, VisibilityLevel level, CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var existing = await db.VisibilitySettings.SingleOrDefaultAsync(v => v.TenantId == tenantId && v.PersonId == personId, cancellationToken);
        if (existing is null)
        {
            db.VisibilitySettings.Add(VisibilitySetting.Choose(tenantId, personId, level, clock.GetUtcNow()));
        }
        else
        {
            existing.Change(level, clock.GetUtcNow());
        }

        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    public async Task<VisibilityLevel?> GetAsync(PersonId personId, CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var level = await db.VisibilitySettings
            .Where(v => v.TenantId == tenantId && v.PersonId == personId)
            .Select(v => (VisibilityLevel?)v.Level)
            .SingleOrDefaultAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return level;
    }
}

/// <summary>Umsetzung des Plattform-Querschnitts für den Beitritt: dieselbe Wahl, dieselbe Tabelle, dieselbe Transaktion.</summary>
internal sealed class VisibilityChoiceRecorder(IVisibilityChoice choice) : Platform.Privacy.IVisibilityChoiceRecorder
{
    public Task RecordAsync(PersonId personId, Platform.Privacy.VisibilityChoice visibility, CancellationToken cancellationToken) =>
        choice.ChooseAsync(personId, visibility switch
        {
            Platform.Privacy.VisibilityChoice.OnlyMe => VisibilityLevel.OnlyMe,
            Platform.Privacy.VisibilityChoice.Team => VisibilityLevel.Team,
            Platform.Privacy.VisibilityChoice.Company => VisibilityLevel.Company,
            _ => throw new ArgumentOutOfRangeException(nameof(visibility)),
        }, cancellationToken);
}
