using CompanyHero.ModuleCatalog;
using CompanyHero.Platform.Configuration;
using CompanyHero.Platform.Hosting;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddOpenBaoIfConfigured(["app/database"]);
builder.AddCompanyHeroHost("companyhero-worker", AllModules.Create());
builder.Services.AddPlatformData(builder.Configuration);

var app = builder.Build();

// Der Worker spricht kein HTTP nach außen; die Gesundheitsendpunkte dienen Compose und Beobachtung im internen Netz.
app.MapCompanyHeroHealth();

app.Run();
