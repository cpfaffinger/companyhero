using CompanyHero.Modules.Branding;
using CompanyHero.Modules.Challenges;
using CompanyHero.Modules.Entitlements;
using CompanyHero.Modules.Feed;
using CompanyHero.Modules.Identity;
using CompanyHero.Modules.Metering;
using CompanyHero.Modules.Notifications;
using CompanyHero.Modules.Organisation;
using CompanyHero.Modules.Privacy;
using CompanyHero.Modules.Progress;
using CompanyHero.Platform.Modules;

namespace CompanyHero.ModuleCatalog;

/// <summary>Alle Module des Monolithen in der Reihenfolge der Domänenkarte, Abschnitt 6. API, Worker und Migrationen laden dieselbe Liste.</summary>
public static class AllModules
{
    public static IReadOnlyList<IModule> Create() =>
    [
        new OrganisationModule(),
        new IdentityModule(),
        new EntitlementsModule(),
        new BrandingModule(),
        new PrivacyModule(),
        new ProgressModule(),
        new ChallengesModule(),
        new FeedModule(),
        new NotificationsModule(),
        new MeteringModule(),
    ];
}
