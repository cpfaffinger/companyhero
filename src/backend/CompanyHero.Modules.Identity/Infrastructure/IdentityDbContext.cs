using CompanyHero.Modules.Identity.Domain;
using CompanyHero.Platform.Data;
using CompanyHero.Platform.Modules;
using Microsoft.EntityFrameworkCore;

namespace CompanyHero.Modules.Identity.Infrastructure;

/// <summary>
/// Schema <c>identity</c> (Zugang 2 bis 8): Personen, Anmeldewege, Wiederherstellungscodes, plattformweiter Identitätsindex,
/// Sitzungen, Kiosk-Geräte und -Kennungen, Beitritts- und Rollencodes, Magic-Links, Übertragungen, tenant-eigene Anbieter,
/// Anmeldewege des Tenants. Tabellen, die die Plattforminfrastruktur vor der Tenant-Zuordnung liest (Sitzung, Index, Codes),
/// tragen eine Policy „eigener Tenant oder Plattformkontext“; alle übrigen sind streng je Tenant isoliert.
/// </summary>
public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : ModuleDbContext(options)
{
    public DbSet<Person> Persons => Set<Person>();

    public DbSet<Passkey> Passkeys => Set<Passkey>();

    public DbSet<EmailLogin> EmailLogins => Set<EmailLogin>();

    public DbSet<ExternalLogin> ExternalLogins => Set<ExternalLogin>();

    public DbSet<RecoveryCode> RecoveryCodes => Set<RecoveryCode>();

    public DbSet<IdentityIndexEntry> IdentityIndex => Set<IdentityIndexEntry>();

    public DbSet<Session> Sessions => Set<Session>();

    public DbSet<KioskDevice> KioskDevices => Set<KioskDevice>();

    public DbSet<KioskCredential> KioskCredentials => Set<KioskCredential>();

    public DbSet<KioskFailedAttempt> KioskFailedAttempts => Set<KioskFailedAttempt>();

    public DbSet<JoinCode> JoinCodes => Set<JoinCode>();

    public DbSet<RoleCode> RoleCodes => Set<RoleCode>();

    public DbSet<MagicLink> MagicLinks => Set<MagicLink>();

    public DbSet<TransferLink> TransferLinks => Set<TransferLink>();

    public DbSet<ExternalProvider> ExternalProviders => Set<ExternalProvider>();

    public DbSet<LoginPolicy> LoginPolicies => Set<LoginPolicy>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        IdentifierConventions.Apply(configurationBuilder);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(ModuleSchemas.Identity);

        modelBuilder.Entity<Person>(b =>
        {
            b.ToTable("person");
            b.HasKey(p => new { p.TenantId, p.Id });
            b.Property(p => p.TenantId).HasColumnName("tenant_id");
            b.Property(p => p.Id).HasColumnName("id");
            b.Property(p => p.DisplayName).HasColumnName("display_name").HasMaxLength(80).IsRequired();
            b.Property(p => p.RealName).HasColumnName("real_name").HasMaxLength(120);
            b.Property(p => p.State).HasColumnName("state").HasConversion<short>().HasDefaultValue(PersonState.Active);
            b.Property(p => p.CreatedAt).HasColumnName("created_at");
            b.Property(p => p.LeftAt).HasColumnName("left_at");
            // Anzeigenamen sind je Tenant eindeutig (Zugang 2.2), nie plattformweit.
            b.HasIndex(p => new { p.TenantId, p.DisplayName }).IsUnique();
        });

        modelBuilder.Entity<Passkey>(b =>
        {
            b.ToTable("passkey");
            b.HasKey(p => new { p.TenantId, p.Id });
            b.Property(p => p.TenantId).HasColumnName("tenant_id");
            b.Property(p => p.Id).HasColumnName("id");
            b.Property(p => p.PersonId).HasColumnName("person_id");
            b.Property(p => p.CredentialId).HasColumnName("credential_id").IsRequired();
            b.Property(p => p.PublicKey).HasColumnName("public_key").IsRequired();
            b.Property(p => p.SignCount).HasColumnName("sign_count").HasConversion<long>();
            b.Property(p => p.AaGuid).HasColumnName("aaguid");
            b.Property(p => p.DeviceName).HasColumnName("device_name").HasMaxLength(80).IsRequired();
            b.Property(p => p.CreatedAt).HasColumnName("created_at");
            b.Property(p => p.LastUsedAt).HasColumnName("last_used_at");
            // Credential-IDs sind plattformweit eindeutig; die Anmeldung findet den Passkey vor der Tenant-Zuordnung.
            b.HasIndex(p => p.CredentialId).IsUnique();
            b.HasIndex(p => new { p.TenantId, p.PersonId });
            b.HasOne<Person>().WithMany().HasForeignKey(p => new { p.TenantId, p.PersonId }).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<EmailLogin>(b =>
        {
            b.ToTable("email_login");
            b.HasKey(e => new { e.TenantId, e.PersonId });
            b.Property(e => e.TenantId).HasColumnName("tenant_id");
            b.Property(e => e.PersonId).HasColumnName("person_id");
            b.Property(e => e.Email).HasColumnName("email").HasMaxLength(254).IsRequired();
            b.Property(e => e.CreatedAt).HasColumnName("created_at");
            b.HasOne<Person>().WithMany().HasForeignKey(e => new { e.TenantId, e.PersonId }).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ExternalLogin>(b =>
        {
            b.ToTable("external_login");
            b.HasKey(e => new { e.TenantId, e.Id });
            b.Property(e => e.TenantId).HasColumnName("tenant_id");
            b.Property(e => e.Id).HasColumnName("id");
            b.Property(e => e.PersonId).HasColumnName("person_id");
            b.Property(e => e.ProviderKey).HasColumnName("provider_key").HasMaxLength(64).IsRequired();
            b.Property(e => e.SubjectHash).HasColumnName("subject_hash").HasMaxLength(100).IsRequired();
            b.Property(e => e.CreatedAt).HasColumnName("created_at");
            b.HasIndex(e => new { e.TenantId, e.PersonId });
            b.HasOne<Person>().WithMany().HasForeignKey(e => new { e.TenantId, e.PersonId }).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RecoveryCode>(b =>
        {
            b.ToTable("recovery_code");
            b.HasKey(r => new { r.TenantId, r.PersonId });
            b.Property(r => r.TenantId).HasColumnName("tenant_id");
            b.Property(r => r.PersonId).HasColumnName("person_id");
            b.Property(r => r.CodeHash).HasColumnName("code_hash").HasMaxLength(100).IsRequired();
            b.Property(r => r.CreatedAt).HasColumnName("created_at");
            // Die Anmeldung mit Wiederherstellungscode findet den Eintrag über den Hash, vor der Tenant-Zuordnung.
            b.HasIndex(r => r.CodeHash).IsUnique();
            b.HasOne<Person>().WithMany().HasForeignKey(r => new { r.TenantId, r.PersonId }).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<IdentityIndexEntry>(b =>
        {
            b.ToTable("identity_index");
            b.HasKey(i => i.Hash);
            b.Property(i => i.Hash).HasColumnName("hash").HasMaxLength(100);
            b.Property(i => i.Kind).HasColumnName("kind").HasConversion<short>();
            b.Property(i => i.TenantId).HasColumnName("tenant_id");
            b.Property(i => i.PersonId).HasColumnName("person_id");
            b.Property(i => i.CreatedAt).HasColumnName("created_at");
            b.HasIndex(i => new { i.TenantId, i.PersonId });
        });

        modelBuilder.Entity<Session>(b =>
        {
            b.ToTable("session");
            b.HasKey(s => s.Id);
            b.Property(s => s.Id).HasColumnName("id");
            b.Property(s => s.TenantId).HasColumnName("tenant_id");
            b.Property(s => s.TokenHash).HasColumnName("token_hash").HasMaxLength(100).IsRequired();
            b.Property(s => s.CsrfToken).HasColumnName("csrf_token").HasMaxLength(100).IsRequired();
            b.Property(s => s.PersonId).HasColumnName("person_id");
            b.Property(s => s.Kind).HasColumnName("kind").HasConversion<short>();
            b.Property(s => s.CreatedAt).HasColumnName("created_at");
            b.Property(s => s.AuthenticatedAt).HasColumnName("authenticated_at");
            b.Property(s => s.LastSeenAt).HasColumnName("last_seen_at");
            b.Property(s => s.SlidingUntil).HasColumnName("sliding_until");
            b.Property(s => s.AbsoluteUntil).HasColumnName("absolute_until");
            b.Property(s => s.RevokedAt).HasColumnName("revoked_at");
            b.Property(s => s.KioskDeviceId).HasColumnName("kiosk_device_id");
            b.HasIndex(s => s.TokenHash).IsUnique();
            b.HasIndex(s => new { s.TenantId, s.PersonId });
            b.HasIndex(s => new { s.TenantId, s.KioskDeviceId });
        });

        modelBuilder.Entity<KioskDevice>(b =>
        {
            b.ToTable("kiosk_device");
            b.HasKey(d => new { d.TenantId, d.Id });
            b.Property(d => d.TenantId).HasColumnName("tenant_id");
            b.Property(d => d.Id).HasColumnName("id");
            b.Property(d => d.Name).HasColumnName("name").HasMaxLength(80).IsRequired();
            b.Property(d => d.RegistrationCodeHash).HasColumnName("registration_code_hash").HasMaxLength(100);
            b.Property(d => d.RegistrationExpiresAt).HasColumnName("registration_expires_at");
            b.Property(d => d.SecretHash).HasColumnName("secret_hash").HasMaxLength(100);
            b.Property(d => d.SecretIssuedAt).HasColumnName("secret_issued_at");
            b.Property(d => d.CreatedAt).HasColumnName("created_at");
            b.Property(d => d.RegisteredAt).HasColumnName("registered_at");
            b.Property(d => d.RevokedAt).HasColumnName("revoked_at");
            b.Property(d => d.LastSeenAt).HasColumnName("last_seen_at");
            b.Property(d => d.LoginCount).HasColumnName("login_count");
            // Registrierungscode und Gerätegeheimnis werden vor der Tenant-Zuordnung über ihren Hash gefunden.
            b.HasIndex(d => d.RegistrationCodeHash).IsUnique();
            b.HasIndex(d => d.SecretHash).IsUnique();
        });

        modelBuilder.Entity<KioskCredential>(b =>
        {
            b.ToTable("kiosk_credential");
            b.HasKey(k => new { k.TenantId, k.PersonId });
            b.Property(k => k.TenantId).HasColumnName("tenant_id");
            b.Property(k => k.PersonId).HasColumnName("person_id");
            b.Property(k => k.KioskId).HasColumnName("kiosk_id").HasMaxLength(6).IsRequired();
            b.Property(k => k.PinHash).HasColumnName("pin_hash").HasMaxLength(100);
            b.Property(k => k.PinSetAt).HasColumnName("pin_set_at");
            // Kiosk-Kennung je Tenant eindeutig (Zugang 6.2).
            b.HasIndex(k => new { k.TenantId, k.KioskId }).IsUnique();
            b.HasOne<Person>().WithMany().HasForeignKey(k => new { k.TenantId, k.PersonId }).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<KioskFailedAttempt>(b =>
        {
            b.ToTable("kiosk_failed_attempt");
            b.HasKey(a => new { a.TenantId, a.Id });
            b.Property(a => a.TenantId).HasColumnName("tenant_id");
            b.Property(a => a.Id).HasColumnName("id");
            b.Property(a => a.DeviceId).HasColumnName("device_id");
            b.Property(a => a.KioskIdHash).HasColumnName("kiosk_id_hash").HasMaxLength(100).IsRequired();
            b.Property(a => a.AttemptedAt).HasColumnName("attempted_at");
            b.HasIndex(a => new { a.TenantId, a.DeviceId, a.AttemptedAt });
        });

        modelBuilder.Entity<JoinCode>(b =>
        {
            b.ToTable("join_code");
            b.HasKey(j => new { j.TenantId, j.Id });
            b.Property(j => j.TenantId).HasColumnName("tenant_id");
            b.Property(j => j.Id).HasColumnName("id");
            b.Property(j => j.Code).HasColumnName("code").HasMaxLength(8).IsRequired();
            b.Property(j => j.CreatedBy).HasColumnName("created_by");
            b.Property(j => j.CreatedAt).HasColumnName("created_at");
            b.Property(j => j.ExpiresAt).HasColumnName("expires_at");
            b.Property(j => j.UsageLimit).HasColumnName("usage_limit");
            b.Property(j => j.UsedCount).HasColumnName("used_count");
            b.Property(j => j.RevokedAt).HasColumnName("revoked_at");
            // Der Code bestimmt den Tenant serverseitig (Zugang 2.1): plattformweit eindeutig.
            b.HasIndex(j => j.Code).IsUnique();
        });

        modelBuilder.Entity<RoleCode>(b =>
        {
            b.ToTable("role_code");
            b.HasKey(r => new { r.TenantId, r.Id });
            b.Property(r => r.TenantId).HasColumnName("tenant_id");
            b.Property(r => r.Id).HasColumnName("id");
            b.Property(r => r.CodeHash).HasColumnName("code_hash").HasMaxLength(100).IsRequired();
            b.Property(r => r.Role).HasColumnName("role").HasMaxLength(40).IsRequired();
            b.Property(r => r.IssuedBy).HasColumnName("issued_by");
            b.Property(r => r.IssuedAt).HasColumnName("issued_at");
            b.Property(r => r.ExpiresAt).HasColumnName("expires_at");
            b.Property(r => r.RedeemedAt).HasColumnName("redeemed_at");
            b.Property(r => r.RevokedAt).HasColumnName("revoked_at");
            b.HasIndex(r => r.CodeHash).IsUnique();
        });

        modelBuilder.Entity<MagicLink>(b =>
        {
            b.ToTable("magic_link");
            b.HasKey(m => new { m.TenantId, m.Id });
            b.Property(m => m.TenantId).HasColumnName("tenant_id");
            b.Property(m => m.Id).HasColumnName("id");
            b.Property(m => m.TokenHash).HasColumnName("token_hash").HasMaxLength(100).IsRequired();
            b.Property(m => m.PersonId).HasColumnName("person_id");
            b.Property(m => m.CreatedAt).HasColumnName("created_at");
            b.Property(m => m.ExpiresAt).HasColumnName("expires_at");
            b.Property(m => m.UsedAt).HasColumnName("used_at");
            b.HasIndex(m => m.TokenHash).IsUnique();
        });

        modelBuilder.Entity<TransferLink>(b =>
        {
            b.ToTable("transfer_link");
            b.HasKey(t => new { t.TenantId, t.Id });
            b.Property(t => t.TenantId).HasColumnName("tenant_id");
            b.Property(t => t.Id).HasColumnName("id");
            b.Property(t => t.TokenHash).HasColumnName("token_hash").HasMaxLength(100).IsRequired();
            b.Property(t => t.PersonId).HasColumnName("person_id");
            b.Property(t => t.CreatedAt).HasColumnName("created_at");
            b.Property(t => t.ExpiresAt).HasColumnName("expires_at");
            b.Property(t => t.UsedAt).HasColumnName("used_at");
            b.HasIndex(t => t.TokenHash).IsUnique();
        });

        modelBuilder.Entity<ExternalProvider>(b =>
        {
            b.ToTable("external_provider");
            b.HasKey(p => new { p.TenantId, p.Id });
            b.Property(p => p.TenantId).HasColumnName("tenant_id");
            b.Property(p => p.Id).HasColumnName("id");
            b.Ignore(p => p.Key);
            b.Property(p => p.DisplayName).HasColumnName("display_name").HasMaxLength(80).IsRequired();
            b.Property(p => p.Issuer).HasColumnName("issuer").HasMaxLength(400).IsRequired();
            b.Property(p => p.ClientId).HasColumnName("client_id").HasMaxLength(200).IsRequired();
            b.Property(p => p.ClientSecretProtected).HasColumnName("client_secret_protected").IsRequired();
            b.Property(p => p.CreatedAt).HasColumnName("created_at");
            b.Property(p => p.UpdatedAt).HasColumnName("updated_at");
            b.Property(p => p.ValidatedAt).HasColumnName("validated_at");
            b.Property(p => p.DisabledAt).HasColumnName("disabled_at");
            b.HasIndex(p => p.Id).IsUnique();
        });

        modelBuilder.Entity<LoginPolicy>(b =>
        {
            b.ToTable("login_policy");
            b.HasKey(p => p.TenantId);
            b.Property(p => p.TenantId).HasColumnName("tenant_id");
            b.Property(p => p.MagicLinkEnabled).HasColumnName("magic_link_enabled");
            b.Property(p => p.MagicLinkDisabledAt).HasColumnName("magic_link_disabled_at");
            b.Property(p => p.PasskeyEnabled).HasColumnName("passkey_enabled");
            b.Property(p => p.PasskeyDisabledAt).HasColumnName("passkey_disabled_at");
            b.Property(p => p.KioskEnabled).HasColumnName("kiosk_enabled");
            b.Property(p => p.KioskDisabledAt).HasColumnName("kiosk_disabled_at");
            b.PrimitiveCollection<IReadOnlyList<string>>("DisabledProviderKeys").HasColumnName("disabled_provider_keys").HasField("_disabledProviderKeys");
            b.PrimitiveCollection<IReadOnlyList<string>>("ForcedProviderKeys").HasColumnName("forced_provider_keys").HasField("_forcedProviderKeys");
            b.Property(p => p.KioskIdleSeconds).HasColumnName("kiosk_idle_seconds");
            b.Property(p => p.UpdatedAt).HasColumnName("updated_at");
        });
    }
}
