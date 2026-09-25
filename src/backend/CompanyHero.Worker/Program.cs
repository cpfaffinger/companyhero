using CompanyHero.ModuleCatalog;
using CompanyHero.Platform.Configuration;
using CompanyHero.Platform.Hosting;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddOpenBaoIfConfigured(["app/database"]);
builder.Services.AddPlatformData(builder.Configuration);
builder.AddCompanyHeroHost("companyhero-worker", AllModules.Create());

var app = builder.Build();

// Der Worker spricht kein HTTP nach außen; die Gesundheitsendpunkte dienen Compose und Beobachtung im internen Netz.
// Jobs erhalten ab Stufe 3 je Job einen eigenen Kontext über ITenantScopeFactory (Backend 5.1 Nr. 2).
app.MapCompanyHeroHealth();

app.Run();
