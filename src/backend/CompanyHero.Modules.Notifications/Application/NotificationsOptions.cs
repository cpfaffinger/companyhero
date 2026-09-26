namespace CompanyHero.Modules.Notifications.Application;

/// <summary>VAPID-Schlüsselpaar der Plattform (Benachrichtigungen 3.1, A-059): privater Schlüssel als PEM aus OpenBao; der öffentliche wird abgeleitet.</summary>
public sealed class VapidOptions
{
    public string PrivateKeyPem { get; set; } = string.Empty;

    /// <summary>Kontakt des Betreibers für Push-Dienste, etwa <c>mailto:betrieb@example</c>.</summary>
    public string Subject { get; set; } = "mailto:betrieb@localhost";

    public bool Configured => !string.IsNullOrWhiteSpace(PrivateKeyPem);
}

/// <summary>E-Mail-Transport des Betreibers (A-032): SMTP mit STARTTLS; ohne Host bleibt der Versand protokolliert statt gesendet.</summary>
public sealed class SmtpOptions
{
    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 587;

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string From { get; set; } = "companyhero@localhost";

    public string FromName { get; set; } = "CompanyHero";

    public bool UseStartTls { get; set; } = true;

    public bool Configured => !string.IsNullOrWhiteSpace(Host);
}

public sealed class NotificationsOptions
{
    public const string Section = "Notifications";

    public VapidOptions Vapid { get; set; } = new();

    public SmtpOptions Smtp { get; set; } = new();

    /// <summary>Öffentliche Origin für Links in E-Mails und Push-Zielen; leer bedeutet die Origin aus <c>Identity:PublicOrigin</c>.</summary>
    public string? PublicOrigin { get; set; }
}
