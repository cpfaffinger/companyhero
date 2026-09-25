using CompanyHero.Api.Contracts;
using CompanyHero.ModuleCatalog;
using CompanyHero.Platform.Configuration;
using CompanyHero.Platform.Hosting;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddOpenBaoIfConfigured(["app/database"]);
builder.AddCompanyHeroHost("companyhero-api", AllModules.Create());
builder.Services.AddPlatformData(builder.Configuration);

var app = builder.Build();

app.MapCompanyHeroHealth();
app.MapGet("/api/version", () => Results.Ok(new VersionResponse(CompanyHeroHost.Version)));

app.Run();

/// <summary>Sichtbarer Einstiegspunkt für WebApplicationFactory in den Integrationstests.</summary>
public partial class Program;
