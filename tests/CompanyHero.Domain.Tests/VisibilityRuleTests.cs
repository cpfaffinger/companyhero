using CompanyHero.Modules.Privacy.Domain;
using CompanyHero.Platform.Tenancy;

namespace CompanyHero.Domain.Tests;

/// <summary>Datenschutz 3.3 und A-021 Regel 1 als reine Fachregel: die restriktivere Einstellung gewinnt, der Arbeitgeber sieht nie Einzelwerte.</summary>
public sealed class VisibilityRuleTests
{
    private static readonly PersonId Reader = PersonId.New();
    private static readonly PersonId Subject = PersonId.New();

    [Fact]
    public void Eigene_Werte_sind_immer_lesbar()
    {
        Assert.True(VisibilityRule.MayReadIndividualValues(Reader, readerHoldsEmployerRole: true, Reader, null, shareGroup: false));
        Assert.True(VisibilityRule.MayReadIndividualValues(Reader, readerHoldsEmployerRole: false, Reader, VisibilityLevel.OnlyMe, shareGroup: false));
    }

    [Theory]
    [InlineData(VisibilityLevel.OnlyMe)]
    [InlineData(VisibilityLevel.Team)]
    [InlineData(VisibilityLevel.Company)]
    public void Funktionsrollen_des_Arbeitgebers_sehen_nie_Einzelwerte_anderer(VisibilityLevel level)
    {
        Assert.False(VisibilityRule.MayReadIndividualValues(Reader, readerHoldsEmployerRole: true, Subject, level, shareGroup: true));
    }

    [Fact]
    public void Nur_fuer_mich_und_fehlende_Wahl_sind_fuer_andere_geschlossen()
    {
        Assert.False(VisibilityRule.MayReadIndividualValues(Reader, false, Subject, VisibilityLevel.OnlyMe, shareGroup: true));
        Assert.False(VisibilityRule.MayReadIndividualValues(Reader, false, Subject, null, shareGroup: true));
    }

    [Fact]
    public void Mein_Team_ist_nur_mit_gemeinsamer_Gruppe_offen()
    {
        Assert.True(VisibilityRule.MayReadIndividualValues(Reader, false, Subject, VisibilityLevel.Team, shareGroup: true));
        Assert.False(VisibilityRule.MayReadIndividualValues(Reader, false, Subject, VisibilityLevel.Team, shareGroup: false));
    }

    [Fact]
    public void Ganze_Firma_ist_fuer_Mitglieder_offen()
    {
        Assert.True(VisibilityRule.MayReadIndividualValues(Reader, false, Subject, VisibilityLevel.Company, shareGroup: false));
    }
}
