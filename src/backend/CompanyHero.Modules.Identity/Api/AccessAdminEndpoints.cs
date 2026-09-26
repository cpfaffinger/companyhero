using CompanyHero.Modules.Identity.Application.Access;
using CompanyHero.Modules.Identity.Application.Kiosk;
using CompanyHero.Platform.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CompanyHero.Modules.Identity.Api;

/// <summary>Verwaltung des Zugangs (Zugang 2, 3.2, 3.4, 6.1): Codes, Anmeldewege, Anbieter, Kiosk-Geräte; sensible Aktionen mit frischer Anmeldung.</summary>
internal static class AccessAdminEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var access = endpoints.MapGroup("/api/access").HandleAccessErrors();

        access.MapGet("/join-codes", async (IAccessAdministration admin, CancellationToken ct) =>
                Results.Ok((await admin.ListJoinCodesAsync(ct)).Select(ToResponse).ToList()))
            .RequireTenantContext()
            .WithName("ListJoinCodes")
            .Produces<List<JoinCodeResponse>>();

        access.MapPost("/join-codes", async (CreateJoinCodeRequest request, IAccessAdministration admin, CancellationToken ct) =>
            {
                var code = await admin.CreateJoinCodeAsync(request.UsageLimit, request.ValidDays, ct);
                return Results.Created($"/api/access/join-codes/{code.Id:D}", ToResponse(code));
            })
            .RequireTenantContext()
            .WithName("CreateJoinCode")
            .Produces<JoinCodeResponse>(StatusCodes.Status201Created);

        access.MapPost("/join-codes/{id:guid}/revoke", async (Guid id, IAccessAdministration admin, CancellationToken ct) =>
                await admin.RevokeJoinCodeAsync(id, ct) ? Results.NoContent() : Results.NotFound())
            .RequireTenantContext()
            .WithName("RevokeJoinCode")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        access.MapPost("/role-codes", async (IssueRoleCodeRequest request, IAccessAdministration admin, CancellationToken ct) =>
            {
                var issued = await admin.IssueRoleCodeAsync(request.Role ?? string.Empty, request.Email, ct);
                return Results.Created($"/api/access/role-codes/{issued.Id:D}", new RoleCodeResponse(issued.Id.ToString("D"), issued.Code, issued.Role, issued.ExpiresAt));
            })
            .RequireTenantContext().RequireFreshLogin()
            .WithName("IssueRoleCode")
            .Produces<RoleCodeResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        access.MapPost("/role-codes/redeem", async (RedeemRoleCodeRequest request, IAccessAdministration admin, CancellationToken ct) =>
            {
                await admin.RedeemRoleCodeAsync(request.Code ?? string.Empty, request.RealName, ct);
                return Results.NoContent();
            })
            .RequireTenantContext()
            .WithName("RedeemRoleCode")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        access.MapGet("/policy", async (IAccessAdministration admin, CancellationToken ct) => Results.Ok(ToResponse(await admin.GetPolicyAsync(ct))))
            .RequireTenantContext()
            .WithName("GetLoginPolicy")
            .Produces<LoginPolicyResponse>();

        access.MapPost("/policy/preview", async (LoginPolicyRequest request, IAccessAdministration admin, CancellationToken ct) =>
            {
                var impact = await admin.PreviewPolicyAsync(ToChange(request), ct);
                return Results.Ok(new LoginPolicyImpactResponse(impact.PersonsWithoutWay));
            })
            .RequireTenantContext()
            .WithName("PreviewLoginPolicy")
            .Produces<LoginPolicyImpactResponse>();

        access.MapPut("/policy", async (LoginPolicyRequest request, IAccessAdministration admin, CancellationToken ct) =>
                Results.Ok(ToResponse(await admin.UpdatePolicyAsync(ToChange(request), ct))))
            .RequireTenantContext().RequireFreshLogin()
            .WithName("UpdateLoginPolicy")
            .Produces<LoginPolicyResponse>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        access.MapGet("/providers", async (IAccessAdministration admin, CancellationToken ct) =>
                Results.Ok((await admin.ListProvidersAsync(ct)).Select(ToResponse).ToList()))
            .RequireTenantContext()
            .WithName("ListTenantProviders")
            .Produces<List<TenantProviderResponse>>();

        access.MapPost("/providers", async (ConfigureProviderRequest request, IAccessAdministration admin, CancellationToken ct) =>
            {
                var (provider, problems) = await admin.ConfigureProviderAsync(request.DisplayName ?? string.Empty, request.Issuer ?? string.Empty, request.ClientId ?? string.Empty, request.ClientSecret ?? string.Empty, ct);
                return provider is null
                    ? Results.ValidationProblem(new Dictionary<string, string[]> { ["issuer"] = problems.ToArray() }, title: "Anbieter nicht aktiviert")
                    : Results.Created($"/api/access/providers/{provider.Id:D}", ToResponse(provider));
            })
            .RequireTenantContext().RequireFreshLogin()
            .WithName("ConfigureTenantProvider")
            .Produces<TenantProviderResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        access.MapDelete("/providers/{id:guid}", async (Guid id, IAccessAdministration admin, CancellationToken ct) =>
                await admin.DisableProviderAsync(id, ct) ? Results.NoContent() : Results.NotFound())
            .RequireTenantContext().RequireFreshLogin()
            .WithName("DisableTenantProvider")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        access.MapGet("/kiosk-devices", async (IKioskService kiosks, CancellationToken ct) =>
                Results.Ok((await kiosks.ListDevicesAsync(ct)).Select(d => ToResponse(d, null)).ToList()))
            .RequireTenantContext().RequireRole()
            .WithName("ListKioskDevices")
            .Produces<List<KioskDeviceAdminResponse>>();

        access.MapPost("/kiosk-devices", async (CreateKioskDeviceRequest request, IKioskService kiosks, CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 80)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["1 bis 80 Zeichen"] });
                }

                var (device, code) = await kiosks.CreateDeviceAsync(request.Name, ct);
                return Results.Created($"/api/access/kiosk-devices/{device.Id:D}", ToResponse(device, code));
            })
            .RequireTenantContext().RequireFreshLogin().RequireRole()
            .WithName("CreateKioskDevice")
            .Produces<KioskDeviceAdminResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        access.MapPost("/kiosk-devices/{id:guid}/revoke", async (Guid id, IKioskService kiosks, CancellationToken ct) =>
                await kiosks.RevokeDeviceAsync(id, ct) ? Results.NoContent() : Results.NotFound())
            .RequireTenantContext().RequireFreshLogin().RequireRole()
            .WithName("RevokeKioskDevice")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden);
    }

    /// <summary>Kiosk-Geräte verwaltet der Tenant-Admin (Zugang 6.1).</summary>
    private static TBuilder RequireRole<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder
    {
        builder.AddEndpointFilter(async (invocation, next) =>
        {
            var current = invocation.HttpContext.RequestServices.GetRequiredService<Platform.Tenancy.ITenantContextAccessor>().Require();
            return current.HasRole(Modules.Organisation.Domain.Role.TenantAdmin) ? await next(invocation) : Results.Forbid();
        });
        return builder;
    }

    private static JoinCodeResponse ToResponse(JoinCodeRecord c) => new(c.Id.ToString("D"), c.Code, c.CreatedAt, c.ExpiresAt, c.UsageLimit, c.UsedCount, c.RevokedAt, c.JoinUrl);

    private static LoginPolicyResponse ToResponse(LoginPolicyRecord p) => new(p.MagicLink, p.Passkey, p.Kiosk, p.DisabledProviderKeys, p.ForcedProviderKeys, p.KioskIdleSeconds, p.AvailableProviders.Select(a => new ProviderResponse(a.Key, a.DisplayName)).ToList());

    private static LoginPolicyChange ToChange(LoginPolicyRequest r) => new(r.MagicLink, r.Passkey, r.Kiosk, r.DisabledProviderKeys ?? [], r.ForcedProviderKeys ?? [], r.KioskIdleSeconds);

    private static TenantProviderResponse ToResponse(TenantProviderRecord p) => new(p.Id.ToString("D"), p.Key, p.DisplayName, p.Issuer, p.ClientId, p.ValidatedAt, p.DisabledAt);

    private static KioskDeviceAdminResponse ToResponse(KioskDeviceRecord d, string? code) => new(d.Id.ToString("D"), d.Name, d.CreatedAt, d.RegisteredAt, d.RevokedAt, d.LastSeenAt, d.LoginCount, code, code is null ? null : d.CreatedAt + Domain.KioskDevice.RegistrationCodeLifetime);
}
