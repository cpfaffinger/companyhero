namespace CompanyHero.Platform.Audit;

/// <summary>
/// Eintrag des Prüfprotokolls (Datenschutz 6.2, Zugang 9): Konfigurations-, Rollen- und Zugangsänderungen, append-only,
/// ohne Personenbezug auf Mitglieder. Die handelnde Person steht als pseudonyme Kennung; der Bezug ist eine Objektkennung,
/// nie ein Anzeigename und nie eine E-Mail.
/// </summary>
/// <param name="Action">Fester Bezeichner, etwa <c>access.role_code.redeemed</c>.</param>
/// <param name="SubjectRef">Kennung des betroffenen Objekts (Code, Gerät, Anbieter) oder <c>null</c>.</param>
/// <param name="Detail">Kurzer fachlicher Zusatz ohne Personenbezug (Rolle, Weg), oder <c>null</c>.</param>
public sealed record AuditEntry(string Action, string? SubjectRef, string? Detail);

/// <summary>
/// Querschnitt „Prüfprotokoll“ (Domänenkarte 3): Eigentümer ist Datenschutz und Nachweis, verwendet wird die Schnittstelle
/// von allen Domänen. Der Eintrag entsteht in derselben Kontexttransaktion wie die Änderung; Tenant und handelnde Person
/// kommen aus dem Kontext.
/// </summary>
public interface IAuditLog
{
    Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken);
}

/// <summary>
/// Eintrag des Sicherheitsprotokolls (Datenschutz 5.1: Anmeldungen und Fehlversuche, pseudonymisiert, 12 Monate).
/// <paramref name="Pseudonym"/> ist eine Hash- oder Objektkennung, nie Kennung im Klartext, nie Name oder E-Mail.
/// </summary>
public sealed record SecurityEvent(string EventType, bool Success, string? Pseudonym, string? Detail);

/// <summary>Querschnitt „Sicherheitsprotokoll“; Eigentümer Datenschutz und Nachweis. Läuft im Tenant-Kontext des Vorgangs.</summary>
public interface ISecurityLog
{
    Task RecordAsync(SecurityEvent securityEvent, CancellationToken cancellationToken);
}
