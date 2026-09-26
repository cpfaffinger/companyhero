namespace CompanyHero.Platform.Messaging;

/// <summary>Konto-Nachricht (Benachrichtigungen 4 „Konto und Sicherheit“): Magic-Link oder Rollencode per E-Mail, ohne Abmeldelink.</summary>
/// <param name="Recipient">Ausdrücklich hinterlegte oder vom Aussteller eingegebene Adresse; wird nach Versand nicht weiter gespeichert.</param>
/// <param name="TemplateKey">Schlüssel der Vorlage, etwa <c>konto.magic_link</c> oder <c>konto.rollencode</c>.</param>
/// <param name="Link">Der Link beziehungsweise Code; die Nachricht enthält keine weiteren Personendaten außer der Anrede mit dem Anzeigenamen.</param>
public sealed record AccountMail(string Recipient, string TemplateKey, string Link, string? DisplayName);

/// <summary>
/// Querschnitt „Konto-E-Mail“ (Zugang 9 Identität ↔ Benachrichtigungen): Eigentümer ist Benachrichtigungen, Identity
/// verwendet die Schnittstelle. Der Versand selbst läuft außerhalb der Kontexttransaktion des Vorgangs.
/// </summary>
public interface IAccountMailSender
{
    Task SendAsync(AccountMail mail, CancellationToken cancellationToken);
}
