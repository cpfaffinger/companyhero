using System.Text.Json;
using CompanyHero.Platform.Tenancy;

namespace CompanyHero.Modules.Feed.Domain;

/// <summary>Kartentypen des Durchstichs (Feed 2.2): System-Ereignis, Challenge-Stand als Kopfkarte, Mitglieder-Beitrag. Inhalte und Tenant-Beiträge folgen später.</summary>
public enum FeedEntryKind
{
    System = 1,
    MemberPost = 3,
}

/// <summary>Geltungsbereich (Feed 2.4): ganzer Tenant oder Gruppen; Arena folgt mit Stufe 8.</summary>
public enum FeedScope
{
    Tenant = 1,
    Groups = 2,
}

/// <summary>Textschlüssel der Feed-Karten; die Texte kommen aus dem Katalog des Operators in Tonalität und Anrede des Tenants (Marke 6.2).</summary>
public static class FeedTextKeys
{
    public const string ChallengeStarted = "feed.challengeGestartet";
    public const string Milestone = "feed.meilenstein";
    public const string ChallengeEnded = "feed.challengeBeendet";
    public const string BadgeAwarded = "feed.abzeichenVerdient";
    public const string BadgesAggregate = "feed.abzeichenSammel";
    public const string LevelReached = "feed.stufeErreicht";
    public const string CheckIn = "feed.checkin";
    public const string DailySummary = "feed.tageszusammenfassung";
    public const string MemberPost = "feed.mitgliedsbeitrag";
}

/// <summary>Regeln des Feeds (Feed 2.5, 3.2): Längen, Deckel, Aufbewahrung.</summary>
public static class FeedRules
{
    public const int MaxPostLength = 1000;

    public const int MaxPostsPerPersonAndDay = 5;

    /// <summary>Höchstens acht System-Karten je Tag und Tenant; überzählige werden in eine Tageszusammenfassung gefaltet (Feed 2.5).</summary>
    public const int MaxSystemCardsPerDay = 8;

    public static TimeSpan Retention { get; } = TimeSpan.FromDays(365);

    public const int PageSize = 50;

    public static string ValidatePost(string? body)
    {
        var text = (body ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            throw new ArgumentException("Ein Beitrag braucht Text.", nameof(body));
        }

        if (text.Length > MaxPostLength)
        {
            throw new ArgumentException($"Höchstens {MaxPostLength} Zeichen (Feed 3.2).", nameof(body));
        }

        return text;
    }
}

/// <summary>
/// Ein Feed-Eintrag (Feed 2.1): entsteht aus Fachereignissen der Domänen oder als Mitglieder-Beitrag. Er trägt nie Anzeigenamen
/// und nie Messwerte Dritter; die Sichtbarkeit der Person wird beim Lesen über die zentrale Leseregel angewandt, damit ein
/// Stufenwechsel rückwirkend wirkt (A-022). Die Ereigniskennung macht die Projektion idempotent (Backend 6.3).
/// </summary>
public sealed class FeedEntry : ITenantOwned
{
    private FeedEntry(TenantId tenantId, Guid id, FeedEntryKind kind, string eventKey, string textKey, string paramsJson, PersonId? subjectPersonId, FeedScope scope, List<Guid> groupIds, string? referenceKind, Guid? referenceId, string? body, DateTimeOffset occurredAt, DateOnly day)
    {
        TenantId = tenantId;
        Id = id;
        Kind = kind;
        EventKey = eventKey;
        TextKey = textKey;
        ParamsJson = paramsJson;
        SubjectPersonId = subjectPersonId;
        Scope = scope;
        GroupIds = groupIds;
        ReferenceKind = referenceKind;
        ReferenceId = referenceId;
        Body = body;
        OccurredAt = occurredAt;
        Day = day;
    }

    public TenantId TenantId { get; }

    public Guid Id { get; }

    public FeedEntryKind Kind { get; }

    /// <summary>Eindeutig je Tenant: Ereigniskennung der Quelle oder die eigene Kennung eines Beitrags.</summary>
    public string EventKey { get; }

    public string TextKey { get; }

    /// <summary>Platzhalter der Karte als JSON-Objekt aus Strings (Titel, Prozent, Abzeichen); nie Werte Dritter.</summary>
    public string ParamsJson { get; }

    /// <summary>Die Person, um die es geht (Abzeichen, Stufe, Check-in, Autor); <c>null</c> bei Ereignissen ohne Personenbezug.</summary>
    public PersonId? SubjectPersonId { get; private set; }

    public FeedScope Scope { get; }

    public List<Guid> GroupIds { get; }

    public string? ReferenceKind { get; }

    public Guid? ReferenceId { get; }

    public string? Body { get; private set; }

    public DateTimeOffset OccurredAt { get; }

    /// <summary>Kalendertag in der Zeitzone des Tenants für Tagesüberschriften und Verdichtung (Feed 2.5).</summary>
    public DateOnly Day { get; }

    public static FeedEntry SystemEvent(TenantId tenantId, string eventKey, string textKey, IReadOnlyDictionary<string, string> parameters, DateTimeOffset occurredAt, DateOnly day, PersonId? subject = null, string? referenceKind = null, Guid? referenceId = null, FeedScope scope = FeedScope.Tenant, IEnumerable<Guid>? groupIds = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(textKey);
        return new FeedEntry(tenantId, Guid.CreateVersion7(), FeedEntryKind.System, eventKey, textKey, JsonSerializer.Serialize(parameters, FeedJson.Options), subject, scope, groupIds?.ToList() ?? [], referenceKind, referenceId, null, occurredAt.ToUniversalTime(), day);
    }

    /// <summary>Mitglieder-Beitrag (Feed 3.2): Reichweite ist der Kreis der Sichtbarkeitsstufe des Autors, beim Lesen entschieden.</summary>
    public static FeedEntry MemberPost(TenantId tenantId, PersonId author, string body, DateTimeOffset now, DateOnly day)
    {
        var id = Guid.CreateVersion7();
        return new FeedEntry(tenantId, id, FeedEntryKind.MemberPost, $"post:{id:D}", FeedTextKeys.MemberPost, "{}", author, FeedScope.Tenant, [], null, null, FeedRules.ValidatePost(body), now.ToUniversalTime(), day);
    }

    public IReadOnlyDictionary<string, string> Parameters() =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(ParamsJson, FeedJson.Options) ?? new Dictionary<string, string>(StringComparer.Ordinal);
}

internal static class FeedJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web);
}

/// <summary>Eine Karte, wie sie die Startseite zeigt; Ergebnis der Verdichtung.</summary>
public sealed record FeedCard(Guid Id, string Kind, DateTimeOffset OccurredAt, DateOnly Day, string TextKey, IReadOnlyDictionary<string, string> Parameters, PersonId? Subject, string? ReferenceKind, Guid? ReferenceId, string? Body, bool Mine);

/// <summary>
/// Verdichtung und Leseregel des Streams (Feed 2.4, 2.5, A-022, A-023) als reine Fachregel: Einträge mit Personenbezug erscheinen
/// nur, wenn die Person für die lesende Person sichtbar ist oder es die eigene ist; gleichartige Abzeichen-Ereignisse eines Tages
/// werden ab fünf Personen zu einer Sammelkarte, darunter bleiben nur die ohnehin sichtbaren; höchstens acht System-Karten je Tag.
/// </summary>
public static class FeedAssembly
{
    public const string SystemCard = "system";
    public const string AggregateCard = "aggregate";
    public const string MemberPostCard = "member_post";

    public static IReadOnlyList<FeedCard> Assemble(IEnumerable<FeedEntry> entries, PersonId reader, IReadOnlySet<PersonId> visibleSubjects, int minimumPersons, int maxSystemCardsPerDay)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(visibleSubjects);
        var cards = new List<FeedCard>();
        foreach (var day in entries.GroupBy(e => e.Day).OrderByDescending(g => g.Key))
        {
            var dayCards = new List<FeedCard>();
            var badges = day.Where(e => e.TextKey == FeedTextKeys.BadgeAwarded).ToList();
            var badgePersons = badges.Select(b => b.SubjectPersonId).Where(p => p is not null).Distinct().Count();
            if (badgePersons >= minimumPersons)
            {
                var latest = badges.MaxBy(b => b.OccurredAt)!;
                dayCards.Add(new FeedCard(latest.Id, AggregateCard, latest.OccurredAt, day.Key, FeedTextKeys.BadgesAggregate, new Dictionary<string, string>(StringComparer.Ordinal) { ["anzahl"] = badgePersons.ToString(System.Globalization.CultureInfo.InvariantCulture) }, null, null, null, null, false));
            }

            foreach (var entry in day.OrderByDescending(e => e.OccurredAt).ThenByDescending(e => e.Id))
            {
                if (badgePersons >= minimumPersons && entry.TextKey == FeedTextKeys.BadgeAwarded)
                {
                    continue;
                }

                if (entry.SubjectPersonId is { } subject && subject != reader && !visibleSubjects.Contains(subject))
                {
                    continue;
                }

                var mine = entry.SubjectPersonId == reader;
                dayCards.Add(new FeedCard(entry.Id, entry.Kind == FeedEntryKind.MemberPost ? MemberPostCard : SystemCard, entry.OccurredAt, day.Key, entry.TextKey, entry.Parameters(), entry.SubjectPersonId, entry.ReferenceKind, entry.ReferenceId, entry.Body, mine));
            }

            var system = dayCards.Where(c => c.Kind != MemberPostCard).ToList();
            if (system.Count > maxSystemCardsPerDay)
            {
                var kept = system.Take(maxSystemCardsPerDay - 1).ToHashSet();
                var folded = system.Count - kept.Count;
                var last = system[maxSystemCardsPerDay - 1];
                dayCards = dayCards.Where(c => c.Kind == MemberPostCard || kept.Contains(c)).ToList();
                dayCards.Add(new FeedCard(last.Id, AggregateCard, last.OccurredAt, day.Key, FeedTextKeys.DailySummary, new Dictionary<string, string>(StringComparer.Ordinal) { ["anzahl"] = folded.ToString(System.Globalization.CultureInfo.InvariantCulture) }, null, null, null, null, false));
            }

            cards.AddRange(dayCards.OrderByDescending(c => c.OccurredAt).ThenByDescending(c => c.Id));
        }

        return cards;
    }
}
