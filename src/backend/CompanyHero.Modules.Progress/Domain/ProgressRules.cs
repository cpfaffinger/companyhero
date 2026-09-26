namespace CompanyHero.Modules.Progress.Domain;

/// <summary>Punkte und Tagesdeckel (Fortschritt 2.2, A-044): Aktivitätsgleichheit als Rechenregel.</summary>
public static class PointsRules
{
    public const int PointsPerAction = 10;

    /// <summary>Höchstens zehn gewertete Handlungen je Tag; weitere zählen für Challenges, erzeugen aber keine Punkte.</summary>
    public const int DailyCap = 10;

    /// <summary>Deckel je Art (Fortschritt 2.2); unbekannte Arten zählen wie automatische Tageswerte: eine je Tag.</summary>
    public static int CapFor(string kind) => kind switch
    {
        ActivityKinds.CheckIn => 1,
        ActivityKinds.ChallengeContribution => 3,
        ActivityKinds.RecognitionGiven => 3,
        ActivityKinds.FeedPost => 1,
        "content_completed" => 3,
        "post_read" => 2,
        "event_attended" => 1,
        _ => 1,
    };

    /// <summary>Punkte einer neuen Handlung nach den bereits gewerteten Handlungen des Tages (gesamt und je Art).</summary>
    public static int PointsFor(string kind, int countedTodayTotal, int countedTodayOfKind) =>
        countedTodayTotal < DailyCap && countedTodayOfKind < CapFor(kind) ? PointsPerAction : 0;

    /// <summary>Saldo verfällt nie, ist nie negativ; Gegenereignisse reduzieren bis höchstens auf null (Fortschritt 2.2).</summary>
    public static int ApplyToBalance(int balance, int points) => Math.Max(0, balance + points);
}

/// <summary>Stufen (Fortschritt 5, A-047): fünf Schwellen; eine erreichte Stufe sinkt nie.</summary>
public static class LevelRules
{
    public static IReadOnlyList<int> Thresholds { get; } = [0, 300, 1000, 3000, 10000];

    public static int LevelFor(int points)
    {
        var level = 1;
        for (var i = 1; i < Thresholds.Count; i++)
        {
            if (points >= Thresholds[i])
            {
                level = i + 1;
            }
        }

        return level;
    }

    /// <summary>Nie unter die erreichte Stufe (Fortschritt 5): Korrekturen senken den Saldo, nicht die Stufe.</summary>
    public static int Keep(int reachedLevel, int points) => Math.Max(reachedLevel, LevelFor(points));
}

/// <summary>Serie (Fortschritt 3.1, A-045): zusammenhängende Kalendertage mit gewerteter Handlung; Serienschutz einmal je Monat.</summary>
public static class StreakRules
{
    /// <summary>Serie nach einer Handlung am Tag <paramref name="day"/>; ein ausgelassener Tag je Monat wird überbrückt.</summary>
    public static (int Current, int Longest, DateOnly? ProtectionUsedIn) Extend(int current, int longest, DateOnly? lastActiveDay, DateOnly day, DateOnly? protectionUsedIn)
    {
        if (lastActiveDay is null)
        {
            return (1, Math.Max(longest, 1), protectionUsedIn);
        }

        var gap = day.DayNumber - lastActiveDay.Value.DayNumber;
        if (gap <= 0)
        {
            return (current, longest, protectionUsedIn);
        }

        if (gap == 1)
        {
            var next = current + 1;
            return (next, Math.Max(longest, next), protectionUsedIn);
        }

        var month = new DateOnly(day.Year, day.Month, 1);
        if (gap == 2 && (protectionUsedIn is null || protectionUsedIn.Value != month))
        {
            // Serienschutz: ein ausgelassener Tag je Kalendermonat, angewendet mit der nächsten Handlung.
            var protectedNext = current + 1;
            return (protectedNext, Math.Max(longest, protectedNext), month);
        }

        return (1, Math.Max(longest, 1), protectionUsedIn);
    }

    /// <summary>Neuberechnung aus allen aktiven Tagen (nach einer Korrektur, Fortschritt 3.1); der Schutz greift, wenn er im Monat noch verfügbar ist.</summary>
    public static (int Current, int Longest, DateOnly? ProtectionUsedIn) Recompute(IEnumerable<DateOnly> activeDays)
    {
        var current = 0;
        var longest = 0;
        DateOnly? last = null;
        DateOnly? protection = null;
        foreach (var day in activeDays.Distinct().Order())
        {
            (current, longest, protection) = Extend(current, longest, last, day, protection);
            last = day;
        }

        return (current, longest, protection);
    }
}
