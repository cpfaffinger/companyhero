using CompanyHero.Platform.Tenancy;

namespace CompanyHero.Modules.Challenges.Domain;

/// <summary>Erfassungsart der Challenge (Challenges 2.1, A-038): im Durchstich Häkchen und Zahl.</summary>
public enum ChallengeMetric
{
    /// <summary>Häkchen: genau ein Beitrag mit Wert 1 je Erfassung.</summary>
    Checkmark = 1,

    /// <summary>Zahl: positiver Wert mit bis zu vier Nachkommastellen.</summary>
    Count = 2,
}

/// <summary>Lebenszyklus (Challenges 4.1, A-040): Entwurf, Geplant, Laufend, Nachfrist, Beendet, Archiviert.</summary>
public enum ChallengeState
{
    Draft = 1,
    Planned = 2,
    Running = 3,
    Grace = 4,
    Ended = 5,
    Archived = 6,
}

/// <summary>Sichtbarkeit der Challenge (Challenges 2.1 Achse 7): Nur ich, Gruppe, ganze Firma; die restriktivere persönliche Einstellung gewinnt immer.</summary>
public enum ChallengeVisibility
{
    OnlyMe = 1,
    Team = 2,
    Company = 3,
}

/// <summary>Entwurf einer Challenge aus dem Wizard (Challenges 2, 4): Sammelziel als Standardform, ganze Firma als Aggregation.</summary>
public sealed record ChallengeDraft(string Title, string? Description, ChallengeMetric Metric, decimal Target, DateTimeOffset StartsAt, DateTimeOffset EndsAt, ChallengeVisibility Visibility, string? TemplateKey);

/// <summary>Challenge eines Tenants mit Sammelziel (Challenges 2, A-038); Achsen nach dem Start unveränderbar, Texte änderbar (A-040).</summary>
public sealed class Challenge : ITenantOwned
{
    public const string KickoffTemplate = "kickoff";

    public const int MaxTitle = 200;
    public const int MaxDescription = 2000;

    private Challenge(TenantId tenantId, Guid id, string title, ChallengeMetric metric, decimal target, ChallengeState state, DateTimeOffset startsAt, DateTimeOffset endsAt, DateTimeOffset createdAt, ChallengeVisibility visibility)
    {
        TenantId = tenantId;
        Id = id;
        Title = title;
        Metric = metric;
        Target = target;
        State = state;
        StartsAt = startsAt;
        EndsAt = endsAt;
        CreatedAt = createdAt;
        Visibility = visibility;
    }

    public TenantId TenantId { get; }

    public Guid Id { get; }

    public string Title { get; private set; }

    public string? Description { get; private set; }

    public ChallengeMetric Metric { get; }

    /// <summary>Sammelziel (Challenges 2.1 Achse 6, Standardform): Zielwert des Kollektivs mit vier Nachkommastellen.</summary>
    public decimal Target { get; }

    public ChallengeState State { get; private set; }

    public DateTimeOffset StartsAt { get; }

    public DateTimeOffset EndsAt { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public ChallengeVisibility Visibility { get; }

    public PersonId? CreatedBy { get; private set; }

    /// <summary>Vorlage, aus der die Challenge entstand (etwa <c>kickoff</c>), sonst <c>null</c>.</summary>
    public string? TemplateKey { get; private set; }

    /// <summary>Vorschau ist Pflicht vor „Planen“ (Challenges 4.1).</summary>
    public DateTimeOffset? PreviewedAt { get; private set; }

    public DateTimeOffset? PlannedAt { get; private set; }

    public DateTimeOffset? EndedAt { get; private set; }

    /// <summary>Vorzeitiges Ende durch den Ersteller mit Begründung im Protokoll (Challenges 4.1).</summary>
    public string? EndedEarlyReason { get; private set; }

    public DateTimeOffset? ArchivedAt { get; private set; }

    /// <summary>Nachfrist: 48 Stunden nach Ende werden Beiträge mit Erfassungszeitpunkt im Zeitraum noch angenommen (Challenges 3, A-039).</summary>
    public static TimeSpan GracePeriod { get; } = TimeSpan.FromHours(48);

    /// <summary>Beendete Challenges werden nach 12 Monaten archiviert (Challenges 4.1).</summary>
    public static TimeSpan ArchiveAfter { get; } = TimeSpan.FromDays(365);

    public DateTimeOffset AcceptsContributionsUntil => EndsAt.Add(GracePeriod);

    /// <summary>Beiträge sind in „Laufend“ und „Nachfrist“ möglich (Challenges 4.1); die Regeln der Erfassung prüfen Zeitraum und Nachfrist.</summary>
    public bool AcceptsContributions => State is ChallengeState.Running or ChallengeState.Grace;

    public static Challenge CreateDraft(TenantId tenantId, ChallengeDraft draft, PersonId createdBy, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var challenge = new Challenge(tenantId, Guid.CreateVersion7(), ValidTitle(draft.Title), draft.Metric, ValidTarget(draft.Target), ChallengeState.Draft, draft.StartsAt.ToUniversalTime(), draft.EndsAt.ToUniversalTime(), now.ToUniversalTime(), draft.Visibility)
        {
            CreatedBy = createdBy,
            TemplateKey = draft.TemplateKey,
            Description = ValidDescription(draft.Description),
        };
        if (challenge.EndsAt <= challenge.StartsAt)
        {
            throw new ArgumentException("Das Ende liegt nach dem Beginn.", nameof(draft));
        }

        return challenge;
    }

    /// <summary>Kickoff-Challenge (Challenges 4.3): Sammelziel, Häkchen, Sichtbarkeit Firma, synchroner Start für alle am kommenden Montag, vier Wochen.</summary>
    public static ChallengeDraft KickoffDraft(DateTimeOffset now, TimeZoneInfo zone, int headcount)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var today = TenantTimeZone.DayOf(now, zone);
        var daysUntilMonday = ((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7;
        var start = today.AddDays(daysUntilMonday == 0 ? 7 : daysUntilMonday);
        var end = start.AddDays(28);
        var target = Math.Max(20, headcount) * 20;
        return new ChallengeDraft("Gemeinsam starten", null, ChallengeMetric.Checkmark, target, TenantTimeZone.StartOfDay(start, zone), TenantTimeZone.StartOfDay(end, zone).AddSeconds(-1), ChallengeVisibility.Company, KickoffTemplate);
    }

    /// <summary>Direkt laufende Challenge für Tests und Bestände der Stufen 2 bis 5.</summary>
    public static Challenge StartRunning(TenantId tenantId, string title, ChallengeMetric metric, decimal target, DateTimeOffset startsAt, DateTimeOffset endsAt, DateTimeOffset now, ChallengeVisibility visibility = ChallengeVisibility.Company)
    {
        if (endsAt <= startsAt)
        {
            throw new ArgumentException("Das Ende liegt nach dem Beginn.", nameof(endsAt));
        }

        return new Challenge(tenantId, Guid.CreateVersion7(), ValidTitle(title), metric, ValidTarget(target), ChallengeState.Running, startsAt.ToUniversalTime(), endsAt.ToUniversalTime(), now.ToUniversalTime(), visibility);
    }

    public void MarkPreviewed(DateTimeOffset now)
    {
        RequireState(ChallengeState.Draft, "Vorschau nur im Entwurf.");
        PreviewedAt = now;
    }

    /// <summary>Entwurf → Geplant: sichtbar als „startet am“, Beitritt möglich, keine Beiträge (Challenges 4.1); Vorschau ist Pflicht.</summary>
    public void Plan(DateTimeOffset now)
    {
        RequireState(ChallengeState.Draft, "Planen nur aus dem Entwurf.");
        if (PreviewedAt is null)
        {
            throw new ChallengeLifecycleException("preview_required", "Vorschau ist Pflicht vor „Planen“ (Challenges 4.1).");
        }

        if (EndsAt <= now)
        {
            throw new ChallengeLifecycleException("period_in_past", "Der Zeitraum liegt in der Vergangenheit.");
        }

        State = ChallengeState.Planned;
        PlannedAt = now;
    }

    /// <summary>Geplant → Laufend zum Startzeitpunkt (zeitgesteuert). Wahr, wenn der Übergang stattfand.</summary>
    public bool StartIfDue(DateTimeOffset now)
    {
        if (State != ChallengeState.Planned || now < StartsAt)
        {
            return false;
        }

        State = ChallengeState.Running;
        return true;
    }

    /// <summary>Laufend → Nachfrist zum Ende (zeitgesteuert): 48 Stunden verspätete Offline-Beiträge und Korrekturen.</summary>
    public bool EnterGraceIfDue(DateTimeOffset now)
    {
        if (State != ChallengeState.Running || now < EndsAt)
        {
            return false;
        }

        State = ChallengeState.Grace;
        return true;
    }

    /// <summary>Nachfrist → Beendet: Endstand endgültig (zeitgesteuert).</summary>
    public bool EndIfDue(DateTimeOffset now)
    {
        if (State != ChallengeState.Grace || now < AcceptsContributionsUntil)
        {
            return false;
        }

        State = ChallengeState.Ended;
        EndedAt = now;
        return true;
    }

    public bool ArchiveIfDue(DateTimeOffset now)
    {
        if (State != ChallengeState.Ended || EndedAt is null || now < EndedAt.Value + ArchiveAfter)
        {
            return false;
        }

        State = ChallengeState.Archived;
        ArchivedAt = now;
        return true;
    }

    /// <summary>Vorzeitiges Ende durch den Ersteller mit Begründung (Challenges 4.1): das Ende ist jetzt, die Nachfrist läuft ab jetzt.</summary>
    public void EndEarly(string reason, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        RequireState(ChallengeState.Running, "Vorzeitig beenden nur eine laufende Challenge.");
        EndsAt = now;
        EndedEarlyReason = reason.Trim();
        State = ChallengeState.Grace;
    }

    /// <summary>Änderbar bleiben Texte (Challenges 4.1); Zeitraum, Metrik, Aggregation und Form nicht.</summary>
    public void UpdateTexts(string title, string? description)
    {
        Title = ValidTitle(title);
        Description = ValidDescription(description);
    }

    /// <summary>Kollektivfortschritt in ganzen Prozent, gedeckelt bei 100 (Challenges 6.2: gerundeter Prozentwert).</summary>
    public int PercentOf(decimal total) => (int)Math.Min(100m, Math.Max(0m, Math.Round(total / Target * 100m, 0, MidpointRounding.AwayFromZero)));

    private void RequireState(ChallengeState expected, string message)
    {
        if (State != expected)
        {
            throw new ChallengeLifecycleException("state_invalid", message);
        }
    }

    private static string ValidTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > MaxTitle)
        {
            throw new ArgumentException($"Titel ist Pflicht und höchstens {MaxTitle} Zeichen lang.", nameof(title));
        }

        return title.Trim();
    }

    private static string? ValidDescription(string? description)
    {
        if (description is null)
        {
            return null;
        }

        if (description.Length > MaxDescription)
        {
            throw new ArgumentException($"Beschreibung höchstens {MaxDescription} Zeichen.", nameof(description));
        }

        return string.IsNullOrWhiteSpace(description) ? null : description.Trim();
    }

    private static decimal ValidTarget(decimal target)
    {
        if (target <= 0m)
        {
            throw new ArgumentException("Das Sammelziel ist positiv.", nameof(target));
        }

        return decimal.Round(target, 4, MidpointRounding.ToEven);
    }
}

/// <summary>Verletzung des Lebenszyklus (Challenges 4.1) mit stabiler Kennung für den Vertrag.</summary>
public sealed class ChallengeLifecycleException : InvalidOperationException
{
    public ChallengeLifecycleException()
    {
        Reason = "state_invalid";
    }

    public ChallengeLifecycleException(string message)
        : base(message)
    {
        Reason = "state_invalid";
    }

    public ChallengeLifecycleException(string message, Exception innerException)
        : base(message, innerException)
    {
        Reason = "state_invalid";
    }

    public ChallengeLifecycleException(string reason, string message)
        : base(message)
    {
        Reason = reason;
    }

    public string Reason { get; }
}
