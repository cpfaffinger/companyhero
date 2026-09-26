using CompanyHero.Platform.Messaging;
using Microsoft.Extensions.Logging;

namespace CompanyHero.Modules.Notifications.Application;

/// <summary>
/// Eigentümer des Querschnitts „Konto-E-Mail“ (Zugang 9): Magic-Link und Rollencode-Einladung. Der SMTP-Transport des
/// Betreibers (A-032) folgt mit dem Fachpfad (Stufe 6); bis dahin protokolliert die Umsetzung nur, dass eine Nachricht
/// fällig war, ohne Adresse und ohne Link (Betrieb 3.2: Logs ohne Personenbezug). Tests ersetzen den Sender.
/// </summary>
internal sealed class LoggingAccountMailSender(ILogger<LoggingAccountMailSender> logger) : IAccountMailSender
{
    public Task SendAsync(AccountMail mail, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mail);
        logger.LogInformation("Konto-Nachricht {Template} fällig; SMTP-Transport folgt mit dem Fachpfad (A-032).", mail.TemplateKey);
        return Task.CompletedTask;
    }
}
