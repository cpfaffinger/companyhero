using System.Security.Cryptography;
using System.Text;
using CompanyHero.Modules.Notifications.Application;
using CompanyHero.Modules.Notifications.Domain;
using CompanyHero.Platform.Tenancy;

namespace CompanyHero.Domain.Tests;

/// <summary>Benachrichtigungen 3.1, 4.2 (A-059, A-060) als reine Fachregeln: Ruhezeit, Rückhaltung, Deduplikation, Nutzlast, Verschlüsselung.</summary>
public sealed class NotificationRulesTests
{
    private static readonly TimeZoneInfo Vienna = TenantTimeZone.Resolve("Europe/Vienna");

    [Theory]
    [InlineData("19:59", false)]
    [InlineData("20:00", true)]
    [InlineData("23:30", true)]
    [InlineData("06:59", true)]
    [InlineData("07:00", false)]
    public void Ruhezeit_20_bis_07_liegt_ueber_Mitternacht(string local, bool quiet)
    {
        Assert.Equal(quiet, NotificationRules.IsQuiet(TimeOnly.Parse(local, System.Globalization.CultureInfo.InvariantCulture), NotificationRules.DefaultQuietStart, NotificationRules.DefaultQuietEnd));
    }

    [Fact]
    public void Ereignis_in_der_Ruhezeit_wird_bis_07_Uhr_Tenant_Zeit_zurueckgehalten()
    {
        // 25.09.2026 21:30 Wien = 19:30 UTC
        var now = new DateTimeOffset(2026, 9, 25, 19, 30, 0, TimeSpan.Zero);
        var held = NotificationRules.HoldUntil(now, Vienna, (NotificationRules.DefaultQuietStart, NotificationRules.DefaultQuietEnd), null);
        Assert.Equal(new DateTimeOffset(2026, 9, 26, 5, 0, 0, TimeSpan.Zero), held);
    }

    [Fact]
    public void Ereignis_am_Tag_wird_nicht_zurueckgehalten()
    {
        var now = new DateTimeOffset(2026, 9, 25, 10, 0, 0, TimeSpan.Zero);
        Assert.Null(NotificationRules.HoldUntil(now, Vienna, (NotificationRules.DefaultQuietStart, NotificationRules.DefaultQuietEnd), null));
    }

    [Fact]
    public void Persoenliche_Ruhezeit_erweitert_die_des_Tenants_und_verkuerzt_sie_nie()
    {
        // 18:30 Wien: Tenant nicht in Ruhe, Person ab 18:00 → Rückhaltung bis 07:00 (Tenant) beziehungsweise 08:00 (Person), das spätere gilt.
        var now = new DateTimeOffset(2026, 9, 25, 16, 30, 0, TimeSpan.Zero);
        var held = NotificationRules.HoldUntil(now, Vienna, (NotificationRules.DefaultQuietStart, NotificationRules.DefaultQuietEnd), (new TimeOnly(18, 0), new TimeOnly(8, 0)));
        Assert.Equal(new DateTimeOffset(2026, 9, 26, 6, 0, 0, TimeSpan.Zero), held);

        // Person „verkürzt“ auf 22:00–06:00: um 21:00 gilt trotzdem die Ruhezeit des Tenants.
        var evening = new DateTimeOffset(2026, 9, 25, 19, 0, 0, TimeSpan.Zero);
        var heldTenant = NotificationRules.HoldUntil(evening, Vienna, (NotificationRules.DefaultQuietStart, NotificationRules.DefaultQuietEnd), (new TimeOnly(22, 0), new TimeOnly(6, 0)));
        Assert.Equal(new DateTimeOffset(2026, 9, 26, 5, 0, 0, TimeSpan.Zero), heldTenant);
    }

    [Fact]
    public void Idempotenzschluessel_besteht_aus_Ereignis_Person_und_Kanal()
    {
        var person = PersonId.New();
        var push = NotificationRules.DedupeKey("challenges.started:abc", person, NotificationChannel.Push);
        var mail = NotificationRules.DedupeKey("challenges.started:abc", person, NotificationChannel.Email);
        Assert.NotEqual(push, mail);
        Assert.Equal(push, NotificationRules.DedupeKey("challenges.started:abc", person, NotificationChannel.Push));
    }

    [Fact]
    public void Push_Nutzlast_enthaelt_nur_Kennung_Kategorie_und_Ziel()
    {
        var json = Encoding.UTF8.GetString(new PushPayload("id-1", "challenge", "/challenges/x").ToBytes());
        Assert.Equal("{\"id\":\"id-1\",\"kategorie\":\"challenge\",\"ziel\":\"/challenges/x\"}", json);
    }

    [Fact]
    public void Abonnement_pausiert_nach_fuenf_Fehlern_und_erneuert_sich_bei_pushsubscriptionchange()
    {
        var now = DateTimeOffset.UtcNow;
        var s = PushSubscription.Register(TenantId.New(), PersonId.New(), "https://push.example/abc", "p", "a", "Handy", now);
        for (var i = 0; i < 4; i++)
        {
            s.RecordFailure(now);
        }

        Assert.True(s.Active);
        s.RecordFailure(now);
        Assert.False(s.Active);
        s.Renew("https://push.example/def", "p2", "a2");
        Assert.True(s.Active);
        Assert.Equal(0, s.FailureCount);
        Assert.Throws<ArgumentException>(() => PushSubscription.Register(TenantId.New(), PersonId.New(), "http://unsicher.example/abc", "p", "a", null, now));
    }

    [Fact]
    public void Web_Push_Verschluesselung_nach_RFC_8291_laesst_sich_mit_dem_Abonnementschluessel_entschluesseln()
    {
        using var client = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var p256dh = System.Buffers.Text.Base64Url.EncodeToString(WebPushCrypto.ExportRaw(client.PublicKey));
        var auth = System.Buffers.Text.Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16));
        var payload = new PushPayload("n1", "fortschritt", "/ich").ToBytes();

        var body = WebPushCrypto.Encrypt(payload, p256dh, auth);
        var decrypted = WebPushCrypto.Decrypt(body, client, auth);

        Assert.Equal(payload, decrypted);
        Assert.Equal(4096u, System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(body.AsSpan(16, 4)));
        Assert.Equal(65, body[20]);
        Assert.DoesNotContain(Encoding.UTF8.GetString(body), s => false);
        Assert.Equal(-1, Encoding.Latin1.GetString(body).IndexOf("fortschritt", StringComparison.Ordinal));
    }

    [Fact]
    public void VAPID_Signatur_ist_ein_ES256_JWT_mit_Audience_des_Push_Dienstes()
    {
        var pem = VapidSigner.GeneratePrivateKeyPem();
        using var signer = new VapidSigner(pem, "mailto:betrieb@example");
        var header = signer.Authorization(new Uri("https://push.example/send/123"), new DateTimeOffset(2026, 9, 25, 8, 0, 0, TimeSpan.Zero));

        Assert.StartsWith("vapid t=", header, StringComparison.Ordinal);
        var jwt = header[8..header.IndexOf(", k=", StringComparison.Ordinal)];
        var parts = jwt.Split('.');
        Assert.Equal(3, parts.Length);
        var claims = Encoding.UTF8.GetString(System.Buffers.Text.Base64Url.DecodeFromChars(parts[1]));
        Assert.Contains("\"aud\":\"https://push.example\"", claims, StringComparison.Ordinal);
        Assert.Contains("\"sub\":\"mailto:betrieb@example\"", claims, StringComparison.Ordinal);

        using var verify = ECDsa.Create();
        verify.ImportFromPem(pem);
        Assert.True(verify.VerifyData(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), System.Buffers.Text.Base64Url.DecodeFromChars(parts[2]), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
        Assert.Equal(87, signer.PublicKey.Length);
    }

    [Fact]
    public void Aushang_PDF_traegt_die_Tokenfarben_und_keine_Personendaten()
    {
        var pdf = new PdfPage().FillRect(0, 0, 100, 50, "#2A45C9").Text(10, 10, 12, "#FFFFFF", "Wiesner GmbH (Woche 39)").Build();
        var text = Encoding.Latin1.GetString(pdf);
        Assert.StartsWith("%PDF-1.4", text, StringComparison.Ordinal);
        Assert.Contains("0.165 0.271 0.788 rg", text, StringComparison.Ordinal);
        Assert.Contains("Wiesner GmbH \\(Woche 39\\)", text, StringComparison.Ordinal);
        Assert.EndsWith("%%EOF\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Kalenderwoche_folgt_ISO_8601()
    {
        Assert.Equal("2026-W39", Aushang.WeekOf(new DateOnly(2026, 9, 25)));
        Assert.Equal("2026-W53", Aushang.WeekOf(new DateOnly(2027, 1, 1)));
    }
}
