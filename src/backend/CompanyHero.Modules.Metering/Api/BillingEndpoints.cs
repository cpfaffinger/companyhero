using System.Globalization;
using CompanyHero.Modules.Metering.Application;
using CompanyHero.Modules.Metering.Domain;
using CompanyHero.Modules.Organisation.Domain;
using CompanyHero.Platform.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CompanyHero.Modules.Metering.Api;

/// <summary>Rechnungsposition (Metering 6.1 Nr. 3): Regel, Metrik, Menge (vier Nachkommastellen), Einzelpreis, Rechenweg, Betrag je Position gerundet. Beträge als Strings (K13).</summary>
public sealed record InvoiceLineResponse(string RuleKey, string Model, string? Module, string? Metric, string Quantity, string? UnitPrice, string Calculation, string Net, string Tax, bool Rated);

public sealed record ForecastResponse(string Net, bool Estimate, int ElapsedDays, int DaysInMonth);

/// <summary>Klartextdefinition jeder verwendeten Metrik (Metering 5: immer sichtbar); personennahe Metriken nur als Monatssumme.</summary>
public sealed record MetricDefinitionResponse(string Module, string Metric, string TextKey, bool Personal, string Model);

/// <summary>Kostenvorschau des laufenden Monats (Metering 5, A-073): Positionen je Regel, Summen, „wären X Euro gewesen“, Prognose als Schätzung.</summary>
public sealed record CostPreviewResponse(string Period, string Currency, string PlanName, IReadOnlyList<InvoiceLineResponse> Lines, string Net, string Tax, string Gross, string WouldHaveBeen, ForecastResponse Forecast, IReadOnlyList<MetricDefinitionResponse> Definitions);

/// <summary>Simulation vor Buchung (Metering 5, Entitlements 4.2 Nr. 2): Auswirkung auf laufenden Monat und Folgemonat.</summary>
public sealed record SimulationResponse(string Module, string ThisMonth, string NextMonth, IReadOnlyList<string> UsageBasedMetrics, string Currency);

public sealed record UsageDayResponse(DateOnly Day, string Quantity);

/// <summary>Verbrauch je Metrik aus Tagesaggregaten (A-023): <c>days</c> ist bei personennahen Metriken <c>null</c>, nur die Summe erscheint.</summary>
public sealed record UsageMetricResponse(string Module, string Metric, string TextKey, bool Personal, string Total, string Rated, string Unrated, IReadOnlyList<UsageDayResponse>? Days);

public sealed record UsageResponse(string Period, bool Sealed, IReadOnlyList<UsageMetricResponse> Metrics);

public sealed record InvoiceSummaryResponse(string InvoiceId, string Period, DateTimeOffset CreatedAt, string Currency, string Net, string Tax, string Gross, string Status);

public sealed record InvoiceDraftResponse(string InvoiceId, string Period, DateTimeOffset CreatedAt, string Currency, string TaxRate, string PlanName, string Net, string Tax, string Gross, string WouldHaveBeen, string Status, IReadOnlyList<InvoiceLineResponse> Lines);

/// <summary>Verwendete Metriken ohne Beträge (Einsichtsrolle, Metering 5).</summary>
public sealed record MetricsResponse(IReadOnlyList<MetricDefinitionResponse> Metrics);

public sealed record PeriodResponse(string Period, DateTimeOffset OpenedAt, DateTimeOffset? SealedAt, DateTimeOffset? SaltDestroyedAt);

internal static class BillingEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        // Programm-Manager, Botschafter und Einsichtsrolle sehen keine Kostenvorschau, keine Rechnungen und kein Verbrauchsdetail (Metering 5, A-073).
        var billing = endpoints.MapGroup("/api/billing").HandleBillingErrors();

        billing.MapGet("/preview", async (ICostPreview preview, IUsageReports usage, CancellationToken ct) =>
            {
                var current = await preview.CurrentAsync(ct);
                var c = current.Calculation;
                var definitions = (await usage.UsedMetricsAsync(ct)).Select(m => new MetricDefinitionResponse(m.Module, m.Metric, MeteringMetrics.DefinitionKey(m.Metric), MeteringMetrics.IsPersonal(m.Metric), ModelName(m.Model))).ToList();
                return Results.Ok(new CostPreviewResponse(current.Period, c.Currency, current.PlanName, c.Lines.Select(ToResponse).ToList(), Money(c.Net), Money(c.Tax), Money(c.Gross), Money(c.WouldHaveBeen), new ForecastResponse(Money(current.Forecast.Net), true, current.Forecast.ElapsedDays, current.Forecast.DaysInMonth), definitions));
            })
            .RequireTenantContext().RequireRoles(Role.TenantAdmin)
            .WithName("GetCostPreview")
            .Produces<CostPreviewResponse>();

        billing.MapGet("/preview/simulate", async (string module, ICostPreview preview, CancellationToken ct) =>
            {
                var simulation = await preview.SimulateAsync(module, ct);
                return Results.Ok(new SimulationResponse(simulation.Module, Money(simulation.ThisMonth), Money(simulation.NextMonth), simulation.UsageBasedMetrics, simulation.Currency));
            })
            .RequireTenantContext().RequireRoles(Role.TenantAdmin)
            .WithName("SimulateBooking")
            .Produces<SimulationResponse>();

        billing.MapGet("/usage", async (string period, IUsageReports usage, CancellationToken ct) =>
            {
                if (!PeriodRules.IsValid(period))
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["period"] = ["Periode als YYYY-MM erwartet."] });
                }

                var report = await usage.UsageAsync(period, ct);
                return Results.Ok(new UsageResponse(report.Period, report.Sealed, report.Metrics.Select(m =>
                    new UsageMetricResponse(m.Module, m.Metric, MeteringMetrics.DefinitionKey(m.Metric), m.Personal, Quantity(m.Total), Quantity(m.Rated), Quantity(m.Unrated), m.Days?.Select(d => new UsageDayResponse(d.Day, Quantity(d.Quantity))).ToList())).ToList()));
            })
            .RequireTenantContext().RequireRoles(Role.TenantAdmin)
            .WithName("GetUsage")
            .Produces<UsageResponse>()
            .ProducesValidationProblem();

        billing.MapGet("/usage/export", async (string period, IUsageReports usage, CancellationToken ct) =>
            {
                if (!PeriodRules.IsValid(period))
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["period"] = ["Periode als YYYY-MM erwartet."] });
                }

                var csv = await usage.ExportCsvAsync(period, ct);
                return Results.Text(csv, "text/csv", System.Text.Encoding.UTF8);
            })
            .RequireTenantContext().RequireRoles(Role.TenantAdmin)
            .WithName("ExportUsage")
            .Produces(StatusCodes.Status200OK, contentType: "text/csv")
            .ProducesValidationProblem();

        billing.MapGet("/invoices", async (IInvoiceDrafts drafts, CancellationToken ct) =>
                Results.Ok((await drafts.ListAsync(ct)).Select(i => new InvoiceSummaryResponse(i.Id.ToString("D"), i.Period, i.CreatedAt, i.Currency, Money(i.Net), Money(i.Tax), Money(i.Gross), "draft")).ToList()))
            .RequireTenantContext().RequireRoles(Role.TenantAdmin)
            .WithName("ListInvoices")
            .Produces<List<InvoiceSummaryResponse>>();

        billing.MapGet("/invoices/{invoiceId:guid}", async (Guid invoiceId, IInvoiceDrafts drafts, CancellationToken ct) =>
            {
                var draft = await drafts.GetAsync(invoiceId, ct);
                return draft is null
                    ? Results.NotFound()
                    : Results.Ok(new InvoiceDraftResponse(draft.Id.ToString("D"), draft.Period, draft.CreatedAt, draft.Currency, Rate(draft.TaxRate), draft.PlanName, Money(draft.Net), Money(draft.Tax), Money(draft.Gross), Money(draft.WouldHaveBeen), "draft", draft.Lines.Select(ToResponse).ToList()));
            })
            .RequireTenantContext().RequireRoles(Role.TenantAdmin)
            .WithName("GetInvoice")
            .Produces<InvoiceDraftResponse>()
            .Produces(StatusCodes.Status404NotFound);

        billing.MapGet("/periods", async (IBillingPeriods periods, CancellationToken ct) =>
                Results.Ok((await periods.ListAsync(ct)).Select(p => new PeriodResponse(p.Period, p.OpenedAt, p.SealedAt, p.SaltDestroyedAt)).ToList()))
            .RequireTenantContext().RequireRoles(Role.TenantAdmin)
            .WithName("ListBillingPeriods")
            .Produces<List<PeriodResponse>>();

        // Einsichtsrolle sieht, welche Metriken vertraglich verwendet werden, ohne Beträge (Metering 5, 9.12).
        billing.MapGet("/metrics", async (IUsageReports usage, CancellationToken ct) =>
                Results.Ok(new MetricsResponse((await usage.UsedMetricsAsync(ct)).Select(m => new MetricDefinitionResponse(m.Module, m.Metric, MeteringMetrics.DefinitionKey(m.Metric), MeteringMetrics.IsPersonal(m.Metric), ModelName(m.Model))).ToList())))
            .RequireTenantContext().RequireRoles(Role.TenantAdmin, Role.Insight)
            .WithName("ListBillingMetrics")
            .Produces<MetricsResponse>();
    }

    private static InvoiceLineResponse ToResponse(InvoiceLine l) =>
        new(l.RuleKey, ModelName(l.Model), l.Module, l.Metric, Quantity(l.Quantity), l.UnitPrice is { } u ? Rate(u) : null, l.Calculation, Money(l.Net), Money(l.Tax), l.Rated);

    public static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    public static string Quantity(decimal value) => value.ToString("0.0000", CultureInfo.InvariantCulture);

    public static string Rate(decimal value) => value.ToString("0.0000", CultureInfo.InvariantCulture);

    public static string ModelName(PriceModel model) => model switch
    {
        PriceModel.Flat => "flat",
        PriceModel.PerUnit => "per_unit",
        PriceModel.Tiered => "tiered",
        PriceModel.Volume => "volume",
        PriceModel.Package => "package",
        PriceModel.Step => "step",
        PriceModel.OneTime => "one_time",
        PriceModel.RevenueShare => "revenue_share",
        PriceModel.MinMax => "min_max",
        _ => "credit",
    };

    private static TBuilder HandleBillingErrors<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder
    {
        builder.AddEndpointFilter(async (invocation, next) =>
        {
            try
            {
                return await next(invocation);
            }
            catch (ArgumentException ex)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { [ex.ParamName ?? "request"] = [ex.Message] });
            }
        });
        return builder;
    }
}
