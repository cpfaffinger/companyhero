using CompanyHero.Modules.Identity.Application;
using CompanyHero.Modules.Identity.Application.Passkeys;
using CompanyHero.Modules.Identity.Application.Sessions;
using CompanyHero.Modules.Identity.Domain;
using CompanyHero.Modules.Identity.Infrastructure.Http;
using CompanyHero.Platform.Privacy;
using CompanyHero.Platform.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace CompanyHero.Modules.Identity.Api;

internal static class AccessEndpointHelpers
{
    /// <summary>Fachliche Ablehnungen des Zugangs als Problem Details mit stabiler Kennung (K15); Rest bleibt beim Standard-Handler.</summary>
    public static TBuilder HandleAccessErrors<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder
    {
        builder.AddEndpointFilter(async (invocation, next) =>
        {
            try
            {
                return await next(invocation);
            }
            catch (AccessDeniedException ex)
            {
                return Results.Problem(statusCode: ex.Status, title: "Zugang abgelehnt", detail: ex.Reason);
            }
            catch (PasskeyRejectedException ex)
            {
                return Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "Passkey abgelehnt", detail: "passkey_rejected", extensions: new Dictionary<string, object?> { ["reason"] = ex.Message });
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Forbid();
            }
        });
        return builder;
    }

    public static SessionKindDto ToDto(this SessionKind kind) => kind switch
    {
        SessionKind.Privileged => SessionKindDto.Privileged,
        SessionKind.KioskDevice => SessionKindDto.KioskDevice,
        SessionKind.KioskPerson => SessionKindDto.KioskPerson,
        _ => SessionKindDto.Member,
    };

    public static VisibilityChoice ToChoice(this VisibilityDto visibility) => visibility switch
    {
        VisibilityDto.OnlyMe => VisibilityChoice.OnlyMe,
        VisibilityDto.Team => VisibilityChoice.Team,
        _ => VisibilityChoice.Company,
    };

    public static void ApplySession(HttpResponse response, IssuedSession session) =>
        SessionCookies.SetSession(response, session.Token, session.CsrfToken, session.SlidingUntil ?? session.AbsoluteUntil);

    public static LoginResponse ToLoginResponse(IssuedSession session, string? recoveryCode, bool mustSetUpAccess) =>
        new(session.TenantId.ToString(), session.PersonId!.Value.ToString(), session.Kind.ToDto(), recoveryCode, mustSetUpAccess);

    public static Guid? SessionId(HttpContext http) =>
        Guid.TryParseExact(http.User.FindFirst(SessionAuthenticationHandler.SessionIdClaim)?.Value, "D", out var id) ? id : null;

    public static bool IsLocalUrl(string? url) => url is { Length: > 0 } && url[0] == '/' && (url.Length == 1 || (url[1] != '/' && url[1] != '\\'));

    public static bool ValidDisplayName(string? name) => !string.IsNullOrWhiteSpace(name) && name.Trim().Length is >= 2 and <= 80;

    public static bool ValidKey(string? key) => key is { Length: > 0 and <= 40 } && key.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c));
}
