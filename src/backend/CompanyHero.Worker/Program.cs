using CompanyHero.ModuleCatalog;
using CompanyHero.Platform.Configuration;
using CompanyHero.Platform.Hosting;
using CompanyHero.Platform.Jobs;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddOpenBaoIfConfigured(["app/database"]);
builder.Services.AddPlatformData(builder.Configuration);
builder.AddCompanyHeroHost("companyhero-worker", AllModules.Create());
builder.Services.AddJobWorker();

var app = builder.Build();

// Der Worker spricht kein HTTP nach außen; die Gesundheitsendpunkte dienen Compose und Beobachtung im internen Netz.
// Jobs laufen je Job in eigenem Kontext und eigener Kontexttransaktion (Backend 5.1 Nr. 2, 6.3); siehe JobWorker.
app.MapCompanyHeroHealth();

app.Run();
