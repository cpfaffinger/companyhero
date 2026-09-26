using Microsoft.AspNetCore.Http;

namespace CompanyHero.Modules.Identity.Infrastructure.Http;

/// <summary>
/// Cookies des Zugangs (A-007): Sitzung und Gerätegeheimnis als Secure/HttpOnly/SameSite=Lax (der externe Callback ist eine
/// Top-Level-Navigation), das CSRF-Token lesbar für den Client, der es als Header zurückgibt. Keine Geheimnisse in
/// localStorage oder IndexedDB; das Cookie trägt ein zufälliges Geheimnis, dessen Hash in PostgreSQL liegt.
/// </summary>
public static class SessionCookies
{
    public const string Session = "ch_session";
    public const string Csrf = "ch_csrf";
    public const string CsrfHeader = "X-CSRF-Token";
    public const string KioskDevice = "ch_kiosk_device";
    public const string ExternalHandoff = "ch_ext";

    /// <summary>Sitzungsschema der Plattform; Standardschema für Authentifizierung und Anmeldeaufforderung.</summary>
    public const string Scheme = "ch-session";

    public static void SetSession(HttpResponse response, string token, string csrfToken, DateTimeOffset? expires)
    {
        response.Cookies.Append(Session, token, Secret(expires));
        response.Cookies.Append(Csrf, csrfToken, Readable(expires));
    }

    public static void ClearSession(HttpResponse response)
    {
        response.Cookies.Delete(Session, Secret(null));
        response.Cookies.Delete(Csrf, Readable(null));
    }

    /// <summary>
    /// Ende einer Personensitzung am Kiosk: das Sitzungs-Cookie fällt, das Gerät behält Geheimnis und sein CSRF-Token
    /// (die Personensitzung trug dasselbe Token). Ohne Gerätegeheimnis wie <see cref="ClearSession"/>.
    /// </summary>
    public static void ClearSessionKeepingDevice(HttpRequest request, HttpResponse response)
    {
        if (request.Cookies.TryGetValue(KioskDevice, out var secret) && !string.IsNullOrEmpty(secret))
        {
            response.Cookies.Delete(Session, Secret(null));
            response.Cookies.Append(Csrf, DeviceCsrfToken(secret), Readable(DateTimeOffset.UtcNow.AddYears(5)));
            return;
        }

        ClearSession(response);
    }

    /// <summary>Nur das (abgelaufene oder widerrufene) Sitzungs-Cookie entfernen; das CSRF-Cookie der Gerätesitzung bleibt.</summary>
    public static void ClearStaleSession(HttpResponse response) => response.Cookies.Delete(Session, Secret(null));

    /// <summary>Gerätegeheimnis ohne Ablauf (nur Widerruf, Zugang 5); das CSRF-Token des Geräts ist der Hash seines Geheimnisses.</summary>
    public static void SetKioskDevice(HttpResponse response, string secret)
    {
        var far = DateTimeOffset.UtcNow.AddYears(5);
        response.Cookies.Append(KioskDevice, secret, Secret(far));
        response.Cookies.Append(Csrf, DeviceCsrfToken(secret), Readable(far));
    }

    public static void ClearKioskDevice(HttpResponse response)
    {
        response.Cookies.Delete(KioskDevice, Secret(null));
        response.Cookies.Delete(Csrf, Readable(null));
    }

    public static string DeviceCsrfToken(string secret) => Domain.AccessCodes.Hash("csrf:" + secret);

    public static void SetHandoff(HttpResponse response, string protectedValue, DateTimeOffset expires) =>
        response.Cookies.Append(ExternalHandoff, protectedValue, Secret(expires));

    public static void ClearHandoff(HttpResponse response) => response.Cookies.Delete(ExternalHandoff, Secret(null));

    private static CookieOptions Secret(DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        Expires = expires,
        IsEssential = true,
    };

    private static CookieOptions Readable(DateTimeOffset? expires) => new()
    {
        HttpOnly = false,
        Secure = true,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        Expires = expires,
        IsEssential = true,
    };
}
