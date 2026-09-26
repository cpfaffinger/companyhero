using CompanyHero.Modules.Identity.Domain;
using CompanyHero.Platform.Tenancy;

namespace CompanyHero.Domain.Tests;

/// <summary>
/// Zugang 1, 2.1, 2.3, 3.1, 3.3, 3.4, 5, 6.1, 6.2; A-005, A-014 bis A-018: Codes ohne verwechselbare Zeichen, Sitzungslaufzeiten,
/// Kiosk-PIN und Drosselung, Anmeldewege des Tenants, Rollenregeln. Reine Fachregeln ohne Infrastruktur.
/// </summary>
public sealed class AccessRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 8, 0, 0, TimeSpan.Zero);
    private static readonly TenantId Tenant = TenantId.New();

    [Fact]
    public void Codes_kommen_aus_dem_Alphabet_ohne_verwechselbare_Zeichen_und_werden_normalisiert()
    {
        Assert.DoesNotContain(AccessCodes.Alphabet, c => c is 'I' or 'O' or '0' or '1');
        Assert.Equal(32, AccessCodes.Alphabet.Length);

        var join = AccessCodes.NewJoinCode();
        var recovery = AccessCodes.NewRecoveryCode();
        Assert.Equal(8, join.Length);
        Assert.Equal(12, recovery.Length);
        Assert.True(AccessCodes.IsWellFormed(join, 8));
        Assert.Equal("ABC-DEF-GHJ-KLM", AccessCodes.Grouped("ABCDEFGHJKLM"));
        Assert.Equal("ABCDEFGHJKLM", AccessCodes.Normalize(" abc-def ghj_klm "));
        Assert.NotEqual(AccessCodes.NewJoinCode(), AccessCodes.NewJoinCode());
        Assert.Matches("^[0-9]{6}$", AccessCodes.NewKioskId());
    }

    [Fact]
    public void Wiederherstellungscode_ist_einmalig_und_wird_nach_Verwendung_ersetzt()
    {
        var person = PersonId.New();
        var (entry, code) = RecoveryCode.Issue(Tenant, person, Now);
        Assert.True(entry.Matches(AccessCodes.Grouped(code).ToLowerInvariant()));
        Assert.DoesNotContain(code, entry.CodeHash, StringComparison.Ordinal);

        var renewed = entry.Renew(Now.AddMinutes(1));
        Assert.False(entry.Matches(code));
        Assert.True(entry.Matches(renewed));
        Assert.NotEqual(code, renewed);
    }

    [Fact]
    public void Identitaetsindex_haelt_nur_Hash_aus_Issuer_und_Subject()
    {
        var hash = IdentityIndexEntry.HashExternal("https://login.example/", "subject-42");
        Assert.Equal(hash, IdentityIndexEntry.HashExternal("https://login.example", "subject-42"));
        Assert.NotEqual(hash, IdentityIndexEntry.HashExternal("https://other.example", "subject-42"));
        Assert.DoesNotContain("subject-42", hash, StringComparison.Ordinal);
        Assert.Equal(IdentityIndexEntry.HashEmail("Anna@Example.org "), IdentityIndexEntry.HashEmail("anna@example.org"));
    }

    [Fact]
    public void Sitzungslaufzeiten_folgen_der_Sitzungsart_und_das_absolute_Ende_gewinnt()
    {
        var person = PersonId.New();
        var (member, token) = Session.Issue(Tenant, person, SessionKind.Member, Now);
        Assert.Equal(Now.AddDays(30), member.SlidingUntil);
        Assert.Equal(Now.AddDays(180), member.AbsoluteUntil);
        Assert.Equal(AccessCodes.Hash(token), member.TokenHash);
        Assert.DoesNotContain(token, member.TokenHash, StringComparison.Ordinal);

        member.Touch(Now.AddDays(175));
        Assert.Equal(Now.AddDays(180), member.SlidingUntil);
        Assert.True(member.IsValidAt(Now.AddDays(179)));
        Assert.False(member.IsValidAt(Now.AddDays(180)));

        var (privileged, _) = Session.Issue(Tenant, person, SessionKind.Privileged, Now);
        Assert.Equal(Now.AddHours(8), privileged.SlidingUntil);
        Assert.Equal(Now.AddHours(24), privileged.AbsoluteUntil);
        Assert.False(privileged.IsValidAt(Now.AddHours(8)));
        Assert.True(privileged.IsFreshAt(Now.AddMinutes(15), TimeSpan.FromMinutes(15)));
        Assert.False(privileged.IsFreshAt(Now.AddMinutes(16), TimeSpan.FromMinutes(15)));

        var (device, _) = Session.Issue(Tenant, null, SessionKind.KioskDevice, Now, Guid.NewGuid());
        Assert.Null(device.SlidingUntil);
        Assert.Null(device.AbsoluteUntil);
        Assert.True(device.IsValidAt(Now.AddYears(3)));

        var (kiosk, _) = Session.Issue(Tenant, person, SessionKind.KioskPerson, Now, Guid.NewGuid(), kioskIdleSeconds: 90);
        Assert.Equal(Now.AddSeconds(90), kiosk.SlidingUntil);
        Assert.Equal(Now.AddMinutes(10), kiosk.AbsoluteUntil);
        Assert.Throws<ArgumentException>(() => Session.Issue(Tenant, person, SessionKind.KioskPerson, Now));

        member.Revoke(Now);
        Assert.False(member.IsValidAt(Now.AddSeconds(1)));
    }

    [Fact]
    public void Kiosk_Auto_Logout_liegt_zwischen_30_und_120_Sekunden_Voreinstellung_60()
    {
        Assert.Equal(TimeSpan.FromSeconds(60), SessionPolicy.For(SessionKind.KioskPerson).Sliding);
        Assert.Equal(TimeSpan.FromSeconds(30), SessionPolicy.For(SessionKind.KioskPerson, 5).Sliding);
        Assert.Equal(TimeSpan.FromSeconds(120), SessionPolicy.For(SessionKind.KioskPerson, 600).Sliding);
        Assert.Equal(TimeSpan.FromMinutes(10), SessionPolicy.For(SessionKind.KioskPerson).Absolute);
    }

    [Theory]
    [InlineData("0000", false)]
    [InlineData("1234", false)]
    [InlineData("4321", false)]
    [InlineData("1212", false)]
    [InlineData("1221", false)]
    [InlineData("12a4", false)]
    [InlineData("123", false)]
    [InlineData("2580", true)]
    [InlineData("7391", true)]
    public void Triviale_PINs_werden_abgelehnt(string pin, bool accepted)
    {
        Assert.Equal(accepted, KioskPin.IsAcceptable(pin));
    }

    [Fact]
    public void Kiosk_Kennung_und_PIN_speichern_nur_den_gesalzenen_Hash()
    {
        var person = PersonId.New();
        var credential = KioskCredential.Create(Tenant, person, "482913", "7391", Now);
        Assert.True(credential.PinMatches("7391"));
        Assert.False(credential.PinMatches("7390"));
        Assert.DoesNotContain("7391", credential.PinHash, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => credential.ChangePin("1111", Now));
        var other = KioskCredential.Create(Tenant, PersonId.New(), "482914", "7391", Now);
        Assert.NotEqual(credential.PinHash, other.PinHash);
    }

    [Fact]
    public void Drosselung_fuenf_Fehlversuche_sperren_die_Kennung_zwanzig_das_Geraet_je_15_Minuten()
    {
        var kennung = Enumerable.Range(0, 4).Select(i => Now.AddSeconds(i)).ToList();
        Assert.Equal(KioskThrottle.Verdict.Open, KioskThrottle.Evaluate(kennung, kennung, Now.AddSeconds(5)));

        kennung.Add(Now.AddSeconds(4));
        Assert.Equal(KioskThrottle.Verdict.KioskIdLocked, KioskThrottle.Evaluate(kennung, kennung, Now.AddSeconds(5)));
        Assert.Equal(Now.AddSeconds(4).AddMinutes(15), KioskThrottle.LockedUntil(kennung, KioskThrottle.KioskIdLimit, Now.AddSeconds(5)));
        Assert.Equal(KioskThrottle.Verdict.Open, KioskThrottle.Evaluate(kennung, kennung, Now.AddSeconds(4).AddMinutes(15).AddSeconds(1)));

        var device = Enumerable.Range(0, 20).Select(i => Now.AddSeconds(i * 10)).ToList();
        Assert.Equal(KioskThrottle.Verdict.DeviceLocked, KioskThrottle.Evaluate(device, [], Now.AddSeconds(200)));
        var spread = Enumerable.Range(0, 20).Select(i => Now.AddMinutes(i)).ToList();
        // 20 Fehlversuche über 19 Minuten liegen nicht im 15-Minuten-Fenster.
        Assert.Equal(KioskThrottle.Verdict.Open, KioskThrottle.Evaluate(spread, [], Now.AddMinutes(19).AddSeconds(1)));
    }

    [Fact]
    public void Geraet_registriert_sich_einmal_rotiert_nach_30_Tagen_und_verliert_beim_Widerruf_sein_Geheimnis()
    {
        var (device, code) = KioskDevice.Create(Tenant, "Eingang Halle 2", Now);
        Assert.True(device.RegistrationCodeMatches(AccessCodes.Grouped(code, 4), Now.AddMinutes(14)));
        Assert.False(device.RegistrationCodeMatches(code, Now.AddMinutes(16)));

        var secret = device.Register(Now);
        Assert.True(device.IsActive);
        Assert.False(device.RegistrationCodeMatches(code, Now));
        Assert.True(device.SecretMatches(secret));
        Assert.False(device.NeedsRotation(Now.AddDays(29)));
        Assert.True(device.NeedsRotation(Now.AddDays(30)));

        var rotated = device.Rotate(Now.AddDays(30));
        Assert.False(device.SecretMatches(secret));
        Assert.True(device.SecretMatches(rotated));

        device.Revoke(Now.AddDays(31));
        Assert.False(device.IsActive);
        Assert.False(device.SecretMatches(rotated));
    }

    [Fact]
    public void Beitrittscode_gilt_180_Tage_mit_Limit_und_Widerruf_Rollencode_14_Tage_einmalig()
    {
        var join = JoinCode.Create(Tenant, null, Now, usageLimit: 2);
        Assert.Equal(Now.AddDays(180), join.ExpiresAt);
        Assert.True(join.IsRedeemableAt(Now.AddDays(180)));
        Assert.False(join.IsRedeemableAt(Now.AddDays(181)));
        join.Use();
        join.Use();
        Assert.False(join.IsRedeemableAt(Now));
        var open = JoinCode.Create(Tenant, null, Now);
        open.Revoke(Now);
        Assert.False(open.IsRedeemableAt(Now));

        var (role, code) = RoleCode.Issue(Tenant, "programme_manager", null, Now);
        Assert.Equal(Now.AddDays(14), role.ExpiresAt);
        Assert.True(AccessCodes.HashEquals(role.CodeHash, code));
        Assert.True(role.IsRedeemableAt(Now.AddDays(14)));
        role.Redeem(Now);
        Assert.False(role.IsRedeemableAt(Now));

        var (magic, _) = MagicLink.Issue(Tenant, PersonId.New(), Now);
        Assert.True(magic.IsUsableAt(Now.AddMinutes(15)));
        Assert.False(magic.IsUsableAt(Now.AddMinutes(16)));
        var (transfer, _) = TransferLink.Issue(Tenant, PersonId.New(), Now);
        Assert.True(transfer.IsUsableAt(Now.AddMinutes(5)));
        Assert.False(transfer.IsUsableAt(Now.AddMinutes(6)));
    }

    [Fact]
    public void Privilegierte_Rollen_erhalten_kurze_Sitzungen_und_keinen_Magic_Link()
    {
        Assert.Equal(SessionKind.Privileged, AccessRules.SessionKindFor(["member", "tenant_admin"]));
        Assert.Equal(SessionKind.Privileged, AccessRules.SessionKindFor(["programme_manager"]));
        Assert.Equal(SessionKind.Member, AccessRules.SessionKindFor(["member", "editor"]));
        Assert.False(AccessRules.MagicLinkAllowedFor(["tenant_admin"]));
        Assert.True(AccessRules.MagicLinkAllowedFor(["editor", "member"]));
        Assert.True(AccessRules.RequiresRealName("editor"));
        Assert.False(AccessRules.RequiresRealName("health_ambassador"));
        Assert.True(AccessRules.RequiresPhishingResistantWay("programme_manager"));
        Assert.False(AccessRules.RequiresPhishingResistantWay("editor"));
    }

    [Fact]
    public void Rollencode_setzt_den_Klarnamen_als_Anzeigenamen()
    {
        var person = Person.Create(Tenant, "Sonnenblume", Now);
        Assert.Null(person.RealName);
        person.AssumeRealName(" Dana Manager ");
        Assert.Equal("Dana Manager", person.RealName);
        Assert.Equal("Dana Manager", person.DisplayName);
        person.Leave(Now);
        Assert.Equal(PersonState.Left, person.State);
    }

    [Fact]
    public void Tenant_legt_Anmeldewege_fest_mindestens_ein_Weg_ausser_Kiosk_bleibt_und_deaktivierte_Wege_haben_30_Tage_Frist()
    {
        var policy = LoginPolicy.Default(Tenant, Now);
        Assert.True(policy.WayOpen(LoginWay.MagicLink, false, Now));
        Assert.True(policy.KioskJoinAllowed);
        Assert.Equal(60, policy.KioskIdleSeconds);

        Assert.Throws<LoginPolicyException>(() => policy.Update(false, false, true, ["microsoft", "google"], [], 60, availableProviderCount: 2, Now));
        Assert.Throws<LoginPolicyException>(() => policy.Update(true, true, true, [], [], 20, 2, Now));
        Assert.Throws<LoginPolicyException>(() => policy.Update(true, true, true, ["google"], ["google"], 60, 2, Now));

        policy.Update(false, true, true, [], [], 45, 2, Now);
        Assert.False(policy.WayOpen(LoginWay.MagicLink, personHasOnlyThisWay: false, Now.AddDays(1)));
        Assert.True(policy.WayOpen(LoginWay.MagicLink, personHasOnlyThisWay: true, Now.AddDays(29)));
        Assert.False(policy.WayOpen(LoginWay.MagicLink, personHasOnlyThisWay: true, Now.AddDays(30)));
        Assert.Equal(45, policy.KioskIdleSeconds);

        policy.Update(false, true, true, [], ["microsoft"], 45, 2, Now.AddDays(1));
        Assert.True(policy.ProviderForced);
        Assert.False(policy.KioskJoinAllowed, "erzwungener Anbieter: kein Beitritt am Kiosk");
        Assert.True(policy.WayOpen(LoginWay.Kiosk, false, Now.AddDays(1)), "Kiosk-Nutzung bleibt");
        Assert.True(policy.ProviderAllowed("microsoft"));
        Assert.False(policy.ProviderAllowed("google"));
        Assert.Equal(Now, policy.MagicLinkDisabledAt);
    }

    [Fact]
    public void Discovery_Dokument_eines_fehlerhaften_Anbieters_wird_abgelehnt()
    {
        var ok = new DiscoveryValidation.Document("https://idp.example", "https://idp.example/authorize", "https://idp.example/token", "https://idp.example/jwks", ["code"], ["S256"], ["openid"]);
        Assert.Empty(DiscoveryValidation.Problems("https://idp.example/", ok));

        var broken = ok with { Issuer = "https://evil.example", TokenEndpoint = "http://idp.example/token", CodeChallengeMethods = ["plain"] };
        var problems = DiscoveryValidation.Problems("https://idp.example", broken);
        Assert.Contains("issuer_mismatch", problems);
        Assert.Contains("token_endpoint_missing", problems);
        Assert.Contains("pkce_s256_unsupported", problems);
    }
}
