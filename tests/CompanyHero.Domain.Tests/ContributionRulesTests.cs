using CompanyHero.Modules.Challenges.Domain;
using CompanyHero.Platform.Tenancy;

namespace CompanyHero.Domain.Tests;

/// <summary>Challenges 3 (A-039) als reine Fachregeln: Zukunft, Rückdatierung, Zeitraum, Nachfrist, Wert je Erfassungsart.</summary>
public sealed class ContributionRulesTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = new(2026, 9, 30, 22, 0, 0, TimeSpan.Zero);
    private static readonly Challenge Running = Challenge.StartRunning(TenantId.New(), "Schritte", ChallengeMetric.Checkmark, 100m, Start, End, Start);

    [Fact]
    public void Beitrag_im_Zeitraum_mit_gueltigem_Wert_wird_angenommen()
    {
        var now = new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(ContributionRejection.None, ContributionRules.Validate(Running, ChallengeMetric.Checkmark, 1m, now, now));
    }

    [Fact]
    public void Zukunft_wird_abgelehnt()
    {
        var now = new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(ContributionRejection.InFuture, ContributionRules.Validate(Running, ChallengeMetric.Checkmark, 1m, now.AddMinutes(1), now));
    }

    [Fact]
    public void Rueckdatierung_um_drei_Kalendertage_ist_erlaubt_um_vier_nicht()
    {
        var received = new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(ContributionRejection.None, ContributionRules.Validate(Running, ChallengeMetric.Checkmark, 1m, received.AddDays(-3), received));
        Assert.Equal(ContributionRejection.BackdatedTooFar, ContributionRules.Validate(Running, ChallengeMetric.Checkmark, 1m, received.AddDays(-4), received));
    }

    [Fact]
    public void Nachfrist_von_48_Stunden_nimmt_verspaetete_Offline_Beitraege_an_danach_nicht()
    {
        var recorded = End.AddHours(-1);
        Assert.Equal(ContributionRejection.None, ContributionRules.Validate(Running, ChallengeMetric.Checkmark, 1m, recorded, End.AddHours(40)));
        Assert.Equal(ContributionRejection.GracePeriodOver, ContributionRules.Validate(Running, ChallengeMetric.Checkmark, 1m, recorded, End.AddHours(50)));
    }

    [Fact]
    public void Erfassungszeitpunkt_ausserhalb_des_Zeitraums_wird_abgelehnt()
    {
        var received = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(ContributionRejection.OutsidePeriod, ContributionRules.Validate(Running, ChallengeMetric.Checkmark, 1m, End.AddHours(1), received));
    }

    [Fact]
    public void Haekchen_tragen_genau_1_Zahlen_sind_positiv()
    {
        var now = new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(ContributionRejection.InvalidValue, ContributionRules.Validate(Running, ChallengeMetric.Checkmark, 2m, now, now));
        Assert.Equal(ContributionRejection.InvalidValue, ContributionRules.Validate(Running, ChallengeMetric.Count, 0m, now, now));
        Assert.Equal(ContributionRejection.None, ContributionRules.Validate(Running, ChallengeMetric.Count, 0.25m, now, now));
    }

    [Fact]
    public void Kennungen_beider_Regime_sind_zeitlich_sortierbar_und_der_Inhalt_wird_kanonisch_gehasht()
    {
        var operation = ContributionKey.ReserveOperation(TenantId.New(), PersonId.New(), Start);
        Assert.True(ContributionKey.IsValidClientKey(operation.Key));
        Assert.False(ContributionKey.IsValidClientKey(Guid.NewGuid().ToString("D")));
        Assert.False(ContributionKey.IsValidClientKey("nicht-sortierbar"));

        var challenge = Guid.CreateVersion7();
        var at = new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.FromHours(2));
        var a = ContributionKey.HashContent(challenge, 1.5m, at, ContributionChannel.Mobile);
        var same = ContributionKey.HashContent(challenge, 1.50m, at.ToUniversalTime(), ContributionChannel.Mobile);
        var different = ContributionKey.HashContent(challenge, 1.5m, at.AddSeconds(1), ContributionChannel.Mobile);
        Assert.Equal(a, same);
        Assert.NotEqual(a, different);

        operation.Commit(Guid.CreateVersion7(), a, Start);
        Assert.True(operation.MatchesContent(same));
        Assert.Throws<InvalidOperationException>(() => operation.Abort());
    }
}
