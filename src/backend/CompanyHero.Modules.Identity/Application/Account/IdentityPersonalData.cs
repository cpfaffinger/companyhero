using System.Text.Json.Nodes;
using CompanyHero.Modules.Identity.Domain;
using CompanyHero.Modules.Identity.Infrastructure;
using CompanyHero.Platform.Data;
using CompanyHero.Platform.Privacy;
using CompanyHero.Platform.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace CompanyHero.Modules.Identity.Application.Account;

/// <summary>Auskunft (Datenschutz 6.4): Profil und Anmeldewege ohne Geheimnisse; Löschung (5.2): die Person selbst nach dem Austritt.</summary>
internal sealed class IdentityPersonalData(IdentityDbContext db, IContextTransaction transaction, ITenantContextAccessor context) : IPersonalDataExporter, IPersonalDataEraser
{
    public string Section => "profil";

    public async Task<JsonNode> ExportAsync(PersonId personId, CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var person = await db.Persons.AsNoTracking().SingleOrDefaultAsync(p => p.TenantId == tenantId && p.Id == personId, cancellationToken);
        var passkeys = await db.Passkeys.AsNoTracking().Where(p => p.TenantId == tenantId && p.PersonId == personId).Select(p => new { p.DeviceName, p.CreatedAt }).ToListAsync(cancellationToken);
        var email = await db.EmailLogins.AsNoTracking().Where(e => e.TenantId == tenantId && e.PersonId == personId).Select(e => e.Email).SingleOrDefaultAsync(cancellationToken);
        var providers = await db.ExternalLogins.AsNoTracking().Where(e => e.TenantId == tenantId && e.PersonId == personId).Select(e => e.ProviderKey).ToListAsync(cancellationToken);
        var kiosk = await db.KioskCredentials.AsNoTracking().Where(k => k.TenantId == tenantId && k.PersonId == personId).Select(k => k.KioskId).SingleOrDefaultAsync(cancellationToken);
        var sessions = await db.Sessions.AsNoTracking().Where(s => s.TenantId == tenantId && s.PersonId == personId && s.RevokedAt == null).Select(s => new { s.Kind, s.CreatedAt, s.LastSeenAt }).ToListAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return new JsonObject
        {
            ["anzeigename"] = person?.DisplayName,
            ["klarname"] = person?.RealName,
            ["beitritt"] = person?.CreatedAt,
            ["anmeldewege"] = new JsonObject
            {
                ["passkeys"] = new JsonArray(passkeys.Select(p => (JsonNode)new JsonObject { ["geraet"] = p.DeviceName, ["seit"] = p.CreatedAt }).ToArray()),
                ["email"] = email,
                ["anbieter"] = new JsonArray(providers.Select(p => (JsonNode)p).ToArray()),
                ["kioskKennung"] = kiosk,
            },
            ["sitzungen"] = new JsonArray(sessions.Select(s => (JsonNode)new JsonObject { ["art"] = s.Kind.ToString(), ["seit"] = s.CreatedAt, ["zuletzt"] = s.LastSeenAt }).ToArray()),
        };
    }

    public async Task EraseAsync(PersonId personId, CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        // Nur ausgetretene Personen; Anmeldewege sind beim Austritt bereits entfernt, hier fallen Person und Sitzungen.
        await db.Sessions.Where(s => s.TenantId == tenantId && s.PersonId == personId).ExecuteDeleteAsync(cancellationToken);
        await db.Persons.Where(p => p.TenantId == tenantId && p.Id == personId && p.State == PersonState.Left).ExecuteDeleteAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }
}
