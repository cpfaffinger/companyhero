using CompanyHero.Modules.Feed.Domain;
using CompanyHero.Platform.Tenancy;

namespace CompanyHero.Domain.Tests;

/// <summary>Feed 2.4, 2.5 (A-049, A-022, A-023) als reine Fachregel: Sichtbarkeitskreis, Sammelkarte ab fünf, Deckel je Tag.</summary>
public sealed class FeedAssemblyTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Day = new(2026, 9, 25);

    private static FeedEntry Badge(PersonId person, int minute) =>
        FeedEntry.SystemEvent(Tenant, $"badge:{person.Value:D}:{minute}", FeedTextKeys.BadgeAwarded, new Dictionary<string, string> { ["abzeichen"] = "x" }, Now.AddMinutes(minute), Day, subject: person, referenceKind: "badge");

    [Fact]
    public void Eintraege_mit_Personenbezug_erscheinen_nur_fuer_sichtbare_Personen_oder_die_eigene()
    {
        var reader = PersonId.New();
        var visible = PersonId.New();
        var hidden = PersonId.New();
        var entries = new[] { Badge(reader, 1), Badge(visible, 2), Badge(hidden, 3) };

        var cards = FeedAssembly.Assemble(entries, reader, new HashSet<PersonId> { visible }, 5, 8);

        Assert.Equal(2, cards.Count);
        Assert.DoesNotContain(cards, c => c.Subject == hidden);
        Assert.Contains(cards, c => c.Subject == reader && c.Mine);
    }

    [Fact]
    public void Sammelkarte_Abzeichen_verdient_erst_ab_fuenf_Personen_darunter_nur_individuell_sichtbare()
    {
        var reader = PersonId.New();
        var four = Enumerable.Range(0, 4).Select(_ => PersonId.New()).ToList();
        var below = four.Select((p, i) => Badge(p, i)).ToList();

        var cardsBelow = FeedAssembly.Assemble(below, reader, new HashSet<PersonId>(four.Take(2)), 5, 8);
        Assert.Equal(2, cardsBelow.Count);
        Assert.All(cardsBelow, c => Assert.Equal(FeedAssembly.SystemCard, c.Kind));

        var five = four.Append(PersonId.New()).ToList();
        var atFive = five.Select((p, i) => Badge(p, i)).ToList();
        var cardsAtFive = FeedAssembly.Assemble(atFive, reader, new HashSet<PersonId>(), 5, 8);

        var aggregate = Assert.Single(cardsAtFive);
        Assert.Equal(FeedAssembly.AggregateCard, aggregate.Kind);
        Assert.Equal(FeedTextKeys.BadgesAggregate, aggregate.TextKey);
        Assert.Equal("5", aggregate.Parameters["anzahl"]);
        Assert.Null(aggregate.Subject);
    }

    [Fact]
    public void Hoechstens_acht_Systemkarten_je_Tag_der_Rest_wird_zur_Tageszusammenfassung()
    {
        var reader = PersonId.New();
        var entries = Enumerable.Range(0, 12).Select(i => FeedEntry.SystemEvent(Tenant, $"m:{i}", FeedTextKeys.Milestone, new Dictionary<string, string>(), Now.AddMinutes(i), Day, referenceKind: "challenge", referenceId: Guid.NewGuid())).ToList();

        var cards = FeedAssembly.Assemble(entries, reader, new HashSet<PersonId>(), 5, 8);

        Assert.Equal(8, cards.Count);
        var summary = Assert.Single(cards, c => c.TextKey == FeedTextKeys.DailySummary);
        Assert.Equal("5", summary.Parameters["anzahl"]);
    }

    [Fact]
    public void Mitgliederbeitrag_folgt_dem_Sichtbarkeitskreis_des_Autors_und_zaehlt_nicht_zum_Systemdeckel()
    {
        var reader = PersonId.New();
        var author = PersonId.New();
        var post = FeedEntry.MemberPost(Tenant, author, "Hallo Team", Now, Day);
        var system = Enumerable.Range(0, 9).Select(i => FeedEntry.SystemEvent(Tenant, $"s:{i}", FeedTextKeys.ChallengeStarted, new Dictionary<string, string>(), Now.AddMinutes(i), Day)).ToList();

        var hidden = FeedAssembly.Assemble(system.Append(post), reader, new HashSet<PersonId>(), 5, 8);
        Assert.DoesNotContain(hidden, c => c.Kind == FeedAssembly.MemberPostCard);

        var shown = FeedAssembly.Assemble(system.Append(post), reader, new HashSet<PersonId> { author }, 5, 8);
        Assert.Single(shown, c => c.Kind == FeedAssembly.MemberPostCard && c.Body == "Hallo Team");
        Assert.Equal(9, shown.Count);
    }

    [Fact]
    public void Beitrag_braucht_Text_und_hoechstens_1000_Zeichen()
    {
        Assert.Throws<ArgumentException>(() => FeedEntry.MemberPost(Tenant, PersonId.New(), "   ", Now, Day));
        Assert.Throws<ArgumentException>(() => FeedEntry.MemberPost(Tenant, PersonId.New(), new string('a', 1001), Now, Day));
        Assert.Equal(1000, FeedEntry.MemberPost(Tenant, PersonId.New(), new string('a', 1000), Now, Day).Body!.Length);
    }
}
