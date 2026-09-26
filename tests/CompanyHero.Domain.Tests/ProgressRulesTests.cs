using CompanyHero.Modules.Progress.Domain;
using CompanyHero.Platform.Tenancy;

namespace CompanyHero.Domain.Tests;

/// <summary>Fortschritt 2 bis 5 (A-044 bis A-047) als reine Fachregeln: Punkte mit Deckeln, Stufen ohne Verlust, Serie mit Schutz, Abzeichen.</summary>
public sealed class ProgressRulesTests
{
    [Fact]
    public void Jede_Handlung_zehn_Punkte_mit_Tagesdeckel_gesamt_und_je_Art()
    {
        Assert.Equal(10, PointsRules.PointsFor(ActivityKinds.ChallengeContribution, 0, 0));
        Assert.Equal(10, PointsRules.PointsFor(ActivityKinds.ChallengeContribution, 2, 2));
        Assert.Equal(0, PointsRules.PointsFor(ActivityKinds.ChallengeContribution, 3, 3));
        Assert.Equal(0, PointsRules.PointsFor(ActivityKinds.CheckIn, 1, 1));
        Assert.Equal(0, PointsRules.PointsFor(ActivityKinds.RecognitionGiven, 10, 0));
        Assert.Equal(0, PointsRules.ApplyToBalance(5, -10));
    }

    [Fact]
    public void Stufe_folgt_den_Schwellen_und_sinkt_nie()
    {
        Assert.Equal(1, LevelRules.LevelFor(299));
        Assert.Equal(2, LevelRules.LevelFor(300));
        Assert.Equal(5, LevelRules.LevelFor(10000));
        Assert.Equal(3, LevelRules.Keep(3, 100));
    }

    [Fact]
    public void Serie_ueberbrueckt_einen_Tag_je_Monat_und_bricht_beim_zweiten()
    {
        var d1 = new DateOnly(2026, 9, 1);
        var (c1, l1, p1) = StreakRules.Extend(0, 0, null, d1, null);
        var (c2, l2, p2) = StreakRules.Extend(c1, l1, d1, d1.AddDays(1), p1);
        var (c3, l3, p3) = StreakRules.Extend(c2, l2, d1.AddDays(1), d1.AddDays(3), p2);
        Assert.Equal(3, c3);
        Assert.Equal(new DateOnly(2026, 9, 1), p3);
        var (c4, _, _) = StreakRules.Extend(c3, l3, d1.AddDays(3), d1.AddDays(5), p3);
        Assert.Equal(1, c4);

        var recomputed = StreakRules.Recompute([d1, d1.AddDays(1), d1.AddDays(3)]);
        Assert.Equal(3, recomputed.Current);
    }

    [Fact]
    public void Gegenereignis_senkt_Punkte_bis_null_aber_nie_die_Stufe()
    {
        var progress = PersonProgress.Start(TenantId.New(), PersonId.New());
        var day = new DateOnly(2026, 9, 1);
        int? reached = null;
        for (var i = 0; i < 30; i++)
        {
            reached = progress.Apply(10, day.AddDays(i)) ?? reached;
        }

        Assert.Equal(300, progress.PointsBalance);
        Assert.Equal(2, progress.Level);
        Assert.Equal(2, reached);

        progress.Reverse(-10, Enumerable.Range(0, 29).Select(i => day.AddDays(i)));
        Assert.Equal(290, progress.PointsBalance);
        Assert.Equal(2, progress.Level);
        Assert.Equal(29, progress.CurrentStreak);
    }

    [Fact]
    public void Abzeichen_erster_Tag_immer_und_genau_ein_naechstes_je_Kategorie()
    {
        var earned = BadgeCatalog.Earned(new BadgeProgressSnapshot(0, 0, 0, false, false, 0)).ToList();
        Assert.Equal([BadgeCatalog.ErsterTag], earned);

        var next = BadgeCatalog.NextPerCategory(new HashSet<string> { BadgeCatalog.ErsterTag });
        Assert.Equal(4, next.Count);
        Assert.Contains(next, b => b.Key == BadgeCatalog.ErsteUebung);
        Assert.Contains(next, b => b.Key == BadgeCatalog.Serie7);
        Assert.Contains(next, b => b.Key == BadgeCatalog.Firmenziel);
        Assert.DoesNotContain(next, b => b.Key == BadgeCatalog.Serie30);

        var collective = BadgeCatalog.Earned(new BadgeProgressSnapshot(3, 7, 2, true, true, 0)).ToList();
        Assert.Contains(BadgeCatalog.Firmenziel, collective);
        Assert.Contains(BadgeCatalog.Serie7, collective);
        Assert.Contains(BadgeCatalog.ErsteGruppe, collective);
    }
}
