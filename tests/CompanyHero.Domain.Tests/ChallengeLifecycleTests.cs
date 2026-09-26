using CompanyHero.Modules.Challenges.Domain;
using CompanyHero.Modules.Privacy.Domain;
using CompanyHero.Platform.Tenancy;

namespace CompanyHero.Domain.Tests;

/// <summary>Challenges 4 (A-038, A-040): Entwurf, Vorschau als Pflicht, Geplant, Laufend, Nachfrist, Beendet; Kickoff-Vorbelegung; Mindestzahl.</summary>
public sealed class ChallengeLifecycleTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 8, 0, 0, TimeSpan.Zero);
    private static readonly TimeZoneInfo Vienna = TenantTimeZone.Resolve("Europe/Vienna");

    private static Challenge Draft() =>
        Challenge.CreateDraft(TenantId.New(), new ChallengeDraft("Rad zur Arbeit", null, ChallengeMetric.Checkmark, 100m, Now.AddDays(3), Now.AddDays(31), ChallengeVisibility.Company, null), PersonId.New(), Now);

    [Fact]
    public void Planen_ohne_Vorschau_wird_abgelehnt()
    {
        var challenge = Draft();
        var ex = Assert.Throws<ChallengeLifecycleException>(() => challenge.Plan(Now));
        Assert.Equal("preview_required", ex.Reason);
        challenge.MarkPreviewed(Now);
        challenge.Plan(Now);
        Assert.Equal(ChallengeState.Planned, challenge.State);
    }

    [Fact]
    public void Zeitgesteuerte_Uebergaenge_Geplant_Laufend_Nachfrist_Beendet()
    {
        var challenge = Draft();
        challenge.MarkPreviewed(Now);
        challenge.Plan(Now);
        Assert.False(challenge.StartIfDue(challenge.StartsAt.AddSeconds(-1)));
        Assert.True(challenge.StartIfDue(challenge.StartsAt));
        Assert.True(challenge.AcceptsContributions);
        Assert.False(challenge.EnterGraceIfDue(challenge.EndsAt.AddSeconds(-1)));
        Assert.True(challenge.EnterGraceIfDue(challenge.EndsAt));
        Assert.True(challenge.AcceptsContributions);
        Assert.False(challenge.EndIfDue(challenge.EndsAt.AddHours(47)));
        Assert.True(challenge.EndIfDue(challenge.EndsAt.AddHours(48)));
        Assert.False(challenge.AcceptsContributions);
        Assert.False(challenge.ArchiveIfDue(challenge.EndsAt.AddDays(100)));
        Assert.True(challenge.ArchiveIfDue(challenge.EndsAt.AddDays(367)));
    }

    [Fact]
    public void Vorzeitiges_Ende_braucht_Begruendung_und_startet_die_Nachfrist_sofort()
    {
        var challenge = Challenge.StartRunning(TenantId.New(), "Schritte", ChallengeMetric.Count, 1000m, Now.AddDays(-5), Now.AddDays(20), Now);
        Assert.Throws<ArgumentException>(() => challenge.EndEarly(" ", Now));
        challenge.EndEarly("Betriebsversammlung", Now);
        Assert.Equal(ChallengeState.Grace, challenge.State);
        Assert.Equal(Now, challenge.EndsAt);
        Assert.Equal(Now.AddHours(48), challenge.AcceptsContributionsUntil);
        Assert.Throws<ChallengeLifecycleException>(() => challenge.EndEarly("nochmal", Now));
    }

    [Fact]
    public void Kickoff_Entwurf_startet_am_naechsten_Montag_vier_Wochen_mit_Ziel_aus_der_Sollstaerke()
    {
        // 25.09.2026 ist ein Freitag; nächster Montag 28.09.2026 00:00 Wien = 27.09. 22:00 UTC.
        var draft = Challenge.KickoffDraft(Now, Vienna, 40);
        Assert.Equal(new DateTimeOffset(2026, 9, 27, 22, 0, 0, TimeSpan.Zero), draft.StartsAt);
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 22, 59, 59, TimeSpan.Zero), draft.EndsAt);
        Assert.Equal(800m, draft.Target);
        Assert.Equal(ChallengeMetric.Checkmark, draft.Metric);
        Assert.Equal(ChallengeVisibility.Company, draft.Visibility);
        Assert.Equal(Challenge.KickoffTemplate, draft.TemplateKey);
        Assert.Equal(400m, Challenge.KickoffDraft(Now, Vienna, 3).Target);
    }

    [Fact]
    public void Korrektur_als_Gegenbuchung_hoechstens_einmal_und_nur_bis_zum_Ende_der_Nachfrist()
    {
        var challenge = Challenge.StartRunning(TenantId.New(), "Schritte", ChallengeMetric.Checkmark, 100m, Now.AddDays(-5), Now.AddDays(1), Now);
        var contribution = Contribution.Record(challenge.TenantId, challenge.Id, PersonId.New(), 1m, Now, Now, ContributionChannel.Mobile);
        var reversal = contribution.Reverse(Now.AddMinutes(1));
        Assert.Equal(-1m, reversal.Value);
        Assert.True(reversal.IsReversal);
        Assert.True(contribution.Reversed);
        Assert.Throws<ChallengeLifecycleException>(() => contribution.Reverse(Now));
        Assert.Throws<ChallengeLifecycleException>(() => reversal.Reverse(Now));
        Assert.True(ContributionRules.MayReverse(challenge, challenge.EndsAt.AddHours(47)));
        challenge.EnterGraceIfDue(challenge.EndsAt);
        Assert.True(ContributionRules.MayReverse(challenge, challenge.EndsAt.AddHours(47)));
        Assert.False(ContributionRules.MayReverse(challenge, challenge.EndsAt.AddHours(49)));
    }

    [Fact]
    public void Beitraege_werden_in_der_Nachfrist_angenommen_und_nach_ihrem_Ende_abgelehnt()
    {
        var challenge = Challenge.StartRunning(TenantId.New(), "Schritte", ChallengeMetric.Checkmark, 100m, Now.AddDays(-5), Now, Now);
        challenge.EnterGraceIfDue(Now);
        Assert.Equal(ContributionRejection.None, ContributionRules.Validate(challenge, ChallengeMetric.Checkmark, 1m, Now.AddHours(-1), Now.AddHours(30), Vienna));
        Assert.Equal(ContributionRejection.GracePeriodOver, ContributionRules.Validate(challenge, ChallengeMetric.Checkmark, 1m, Now.AddHours(-1), Now.AddHours(49), Vienna));
    }

    [Fact]
    public void Aggregate_erst_ab_fuenf_Personen_Rangliste_zusaetzlich_ab_40_Prozent()
    {
        Assert.False(AggregateRule.MayPublish(4));
        Assert.True(AggregateRule.MayPublish(5));
        Assert.False(AggregateRule.MayAggregatePersons(4));
        Assert.False(AggregateRule.MayRenderRanking(5, 20));
        Assert.True(AggregateRule.MayRenderRanking(8, 20));
    }

    [Fact]
    public void Sichtbarkeitsregel_Praesenz_im_Kreis_individuelle_Werte_nie_fuer_Arbeitgeberrollen()
    {
        var reader = PersonId.New();
        var subject = PersonId.New();
        Assert.True(VisibilityRule.IsVisible(reader, true, subject, VisibilityLevel.Company, false, VisibilityPurpose.Presence));
        Assert.False(VisibilityRule.IsVisible(reader, true, subject, VisibilityLevel.Company, false, VisibilityPurpose.IndividualValues));
        Assert.True(VisibilityRule.IsVisible(reader, false, subject, VisibilityLevel.Team, true, VisibilityPurpose.Presence));
        Assert.False(VisibilityRule.IsVisible(reader, false, subject, VisibilityLevel.Team, false, VisibilityPurpose.Presence));
        Assert.False(VisibilityRule.IsVisible(reader, false, subject, null, true, VisibilityPurpose.Presence));
        Assert.True(VisibilityRule.IsVisible(subject, true, subject, VisibilityLevel.OnlyMe, false, VisibilityPurpose.IndividualValues));
    }
}
