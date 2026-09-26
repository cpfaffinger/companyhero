using CompanyHero.Modules.Organisation.Domain;
using CompanyHero.Platform.Tenancy;

namespace CompanyHero.Domain.Tests;

/// <summary>Organisation 3 und 7 (A-034, A-036): Dimensionen, Gruppen, Sollstärke, Tenant-Lebenszyklus als reine Fachregeln.</summary>
public sealed class GroupRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 8, 0, 0, TimeSpan.Zero);
    private static readonly TenantId Tenant = TenantId.New();

    [Fact]
    public void Sollstaerke_gilt_ab_Stichtag_und_warnt_unter_fuenf()
    {
        var entries = new[]
        {
            HeadcountEntry.Record(Tenant, null, new DateOnly(2026, 9, 1), 40, Now),
            HeadcountEntry.Record(Tenant, null, new DateOnly(2026, 10, 1), 4, Now),
        };

        Assert.Null(HeadcountEntry.EffectiveAt(entries, new DateOnly(2026, 8, 31)));
        Assert.Equal(40, HeadcountEntry.EffectiveAt(entries, new DateOnly(2026, 9, 15)));
        Assert.Equal(4, HeadcountEntry.EffectiveAt(entries, new DateOnly(2026, 10, 1)));
        Assert.True(GroupRules.WarnsAboutHeadcount(4));
        Assert.False(GroupRules.WarnsAboutHeadcount(5));
        Assert.False(GroupRules.WarnsAboutHeadcount(null));
        Assert.Throws<ArgumentOutOfRangeException>(() => HeadcountEntry.Record(Tenant, null, new DateOnly(2026, 9, 1), -1, Now));
    }

    [Fact]
    public void Archivierte_Gruppe_ist_nicht_mehr_waehlbar_und_Wechsel_bleibt_nur_in_der_Dimension()
    {
        var dimension = GroupDimension.Create(Tenant, "Standort", 1, Now);
        var other = GroupDimension.Create(Tenant, "Abteilung", 2, Now);
        var wien = MemberGroup.Create(Tenant, dimension, "Wien", Now);
        var graz = MemberGroup.Create(Tenant, dimension, "Graz", Now);
        var it = MemberGroup.Create(Tenant, other, "IT", Now);
        var person = PersonId.New();

        var membership = GroupMembership.Choose(Tenant, person, wien, Now);
        membership.Change(graz, Now.AddDays(1));
        Assert.Equal(graz.Id, membership.GroupId);
        Assert.Throws<OrganisationHierarchyException>(() => membership.Change(it, Now));

        graz.Archive(Now.AddDays(2));
        Assert.False(graz.IsSelectable);
        Assert.Throws<OrganisationHierarchyException>(() => GroupMembership.Choose(Tenant, PersonId.New(), graz, Now));
    }

    [Fact]
    public void Tenant_Lebenszyklus_Sperre_Kuendigung_Lesefenster_Loeschung()
    {
        var tenant = Organisation.CreateTenant("Wiesner", Organisation.CreateOperator("Betreiber", Now), Now, "Europe/Vienna");
        tenant.ActivateOnFirstTenantAdmin();
        Assert.True(tenant.GrantsMemberAccessAt(Now));

        tenant.Suspend("Zahlungsverzug", Now);
        Assert.False(tenant.GrantsMemberAccessAt(Now));
        Assert.Equal("tenant_suspended", tenant.AccessDeniedReasonAt(Now));
        tenant.Unsuspend();
        Assert.True(tenant.GrantsMemberAccessAt(Now));

        tenant.Terminate(Now);
        // Wirksam zum Monatsende in der Tenant-Zeitzone: 30.09.2026 24:00 Wien = 22:00 UTC.
        Assert.Equal(new DateTimeOffset(2026, 9, 30, 22, 0, 0, TimeSpan.Zero), tenant.TerminationEffectiveAt);
        Assert.True(tenant.GrantsMemberAccessAt(Now));
        var afterEnd = tenant.TerminationEffectiveAt!.Value.AddDays(1);
        Assert.False(tenant.GrantsMemberAccessAt(afterEnd));
        Assert.True(tenant.GrantsReadOnlyAccessAt(afterEnd));
        Assert.Equal("tenant_terminated", tenant.AccessDeniedReasonAt(afterEnd));
        var afterWindow = tenant.TerminationEffectiveAt.Value.AddDays(91);
        Assert.False(tenant.GrantsReadOnlyAccessAt(afterWindow));
        Assert.True(tenant.IsDueForDeletionAt(afterWindow));
        Assert.False(tenant.IsDueForDeletionAt(afterEnd));
    }
}
