using CompanyHero.Platform.Tenancy;

namespace CompanyHero.Modules.Progress.Domain;

/// <summary>Kategorien des Plattformkatalogs (Fortschritt 4.1).</summary>
public enum BadgeCategory
{
    Einstieg = 1,
    Dranbleiben = 2,
    Gemeinsam = 3,
    Vielfalt = 4,
    Saison = 5,
}

/// <summary>Grundlage der Regel eines Abzeichens; Regeln beziehen sich nie auf Messwerte (Fortschritt 4.1).</summary>
public sealed record BadgeDefinition(string Key, BadgeCategory Category, int Order, string TextKey);

/// <summary>Sicht einer Person auf ihre Abzeichen (Fortschritt 4.2): verdiente und genau ein nächstes je Kategorie.</summary>
public sealed record BadgeProgressSnapshot(int CountedActions, int CurrentStreak, int DistinctKinds, bool HasGroup, bool CollectiveGoalReached, int RecognitionsGiven);

/// <summary>
/// Plattformkatalog der Abzeichen (Fortschritt 4.1, A-046) im Umfang des Durchstichs: Regeln über Handlungen, Tage und
/// Kollektivereignisse. Tenants legen keine eigenen Abzeichen an. Das erste Abzeichen entsteht im Beitritt selbst.
/// </summary>
public static class BadgeCatalog
{
    public const string ErsterTag = "einstieg.erster_tag";
    public const string ErsteUebung = "einstieg.erste_uebung";
    public const string ErsteGruppe = "einstieg.erste_gruppe";
    public const string Serie7 = "dranbleiben.serie_7";
    public const string Serie30 = "dranbleiben.serie_30";
    public const string Serie100 = "dranbleiben.serie_100";
    public const string Firmenziel = "gemeinsam.firmenziel";
    public const string Anerkennung10 = "gemeinsam.anerkennung_10";
    public const string FuenfArten = "vielfalt.fuenf_arten";

    public static IReadOnlyList<BadgeDefinition> All { get; } =
    [
        new(ErsterTag, BadgeCategory.Einstieg, 1, "abzeichen.einstieg.ersterTag"),
        new(ErsteUebung, BadgeCategory.Einstieg, 2, "abzeichen.einstieg.ersteUebung"),
        new(ErsteGruppe, BadgeCategory.Einstieg, 3, "abzeichen.einstieg.ersteGruppe"),
        new(Serie7, BadgeCategory.Dranbleiben, 1, "abzeichen.dranbleiben.serie7"),
        new(Serie30, BadgeCategory.Dranbleiben, 2, "abzeichen.dranbleiben.serie30"),
        new(Serie100, BadgeCategory.Dranbleiben, 3, "abzeichen.dranbleiben.serie100"),
        new(Firmenziel, BadgeCategory.Gemeinsam, 1, "abzeichen.gemeinsam.firmenziel"),
        new(Anerkennung10, BadgeCategory.Gemeinsam, 2, "abzeichen.gemeinsam.anerkennung10"),
        new(FuenfArten, BadgeCategory.Vielfalt, 1, "abzeichen.vielfalt.fuenfArten"),
    ];

    public static BadgeDefinition Get(string key) => All.Single(b => b.Key == key);

    /// <summary>Welche Abzeichen der Stand verdient hat; verdiente Abzeichen werden nie entzogen (Fortschritt 4.2).</summary>
    public static IEnumerable<string> Earned(BadgeProgressSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        yield return ErsterTag;
        if (snapshot.CountedActions >= 1)
        {
            yield return ErsteUebung;
        }

        if (snapshot.HasGroup)
        {
            yield return ErsteGruppe;
        }

        if (snapshot.CurrentStreak >= 7)
        {
            yield return Serie7;
        }

        if (snapshot.CurrentStreak >= 30)
        {
            yield return Serie30;
        }

        if (snapshot.CurrentStreak >= 100)
        {
            yield return Serie100;
        }

        if (snapshot.CollectiveGoalReached)
        {
            yield return Firmenziel;
        }

        if (snapshot.RecognitionsGiven >= 10)
        {
            yield return Anerkennung10;
        }

        if (snapshot.DistinctKinds >= 5)
        {
            yield return FuenfArten;
        }
    }

    /// <summary>Genau ein nächstes erreichbares Abzeichen je Kategorie (Fortschritt 4.2); alle weiteren existieren für die Person nicht sichtbar.</summary>
    public static IReadOnlyList<BadgeDefinition> NextPerCategory(IReadOnlySet<string> earned)
    {
        ArgumentNullException.ThrowIfNull(earned);
        return All.GroupBy(b => b.Category)
            .Select(g => g.OrderBy(b => b.Order).FirstOrDefault(b => !earned.Contains(b.Key)))
            .Where(b => b is not null)
            .Select(b => b!)
            .ToList();
    }
}

/// <summary>Verliehenes Abzeichen (Fortschritt 4.2): je Person höchstens einmal, nie entzogen.</summary>
public sealed class BadgeAward : ITenantOwned
{
    private BadgeAward(TenantId tenantId, PersonId personId, string badgeKey, DateTimeOffset awardedAt)
    {
        TenantId = tenantId;
        PersonId = personId;
        BadgeKey = badgeKey;
        AwardedAt = awardedAt;
    }

    public TenantId TenantId { get; }

    public PersonId PersonId { get; }

    public string BadgeKey { get; }

    public DateTimeOffset AwardedAt { get; }

    public static BadgeAward Grant(TenantId tenantId, PersonId personId, string badgeKey, DateTimeOffset now) =>
        new(tenantId, personId, BadgeCatalog.Get(badgeKey).Key, now);
}

/// <summary>Persönlicher Stand (Fortschritt 2 bis 6): Saldo, erreichte Stufe, Serie, Serienschutz, Tagesziel.</summary>
public sealed class PersonProgress : ITenantOwned
{
    private PersonProgress(TenantId tenantId, PersonId personId)
    {
        TenantId = tenantId;
        PersonId = personId;
    }

    public TenantId TenantId { get; }

    public PersonId PersonId { get; }

    public int PointsBalance { get; private set; }

    /// <summary>Erreichte Stufe; sinkt nie (Fortschritt 5).</summary>
    public int Level { get; private set; } = 1;

    public int CurrentStreak { get; private set; }

    public int LongestStreak { get; private set; }

    public DateOnly? LastActiveDay { get; private set; }

    /// <summary>Monat, in dem der Serienschutz verbraucht wurde (erster Tag des Monats).</summary>
    public DateOnly? ProtectionUsedIn { get; private set; }

    /// <summary>Persönliches Tagesziel 1 bis 3 Handlungen (Fortschritt 6.2), Voreinstellung 1.</summary>
    public int DailyGoal { get; private set; } = 1;

    public static PersonProgress Start(TenantId tenantId, PersonId personId) => new(tenantId, personId);

    /// <summary>Gewertete Handlung: Saldo, Stufe und Serie fortschreiben. Liefert die neue Stufe, wenn sie erreicht wurde.</summary>
    public int? Apply(int points, DateOnly day)
    {
        var previousLevel = Level;
        PointsBalance = PointsRules.ApplyToBalance(PointsBalance, points);
        Level = LevelRules.Keep(Level, PointsBalance);
        if (points > 0)
        {
            (CurrentStreak, LongestStreak, ProtectionUsedIn) = StreakRules.Extend(CurrentStreak, LongestStreak, LastActiveDay, day, ProtectionUsedIn);
            if (LastActiveDay is null || day > LastActiveDay)
            {
                LastActiveDay = day;
            }
        }

        return Level > previousLevel ? Level : null;
    }

    /// <summary>Korrektur (Fortschritt 2.2, 3.1): Saldo sinkt bis null, Serie wird aus den verbleibenden Tagen neu berechnet; Stufe bleibt.</summary>
    public void Reverse(int points, IEnumerable<DateOnly> remainingActiveDays)
    {
        PointsBalance = PointsRules.ApplyToBalance(PointsBalance, points);
        var days = remainingActiveDays.Distinct().ToList();
        (CurrentStreak, LongestStreak, ProtectionUsedIn) = StreakRules.Recompute(days);
        LongestStreak = Math.Max(LongestStreak, CurrentStreak);
        LastActiveDay = days.Count == 0 ? null : days.Max();
    }

    public void SetDailyGoal(int goal)
    {
        if (goal is < 1 or > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(goal), "Tagesziel 1 bis 3 Handlungen (Fortschritt 6.2).");
        }

        DailyGoal = goal;
    }
}

/// <summary>Täglicher Check-in (Fortschritt 6.1): drei Kacheln, Mehrfachauswahl, einmal je Tag, auch am Kiosk.</summary>
[Flags]
public enum CheckInTiles
{
    None = 0,
    Moved = 1,
    Paused = 2,
    Rested = 4,
}

public sealed class CheckIn : ITenantOwned
{
    private CheckIn(TenantId tenantId, PersonId personId, DateOnly day, CheckInTiles tiles, DateTimeOffset recordedAt, Guid activityId)
    {
        TenantId = tenantId;
        PersonId = personId;
        Day = day;
        Tiles = tiles;
        RecordedAt = recordedAt;
        ActivityId = activityId;
    }

    public TenantId TenantId { get; }

    public PersonId PersonId { get; }

    public DateOnly Day { get; }

    public CheckInTiles Tiles { get; }

    public DateTimeOffset RecordedAt { get; }

    public Guid ActivityId { get; }

    public static CheckIn Record(TenantId tenantId, PersonId personId, DateOnly day, CheckInTiles tiles, DateTimeOffset now, Guid activityId)
    {
        if (tiles == CheckInTiles.None)
        {
            throw new ArgumentException("Mindestens eine Kachel (Fortschritt 6.1).", nameof(tiles));
        }

        return new CheckIn(tenantId, personId, day, tiles, now, activityId);
    }
}
