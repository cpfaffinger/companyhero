using CompanyHero.Modules.Organisation.Application;
using CompanyHero.Modules.Organisation.Domain;
using CompanyHero.Modules.Privacy.Domain;
using CompanyHero.Modules.Privacy.Infrastructure;
using CompanyHero.Platform.Data;
using CompanyHero.Platform.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace CompanyHero.Modules.Privacy.Application;

internal sealed class VisibilityRuleService(PrivacyDbContext db, IContextTransaction transaction, ITenantContextAccessor context, IOrganisationDirectory organisations) : IVisibilityRule
{
    public async Task<bool> MayReadIndividualValuesAsync(PersonId subject, CancellationToken cancellationToken)
    {
        var visible = await FilterVisibleAsync([subject], VisibilityPurpose.IndividualValues, cancellationToken);
        return visible.Contains(subject);
    }

    public async Task<IReadOnlySet<PersonId>> FilterVisibleAsync(IEnumerable<PersonId> subjects, VisibilityPurpose purpose, CancellationToken cancellationToken)
    {
        var current = context.Require();
        var tenantId = current.RequireTenant();
        var ids = subjects.Distinct().ToList();
        var result = new HashSet<PersonId>();
        if (ids.Count == 0)
        {
            return result;
        }

        // Kiosk-Gerätesitzung ohne Person und Jobs ohne Person sehen keine Einzelperson.
        if (current.PersonId is not { } reader)
        {
            return result;
        }

        var employerRole = current.Roles.Any(Role.IsEmployerRole);
        Dictionary<PersonId, VisibilityLevel> levels;
        await using (var tx = await transaction.BeginAsync(cancellationToken))
        {
            levels = await db.VisibilitySettings
                .Where(v => v.TenantId == tenantId && ids.Contains(v.PersonId))
                .ToDictionaryAsync(v => v.PersonId, v => v.Level, cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }

        // „Mein Team“ (Datenschutz 3.1): gemeinsame Gruppe laut Organisation; Gruppen entstehen mit Organisation 2.
        var needGroups = ids.Where(id => id != reader && levels.GetValueOrDefault(id) == VisibilityLevel.Team).Append(reader).ToList();
        var groups = needGroups.Count > 1 ? await organisations.GetGroupsOfManyAsync(needGroups, cancellationToken) : new Dictionary<PersonId, IReadOnlyList<Guid>>();
        var readerGroups = groups.TryGetValue(reader, out var rg) ? rg.ToHashSet() : [];

        foreach (var subject in ids)
        {
            var share = groups.TryGetValue(subject, out var sg) && sg.Any(readerGroups.Contains);
            if (VisibilityRule.IsVisible(reader, employerRole, subject, levels.TryGetValue(subject, out var level) ? level : null, share, purpose))
            {
                result.Add(subject);
            }
        }

        return result;
    }
}

internal sealed class VisibilityChoice(PrivacyDbContext db, IContextTransaction transaction, ITenantContextAccessor context, TimeProvider clock) : IVisibilityChoice
{
    public async Task ChooseAsync(PersonId personId, VisibilityLevel level, CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var now = clock.GetUtcNow();
        var existing = await db.VisibilitySettings.SingleOrDefaultAsync(v => v.TenantId == tenantId && v.PersonId == personId, cancellationToken);
        string? previous = existing?.Level.ToString();
        if (existing is null)
        {
            db.VisibilitySettings.Add(VisibilitySetting.Choose(tenantId, personId, level, now));
        }
        else
        {
            existing.Change(level, now);
        }

        // Zustimmungsprotokoll (Datenschutz 6.1): jede Wahl und Änderung mit vorherigem und neuem Zustand, unveränderlich.
        db.Consents.Add(ConsentEntry.Record(tenantId, personId, ConsentKinds.Visibility, previous, level.ToString(), now));
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

    public async Task<IReadOnlyDictionary<PersonId, VisibilityLevel>> GetManyAsync(IEnumerable<PersonId> personIds, CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        var ids = personIds.Distinct().ToList();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var levels = await db.VisibilitySettings.Where(v => v.TenantId == tenantId && ids.Contains(v.PersonId)).ToDictionaryAsync(v => v.PersonId, v => v.Level, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return levels;
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
