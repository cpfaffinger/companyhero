using System.Net;
using System.Net.Mail;
using System.Text;
using CompanyHero.Modules.Branding.Application;
using CompanyHero.Modules.Branding.Domain.Text;
using CompanyHero.Modules.Branding.Domain.Theme;
using CompanyHero.Modules.Notifications.Domain;
using CompanyHero.Platform.Messaging;
using CompanyHero.Platform.Tenancy;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CompanyHero.Modules.Notifications.Application;

/// <summary>Eine E-Mail ohne Tracking (Benachrichtigungen 6.3): reiner Text, optionale Abmelde-Kopfzeilen, keine Bilder, keine Klickverfolgung.</summary>
public sealed record MailEnvelope(string To, string Subject, string TextBody, string? ListUnsubscribeUrl);

/// <summary>Transport des Betreibers (A-032); Tests ersetzen ihn.</summary>
public interface IMailTransport
{
    bool Configured { get; }

    Task SendAsync(MailEnvelope envelope, CancellationToken cancellationToken);
}

/// <summary>SMTP mit STARTTLS über <c>System.Net.Mail</c>; Zugangsdaten aus OpenBao (<c>app/notifications</c>).</summary>
internal sealed class SmtpMailTransport(IOptions<NotificationsOptions> options, ILogger<SmtpMailTransport> logger) : IMailTransport
{
    public bool Configured => options.Value.Smtp.Configured;

    public async Task SendAsync(MailEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        var smtp = options.Value.Smtp;
        if (!smtp.Configured)
        {
            // Ohne Transport wird nur protokolliert, dass eine Nachricht fällig war: ohne Adresse, ohne Inhalt (Betrieb 3.2).
            logger.LogInformation("E-Mail fällig, aber kein SMTP-Transport konfiguriert (A-032).");
            return;
        }

        using var message = new MailMessage
        {
            From = new MailAddress(smtp.From, smtp.FromName, Encoding.UTF8),
            Subject = envelope.Subject,
            SubjectEncoding = Encoding.UTF8,
            Body = envelope.TextBody,
            BodyEncoding = Encoding.UTF8,
            IsBodyHtml = false,
        };
        message.To.Add(new MailAddress(envelope.To));
        if (envelope.ListUnsubscribeUrl is not null)
        {
            message.Headers.Add("List-Unsubscribe", $"<{envelope.ListUnsubscribeUrl}>");
            message.Headers.Add("List-Unsubscribe-Post", "List-Unsubscribe=One-Click");
        }

        message.Headers.Add("Auto-Submitted", "auto-generated");
        using var client = new SmtpClient(smtp.Host, smtp.Port)
        {
            EnableSsl = smtp.UseStartTls,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            Credentials = string.IsNullOrEmpty(smtp.Username) ? null : new NetworkCredential(smtp.Username, smtp.Password),
        };
        await client.SendMailAsync(message, cancellationToken);
    }
}

/// <summary>Konto-Nachrichten (Benachrichtigungen 4.1 „Konto und Sicherheit“): Magic-Link und Rollencode per E-Mail, ohne Abmeldelink, Texte aus dem Katalog.</summary>
internal sealed class MailAccountMailSender(IMailTransport transport, MailTexts texts) : IAccountMailSender
{
    public async Task SendAsync(AccountMail mail, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mail);
        var t = await texts.ResolveAsync(cancellationToken);
        var (subjectKey, bodyKey) = mail.TemplateKey switch
        {
            "konto.magic_link" => ("konto.magicLink.betreff", "konto.magicLink.text"),
            "konto.rollencode" => ("konto.rollencode.betreff", "konto.rollencode.text"),
            _ => throw new ArgumentException($"Unbekannte Vorlage '{mail.TemplateKey}'.", nameof(mail)),
        };
        var greeting = mail.DisplayName is null ? t.Text("mail.anredeOhneName") : t.Text("mail.anrede", ("name", mail.DisplayName));
        var body = greeting + "\n\n" + t.Text(bodyKey, ("link", mail.Link)) + "\n\n" + t.Text("mail.abschluss");
        await transport.SendAsync(new MailEnvelope(mail.Recipient, t.Text(subjectKey), body, null), cancellationToken);
    }
}

/// <summary>Aufgelöste Texte in Tonalität und Anrede des Tenants (Marke 6.2) für E-Mail und Aushang; Platzhalter mit Bezügen werden hier ersetzt.</summary>
public sealed class ResolvedTexts(IReadOnlyDictionary<string, string> texts, string produktname)
{
    public string Produktname { get; } = produktname;

    public string Text(string key, params (string Name, string Value)[] parameters)
    {
        var text = texts.TryGetValue(key, out var value) ? value : key;
        foreach (var (name, v) in parameters)
        {
            text = text.Replace("{" + name + "}", v, StringComparison.Ordinal);
        }

        return text;
    }
}

internal sealed class MailTexts(IThemeService themes, ITenantContextAccessor context)
{
    public async Task<ResolvedTexts> ResolveAsync(CancellationToken cancellationToken)
    {
        if (context.Current is null || context.Current.Kind != TenantContextKind.Tenant)
        {
            var standard = ThemeDocument.Standard(themes.Platform.Plattformname, themes.Platform);
            return new ResolvedTexts(TextCatalog.Embedded.Resolve(standard.Tonalitaet, standard.Anrede, standard.Produktname, standard.Bezeichnungen), standard.Produktname);
        }

        var theme = await themes.GetCurrentAsync(cancellationToken);
        var d = theme.Document;
        return new ResolvedTexts(TextCatalog.Embedded.Resolve(d.Tonalitaet, d.Anrede, d.Produktname, d.Bezeichnungen), d.Produktname);
    }
}

/// <summary>Signierte, zeitlich begrenzte Abmeldelinks (Benachrichtigungen 8): erlauben nur die Änderung von Benachrichtigungseinstellungen.</summary>
public sealed record UnsubscribeToken(TenantId TenantId, PersonId PersonId, NotificationCategory Category);

public interface IUnsubscribeTokens
{
    string Protect(UnsubscribeToken token);

    UnsubscribeToken? Unprotect(string value);
}

internal sealed class UnsubscribeTokens(IDataProtectionProvider dataProtection) : IUnsubscribeTokens
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);

    private readonly ITimeLimitedDataProtector _protector = dataProtection.CreateProtector("CompanyHero.Notifications.Unsubscribe").ToTimeLimitedDataProtector();

    public string Protect(UnsubscribeToken token)
    {
        ArgumentNullException.ThrowIfNull(token);
        return _protector.Protect($"{token.TenantId.Value:D}|{token.PersonId.Value:D}|{(int)token.Category}", Lifetime);
    }

    public UnsubscribeToken? Unprotect(string value)
    {
        try
        {
            var parts = _protector.Unprotect(value).Split('|');
            if (parts.Length != 3 || !Guid.TryParseExact(parts[0], "D", out var tenant) || !Guid.TryParseExact(parts[1], "D", out var person) || !int.TryParse(parts[2], out var category))
            {
                return null;
            }

            return new UnsubscribeToken(new TenantId(tenant), new PersonId(person), (NotificationCategory)category);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }
}
