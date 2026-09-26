using System.Globalization;
using System.Text;
using CompanyHero.Modules.Branding.Application;
using CompanyHero.Modules.Branding.Domain.Theme;
using CompanyHero.Modules.Identity.Application;
using CompanyHero.Modules.Identity.Application.Access;
using CompanyHero.Modules.Notifications.Domain;
using CompanyHero.Modules.Notifications.Infrastructure;
using CompanyHero.Modules.Organisation.Application;
using CompanyHero.Platform.Data;
using CompanyHero.Platform.Events;
using CompanyHero.Platform.Jobs;
using CompanyHero.Platform.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyHero.Modules.Notifications.Application;

/// <summary>
/// Minimaler PDF-Schreiber (PDF 1.4, eine A4-Seite, Helvetica mit WinAnsi) für den Aushang (Benachrichtigungen 6.2): Farben
/// kommen aus dem Tokensatz des Tenants (A-013), damit der Zettel dieselben Werte trägt wie die App. Keine Fremdbibliothek.
/// </summary>
public sealed class PdfPage
{
    public const double Width = 595.28;
    public const double Height = 841.89;

    private readonly StringBuilder _content = new();

    public PdfPage FillRect(double x, double y, double width, double height, string hexColor)
    {
        var (r, g, b) = Rgb(hexColor);
        _content.Append(CultureInfo.InvariantCulture, $"{r} {g} {b} rg {N(x)} {N(y)} {N(width)} {N(height)} re f\n");
        return this;
    }

    public PdfPage Text(double x, double y, double size, string hexColor, string text, bool bold = false)
    {
        var (r, g, b) = Rgb(hexColor);
        _content.Append(CultureInfo.InvariantCulture, $"BT {r} {g} {b} rg /{(bold ? "F2" : "F1")} {N(size)} Tf {N(x)} {N(y)} Td ({Escape(text)}) Tj ET\n");
        return this;
    }

    public byte[] Build()
    {
        var content = Encoding.Latin1.GetBytes(_content.ToString());
        var objects = new List<byte[]>
        {
            Encoding.ASCII.GetBytes("<< /Type /Catalog /Pages 2 0 R >>"),
            Encoding.ASCII.GetBytes("<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            Encoding.ASCII.GetBytes(FormattableString.Invariant($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {N(Width)} {N(Height)}] /Contents 4 0 R /Resources << /Font << /F1 5 0 R /F2 6 0 R >> >> >>")),
            Concat(Encoding.ASCII.GetBytes(FormattableString.Invariant($"<< /Length {content.Length} >>\nstream\n")), content, Encoding.ASCII.GetBytes("\nendstream")),
            Encoding.ASCII.GetBytes("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>"),
            Encoding.ASCII.GetBytes("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>"),
        };

        using var stream = new MemoryStream();
        void Write(string s) => stream.Write(Encoding.ASCII.GetBytes(s));
        Write("%PDF-1.4\n%âãÏÓ\n");
        var offsets = new List<long>();
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(stream.Position);
            Write(FormattableString.Invariant($"{i + 1} 0 obj\n"));
            stream.Write(objects[i]);
            Write("\nendobj\n");
        }

        var xref = stream.Position;
        Write(FormattableString.Invariant($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n"));
        foreach (var offset in offsets)
        {
            Write(FormattableString.Invariant($"{offset:0000000000} 00000 n \n"));
        }

        Write(FormattableString.Invariant($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n"));
        return stream.ToArray();
    }

    private static string N(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static (string R, string G, string B) Rgb(string hex)
    {
        var h = hex.TrimStart('#');
        if (h.Length != 6)
        {
            throw new ArgumentException("Farbe als #RRGGBB erwartet.", nameof(hex));
        }

        string Part(int i) => (Convert.ToInt32(h.Substring(i, 2), 16) / 255.0).ToString("0.###", CultureInfo.InvariantCulture);
        return (Part(0), Part(2), Part(4));
    }

    private static string Escape(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '(': sb.Append("\\("); break;
                case ')': sb.Append("\\)"); break;
                case '\r' or '\n': sb.Append(' '); break;
                default: sb.Append(c is < (char)256 ? c : '?'); break;
            }
        }

        return sb.ToString();
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var result = new byte[parts.Sum(p => p.Length)];
        var offset = 0;
        foreach (var part in parts)
        {
            part.CopyTo(result, offset);
            offset += part.Length;
        }

        return result;
    }
}

/// <summary>Letzter bekannter Stand der Challenge aus den abonnierten Ereignissen (Start, Meilenstein, Ende); Benachrichtigungen liest nie Tabellen von Challenges.</summary>
public sealed class ChallengeSnapshot : ITenantOwned
{
    private ChallengeSnapshot(TenantId tenantId, Guid challengeId, string title, int milestone, bool ended, DateTimeOffset updatedAt)
    {
        TenantId = tenantId;
        ChallengeId = challengeId;
        Title = title;
        Milestone = milestone;
        Ended = ended;
        UpdatedAt = updatedAt;
    }

    public TenantId TenantId { get; }

    public Guid ChallengeId { get; }

    public string Title { get; private set; }

    /// <summary>Höchster gemeldeter Meilenstein in Prozent (0, 25, 50, 75, 100).</summary>
    public int Milestone { get; private set; }

    public bool Ended { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static ChallengeSnapshot Started(TenantId tenantId, Guid challengeId, string title, DateTimeOffset at) => new(tenantId, challengeId, title, 0, false, at.ToUniversalTime());

    public void Apply(string title, int percent, bool ended, DateTimeOffset at)
    {
        Title = title;
        Milestone = Math.Max(Milestone, percent);
        Ended |= ended;
        UpdatedAt = at.ToUniversalTime();
    }

    public int? NextMilestone => Ended ? null : new[] { 25, 50, 75, 100 }.FirstOrDefault(m => m > Milestone, 0) is var next && next > 0 ? next : null;
}

public sealed record AushangRecord(Guid Id, string Week, DateTimeOffset GeneratedAt);

/// <summary>Aushang (Benachrichtigungen 6.2): erzeugen und laden; Programm-Manager, Tenant-Admin und Botschafter.</summary>
public interface IAushangService
{
    Task<AushangRecord> RenderAsync(CancellationToken cancellationToken);

    Task<(AushangRecord Record, byte[] Pdf)?> GetLatestAsync(CancellationToken cancellationToken);
}

internal sealed class AushangService(NotificationsDbContext db, IContextTransaction transaction, ITenantContextAccessor context, ITenantTimeZone timeZone, IThemeService themes, IJoinLinks joinLinks, MailTexts texts, TimeProvider clock) : IAushangService
{
    public async Task<AushangRecord> RenderAsync(CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        var now = clock.GetUtcNow();
        var zone = await timeZone.GetAsync(cancellationToken);
        var week = Aushang.WeekOf(TenantTimeZone.DayOf(now, zone));
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var theme = await themes.GetCurrentAsync(cancellationToken);
        var t = await texts.ResolveAsync(cancellationToken);
        var snapshot = await db.ChallengeSnapshots.AsNoTracking().Where(s => s.TenantId == tenantId).OrderByDescending(s => s.Ended ? 0 : 1).ThenByDescending(s => s.UpdatedAt).FirstOrDefaultAsync(cancellationToken);
        var join = await joinLinks.GetCurrentAsync(cancellationToken);

        var tokens = theme.Tokens.Hell;
        var page = new PdfPage();
        page.FillRect(0, PdfPage.Height - 120, PdfPage.Width, 120, tokens[ColorRoles.Primary]);
        page.Text(48, PdfPage.Height - 62, 26, tokens[ColorRoles.OnPrimary], t.Produktname, bold: true);
        page.Text(48, PdfPage.Height - 92, 12, tokens[ColorRoles.OnPrimary], t.Text("aushang.woche", ("woche", week)));

        var y = PdfPage.Height - 190;
        page.Text(48, y, 20, tokens[ColorRoles.OnSurface], t.Text("aushang.titel"), bold: true);
        y -= 40;
        if (snapshot is null || snapshot.Ended)
        {
            page.Text(48, y, 13, tokens[ColorRoles.OnSurfaceVariant], t.Text("aushang.keineChallenge"));
            y -= 30;
        }
        else
        {
            page.Text(48, y, 16, tokens[ColorRoles.OnSurface], snapshot.Title, bold: true);
            y -= 34;
            page.FillRect(48, y, PdfPage.Width - 96, 22, tokens[ColorRoles.ProgressContainer]);
            page.FillRect(48, y, (PdfPage.Width - 96) * snapshot.Milestone / 100.0, 22, tokens[ColorRoles.Progress]);
            y -= 22;
            page.Text(48, y, 12, tokens[ColorRoles.OnSurface], t.Text("aushang.stand", ("prozent", NotificationRules.Percent(snapshot.Milestone))));
            y -= 20;
            if (snapshot.NextMilestone is { } next)
            {
                page.Text(48, y, 12, tokens[ColorRoles.OnSurfaceVariant], t.Text("aushang.naechsterMeilenstein", ("prozent", NotificationRules.Percent(next))));
                y -= 20;
            }
        }

        y -= 30;
        if (join is not null)
        {
            page.FillRect(48, y - 70, PdfPage.Width - 96, 90, tokens[ColorRoles.PrimaryContainer]);
            page.Text(64, y - 4, 14, tokens[ColorRoles.OnPrimaryContainer], t.Text("aushang.beitritt"), bold: true);
            page.Text(64, y - 30, 22, tokens[ColorRoles.OnPrimaryContainer], join.Code, bold: true);
            page.Text(64, y - 56, 11, tokens[ColorRoles.OnPrimaryContainer], join.JoinUrl);
        }

        page.Text(48, 48, 9, tokens[ColorRoles.OnSurfaceVariant], t.Text("aushang.fusszeile"));
        var aushang = Aushang.Create(tenantId, week, now, page.Build());
        db.Aushaenge.Add(aushang);
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return new AushangRecord(aushang.Id, aushang.Week, aushang.GeneratedAt);
    }

    public async Task<(AushangRecord Record, byte[] Pdf)?> GetLatestAsync(CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var latest = await db.Aushaenge.AsNoTracking().Where(a => a.TenantId == tenantId).OrderByDescending(a => a.GeneratedAt).FirstOrDefaultAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return latest is null ? null : (new AushangRecord(latest.Id, latest.Week, latest.GeneratedAt), latest.Pdf);
    }
}

/// <summary>Projektion der Challenge-Ereignisse in den Schnappschuss für den Aushang; läuft im selben Abonnement wie die Pipeline.</summary>
internal sealed class ChallengeSnapshotProjector(NotificationsDbContext db, IContextTransaction transaction, ITenantContextAccessor context)
{
    public async Task ApplyAsync(DomainEventRecord record, CancellationToken cancellationToken)
    {
        var tenantId = context.Require().RequireTenant();
        var payload = System.Text.Json.Nodes.JsonNode.Parse(record.PayloadJson)?.AsObject();
        if (payload is null || !Guid.TryParse(payload["challengeId"]?.ToString(), out var challengeId))
        {
            return;
        }

        var title = payload["title"]?.ToString() ?? string.Empty;
        var percent = payload["percent"]?.GetValue<int>() ?? 0;
        await using var tx = await transaction.BeginAsync(cancellationToken);
        var snapshot = await db.ChallengeSnapshots.SingleOrDefaultAsync(s => s.TenantId == tenantId && s.ChallengeId == challengeId, cancellationToken);
        if (snapshot is null)
        {
            snapshot = ChallengeSnapshot.Started(tenantId, challengeId, title, record.OccurredAt);
            db.ChallengeSnapshots.Add(snapshot);
        }

        snapshot.Apply(title, percent, record.Type == "challenges.ended", record.OccurredAt);
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }
}

/// <summary>Wöchentlicher Aushang je Tenant als Job des Workers (Benachrichtigungen 6.2): montags in der Tenant-Zeitzone, ein Job je Woche.</summary>
internal sealed class AushangWeeklyTask(ITenantScopeFactory scopes, IOrganisationDirectory organisations, TimeProvider clock) : IScheduledTask
{
    public static string Name => "notifications.aushang.weekly";

    public static TimeSpan Interval => TimeSpan.FromHours(6);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        foreach (var tenant in await organisations.ListActiveTenantsAsync(cancellationToken))
        {
            await scopes.RunAsync(TenantContext.ForTenant(tenant), async (sp, ct) =>
            {
                var zone = await sp.GetRequiredService<ITenantTimeZone>().GetAsync(ct);
                var day = TenantTimeZone.DayOf(now, zone);
                if (day.DayOfWeek != DayOfWeek.Monday)
                {
                    return;
                }

                var db = sp.GetRequiredService<NotificationsDbContext>();
                var transaction = sp.GetRequiredService<IContextTransaction>();
                await using var tx = await transaction.BeginAsync(ct);
                var settings = await db.TenantSettings.AsNoTracking().SingleOrDefaultAsync(s => s.TenantId == tenant, ct);
                if (settings is { AushangEnabled: false })
                {
                    await tx.CommitAsync(ct);
                    return;
                }

                var week = Aushang.WeekOf(day);
                await sp.GetRequiredService<IJobQueue>().EnqueueAsync(new JobRequest(AushangRenderHandler.JobType, week, $"aushang:{week}"), ct);
                await tx.CommitAsync(ct);
            }, cancellationToken);
        }
    }
}

internal sealed class AushangRenderHandler(IAushangService aushang) : IJobHandler
{
    public static string JobType => "notifications.aushang.render";

    public Task HandleAsync(JobExecution job, CancellationToken cancellationToken) => aushang.RenderAsync(cancellationToken);
}
