using CompanyHero.Modules.Organisation.Domain;
using CompanyHero.Platform.Tenancy;
using Organisation = CompanyHero.Modules.Organisation.Domain.Organisation;

namespace CompanyHero.Domain.Tests;

/// <summary>Organisation 1.1, 1.3 und 4.1: Hierarchie mit drei Ebenen, Aktivierung durch den ersten Tenant-Admin, Rollen aus dem Katalog.</summary>
public sealed class OrganisationRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Hierarchie_hat_hoechstens_drei_Ebenen()
    {
        var operatorOrganisation = Organisation.CreateOperator("Betreiber", Now);
        var partner = Organisation.CreatePartner("Partner", operatorOrganisation, Now);
        var tenant = Organisation.CreateTenant("Tenant", partner, Now);

        Assert.Equal(OrganisationType.Tenant, tenant.Type);
        Assert.Equal(partner.Id, tenant.ParentId);
        Assert.Throws<OrganisationHierarchyException>(() => Organisation.CreateTenant("Vierte Ebene", tenant, Now));
        Assert.Throws<OrganisationHierarchyException>(() => Organisation.CreatePartner("Sub-Partner", partner, Now));
    }

    [Fact]
    public void Tenant_wird_erst_mit_dem_ersten_Tenant_Admin_aktiv()
    {
        var tenant = Organisation.CreateTenant("Tenant", Organisation.CreateOperator("Betreiber", Now), Now);
        Assert.Equal(OrganisationState.Provisioned, tenant.State);
        Assert.False(tenant.GrantsMemberAccess);

        tenant.ActivateOnFirstTenantAdmin();

        Assert.Equal(OrganisationState.Active, tenant.State);
        Assert.True(tenant.GrantsMemberAccess);
    }

    [Fact]
    public void Mitgliedschaft_haelt_jede_Rolle_hoechstens_einmal_und_nur_aus_dem_Katalog()
    {
        var membership = Membership.Join(TenantId.New(), PersonId.New(), Now);

        var first = membership.Assign(Role.Member, Now);
        var again = membership.Assign(Role.Member, Now.AddDays(1));
        membership.Assign(Role.TenantAdmin, Now);

        Assert.Same(first, again);
        Assert.Equal(2, membership.Roles.Count);
        Assert.Throws<ArgumentException>(() => membership.Assign("superuser", Now));
        Assert.Throws<ArgumentException>(() => membership.Assign(Role.OperatorAdmin, Now));
    }

    [Fact]
    public void Funktionsrollen_des_Arbeitgebers_sind_benannt()
    {
        Assert.True(Role.IsEmployerRole(Role.TenantAdmin));
        Assert.True(Role.IsEmployerRole(Role.ProgrammeManager));
        Assert.False(Role.IsEmployerRole(Role.Member));
        Assert.False(Role.IsEmployerRole(Role.HealthAmbassador));
    }
}
