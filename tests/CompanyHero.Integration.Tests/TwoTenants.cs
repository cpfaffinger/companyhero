using CompanyHero.Modules.Identity.Application;
using CompanyHero.Modules.Organisation.Application;
using CompanyHero.Modules.Organisation.Domain;
using CompanyHero.Modules.Privacy.Application;
using CompanyHero.Modules.Privacy.Domain;
using CompanyHero.Modules.Progress.Application;
using CompanyHero.Modules.Progress.Domain;
using CompanyHero.Platform.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyHero.Integration.Tests;

/// <summary>
/// Synthetische Testdaten (Backend 9, Durchstich 4): zwei Tenants mit mehreren Rollen und persönlichen Aktivitäten,
/// angelegt über die öffentlichen Anwendungsfunktionen der Module, je Tenant in eigenem Kontext. Keine echten Personen.
/// </summary>
public sealed record ScratchTenant(TenantId Id, PersonId Admin, PersonId Manager, PersonId MemberA, PersonId MemberB);

public sealed record TwoTenants(
    Guid OperatorId,
    TenantId WiesnerId,
    TenantId HoedlId,
    PersonId WiesnerAdmin,
    PersonId WiesnerManager,
    PersonId WiesnerBea,
    PersonId WiesnerCem,
    PersonId HoedlAdmin,
    PersonId HoedlMia)
{
    public const int WiesnerMembers = 4;
    public const int HoedlMembers = 2;
    public const int BeaActivities = 2;
    public const int CemActivities = 3;
    public const int MiaActivities = 2;

    /// <summary>Alle Personen der beiden Tenants mit ihrem Tenant; die Fixture stellt ihnen Sitzungen aus.</summary>
    public IEnumerable<(TenantId Tenant, PersonId Person)> Persons =>
    [
        (WiesnerId, WiesnerAdmin), (WiesnerId, WiesnerManager), (WiesnerId, WiesnerBea), (WiesnerId, WiesnerCem),
        (HoedlId, HoedlAdmin), (HoedlId, HoedlMia),
    ];

    public static async Task<TwoTenants> SeedAsync(IServiceProvider services, FixedClock clock)
    {
        var scopes = services.GetRequiredService<ITenantScopeFactory>();
        var ct = CancellationToken.None;

        var (operatorId, wiesner, hoedl) = await scopes.RunAsync(TenantContext.ForPlatform(), async (sp, c) =>
        {
            var organisations = sp.GetRequiredService<IOrganisationDirectory>();
            var op = await organisations.EnsureOperatorAsync("CompanyHero Betreiber (Test)", c);
            var a = await organisations.CreateTenantAsync("Wiesner", op, c);
            var b = await organisations.CreateTenantAsync("Hödl", op, c);
            return (op, a, b);
        }, ct);

        var w = await scopes.RunAsync(TenantContext.ForTenant(wiesner), async (sp, c) =>
        {
            var admin = await JoinAsync(sp, "Anna Admin", VisibilityLevel.Company, [Role.Member, Role.TenantAdmin], c);
            var manager = await JoinAsync(sp, "Dana Manager", VisibilityLevel.Company, [Role.Member, Role.ProgrammeManager], c);
            var bea = await JoinAsync(sp, "Bea", VisibilityLevel.Company, [Role.Member], c);
            var cem = await JoinAsync(sp, "Cem", VisibilityLevel.OnlyMe, [Role.Member], c);
            await RecordAsync(sp, clock, admin, 1, c);
            await RecordAsync(sp, clock, bea, BeaActivities, c);
            await RecordAsync(sp, clock, cem, CemActivities, c);
            return (admin, manager, bea, cem);
        }, ct);

        var h = await scopes.RunAsync(TenantContext.ForTenant(hoedl), async (sp, c) =>
        {
            var admin = await JoinAsync(sp, "Ben Admin", VisibilityLevel.Company, [Role.Member, Role.TenantAdmin], c);
            var mia = await JoinAsync(sp, "Mia", VisibilityLevel.Company, [Role.Member], c);
            await RecordAsync(sp, clock, mia, MiaActivities, c);
            return (admin, mia);
        }, ct);

        return new TwoTenants(operatorId, wiesner, hoedl, w.admin, w.manager, w.bea, w.cem, h.admin, h.mia);
    }

    /// <summary>Ein eigener aktiver Tenant mit Tenant-Admin, Programm-Manager und zwei Mitgliedern für Tests, die Daten schreiben.</summary>
    public static async Task<ScratchTenant> SeedScratchAsync(IServiceProvider services, string displayName, Guid operatorId)
    {
        var scopes = services.GetRequiredService<ITenantScopeFactory>();
        var ct = CancellationToken.None;
        var tenant = await scopes.RunAsync(TenantContext.ForPlatform(), (sp, c) =>
            sp.GetRequiredService<IOrganisationDirectory>().CreateTenantAsync(displayName, operatorId, c), ct);
        return await scopes.RunAsync(TenantContext.ForTenant(tenant), async (sp, c) =>
        {
            var admin = await JoinAsync(sp, "Admin", VisibilityLevel.Company, [Role.Member, Role.TenantAdmin], c);
            var manager = await JoinAsync(sp, "Manager", VisibilityLevel.Company, [Role.Member, Role.ProgrammeManager], c);
            var a = await JoinAsync(sp, "Mitglied A", VisibilityLevel.Company, [Role.Member], c);
            var b = await JoinAsync(sp, "Mitglied B", VisibilityLevel.OnlyMe, [Role.Member], c);
            return new ScratchTenant(tenant, admin, manager, a, b);
        }, ct);
    }

    private static async Task<PersonId> JoinAsync(IServiceProvider sp, string displayName, VisibilityLevel level, string[] roles, CancellationToken ct)
    {
        // Beitritt erzeugt Person, Mitgliedschaft, Sichtbarkeitswahl (Zugang 2.2); Rollen über Organisation.
        var personId = await sp.GetRequiredService<IPersonDirectory>().CreatePersonAsync(displayName, ct);
        var organisations = sp.GetRequiredService<IOrganisationDirectory>();
        await organisations.AddMemberAsync(personId, ct);
        foreach (var role in roles)
        {
            await organisations.AssignRoleAsync(personId, role, ct);
        }

        await sp.GetRequiredService<IVisibilityChoice>().ChooseAsync(personId, level, ct);
        return personId;
    }

    private static async Task RecordAsync(IServiceProvider sp, FixedClock clock, PersonId personId, int count, CancellationToken ct)
    {
        var recorder = sp.GetRequiredService<IActivityRecorder>();
        for (var i = 0; i < count; i++)
        {
            clock.Advance(TimeSpan.FromMinutes(1));
            await recorder.RecordAsync(personId, "check_in", ActivitySource.Self, clock.GetUtcNow(), ct);
        }
    }
}
