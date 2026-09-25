using CompanyHero.Api.Contracts;
using CompanyHero.ModuleCatalog;
using CompanyHero.Platform.Configuration;
using CompanyHero.Platform.Hosting;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddOpenBaoIfConfigured(["app/database"]);
var modules = AllModules.Create();
builder.Services.AddPlatformData(builder.Configuration);
builder.AddCompanyHeroHost("companyhero-api", modules);

// Die serverseitige Cookie-Sitzung (A-007) kommt in Stufe 4; bis dahin ist kein Schema registriert und jeder Request
// bleibt ohne Identität. Der Tenant-Kontext entsteht ausschließlich aus geprüfter Identität und Mitgliedschaft.
builder.Services.AddAuthentication();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseTenantContext();

app.MapCompanyHeroHealth();
app.MapGet("/api/version", () => Results.Ok(new VersionResponse(CompanyHeroHost.Version)));
foreach (var module in modules)
{
    module.MapEndpoints(app);
}

app.Run();

/// <summary>Sichtbarer Einstiegspunkt für WebApplicationFactory in den Integrationstests.</summary>
public partial class Program;
