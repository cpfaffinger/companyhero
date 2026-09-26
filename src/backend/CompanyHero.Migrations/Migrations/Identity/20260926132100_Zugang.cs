using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CompanyHero.Migrations.Migrations.Identity
{
    /// <inheritdoc />
    public partial class Zugang : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "left_at",
                schema: "identity",
                table: "person",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "real_name",
                schema: "identity",
                table: "person",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "state",
                schema: "identity",
                table: "person",
                type: "smallint",
                nullable: false,
                defaultValue: (short)1);

            migrationBuilder.CreateTable(
                name: "email_login",
                schema: "identity",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_login", x => new { x.tenant_id, x.person_id });
                    table.ForeignKey(
                        name: "FK_email_login_person_tenant_id_person_id",
                        columns: x => new { x.tenant_id, x.person_id },
                        principalSchema: "identity",
                        principalTable: "person",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "external_login",
                schema: "identity",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    subject_hash = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_external_login", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "FK_external_login_person_tenant_id_person_id",
                        columns: x => new { x.tenant_id, x.person_id },
                        principalSchema: "identity",
                        principalTable: "person",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "external_provider",
                schema: "identity",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    issuer = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    client_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    client_secret_protected = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    validated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    disabled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_external_provider", x => new { x.tenant_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "identity_index",
                schema: "identity",
                columns: table => new
                {
                    hash = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    kind = table.Column<short>(type: "smallint", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_identity_index", x => x.hash);
                });

            migrationBuilder.CreateTable(
                name: "join_code",
                schema: "identity",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    usage_limit = table.Column<int>(type: "integer", nullable: true),
                    used_count = table.Column<int>(type: "integer", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_join_code", x => new { x.tenant_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "kiosk_credential",
                schema: "identity",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kiosk_id = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    pin_hash = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    pin_set_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_kiosk_credential", x => new { x.tenant_id, x.person_id });
                    table.ForeignKey(
                        name: "FK_kiosk_credential_person_tenant_id_person_id",
                        columns: x => new { x.tenant_id, x.person_id },
                        principalSchema: "identity",
                        principalTable: "person",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "kiosk_device",
                schema: "identity",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    registration_code_hash = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    registration_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    secret_hash = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    secret_issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    registered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    login_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_kiosk_device", x => new { x.tenant_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "kiosk_failed_attempt",
                schema: "identity",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kiosk_id_hash = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    attempted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_kiosk_failed_attempt", x => new { x.tenant_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "login_policy",
                schema: "identity",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    magic_link_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    magic_link_disabled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    passkey_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    passkey_disabled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    kiosk_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    kiosk_disabled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    disabled_provider_keys = table.Column<string[]>(type: "text[]", nullable: false),
                    forced_provider_keys = table.Column<string[]>(type: "text[]", nullable: false),
                    kiosk_idle_seconds = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_login_policy", x => x.tenant_id);
                });

            migrationBuilder.CreateTable(
                name: "magic_link",
                schema: "identity",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_magic_link", x => new { x.tenant_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "passkey",
                schema: "identity",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    credential_id = table.Column<byte[]>(type: "bytea", nullable: false),
                    public_key = table.Column<byte[]>(type: "bytea", nullable: false),
                    sign_count = table.Column<long>(type: "bigint", nullable: false),
                    aaguid = table.Column<Guid>(type: "uuid", nullable: false),
                    device_name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_passkey", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "FK_passkey_person_tenant_id_person_id",
                        columns: x => new { x.tenant_id, x.person_id },
                        principalSchema: "identity",
                        principalTable: "person",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "recovery_code",
                schema: "identity",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code_hash = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recovery_code", x => new { x.tenant_id, x.person_id });
                    table.ForeignKey(
                        name: "FK_recovery_code_person_tenant_id_person_id",
                        columns: x => new { x.tenant_id, x.person_id },
                        principalSchema: "identity",
                        principalTable: "person",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "role_code",
                schema: "identity",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code_hash = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    role = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    issued_by = table.Column<Guid>(type: "uuid", nullable: true),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    redeemed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_role_code", x => new { x.tenant_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "session",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    csrf_token = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: true),
                    kind = table.Column<short>(type: "smallint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    authenticated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    sliding_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    absolute_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    kiosk_device_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_session", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "transfer_link",
                schema: "identity",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transfer_link", x => new { x.tenant_id, x.id });
                });

            migrationBuilder.CreateIndex(
                name: "IX_external_login_tenant_id_person_id",
                schema: "identity",
                table: "external_login",
                columns: new[] { "tenant_id", "person_id" });

            migrationBuilder.CreateIndex(
                name: "IX_external_provider_id",
                schema: "identity",
                table: "external_provider",
                column: "id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_identity_index_tenant_id_person_id",
                schema: "identity",
                table: "identity_index",
                columns: new[] { "tenant_id", "person_id" });

            migrationBuilder.CreateIndex(
                name: "IX_join_code_code",
                schema: "identity",
                table: "join_code",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_kiosk_credential_tenant_id_kiosk_id",
                schema: "identity",
                table: "kiosk_credential",
                columns: new[] { "tenant_id", "kiosk_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_kiosk_device_registration_code_hash",
                schema: "identity",
                table: "kiosk_device",
                column: "registration_code_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_kiosk_device_secret_hash",
                schema: "identity",
                table: "kiosk_device",
                column: "secret_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_kiosk_failed_attempt_tenant_id_device_id_attempted_at",
                schema: "identity",
                table: "kiosk_failed_attempt",
                columns: new[] { "tenant_id", "device_id", "attempted_at" });

            migrationBuilder.CreateIndex(
                name: "IX_magic_link_token_hash",
                schema: "identity",
                table: "magic_link",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_passkey_credential_id",
                schema: "identity",
                table: "passkey",
                column: "credential_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_passkey_tenant_id_person_id",
                schema: "identity",
                table: "passkey",
                columns: new[] { "tenant_id", "person_id" });

            migrationBuilder.CreateIndex(
                name: "IX_recovery_code_code_hash",
                schema: "identity",
                table: "recovery_code",
                column: "code_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_role_code_code_hash",
                schema: "identity",
                table: "role_code",
                column: "code_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_session_tenant_id_kiosk_device_id",
                schema: "identity",
                table: "session",
                columns: new[] { "tenant_id", "kiosk_device_id" });

            migrationBuilder.CreateIndex(
                name: "IX_session_tenant_id_person_id",
                schema: "identity",
                table: "session",
                columns: new[] { "tenant_id", "person_id" });

            migrationBuilder.CreateIndex(
                name: "IX_session_token_hash",
                schema: "identity",
                table: "session",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_transfer_link_token_hash",
                schema: "identity",
                table: "transfer_link",
                column: "token_hash",
                unique: true);

            // Row Level Security (A-011, Backend 5.1 Nr. 4)
            RowLevelSecurity.IsolateByTenantOrPlatform(migrationBuilder, "identity", "session");
            RowLevelSecurity.IsolateByTenantOrPlatform(migrationBuilder, "identity", "identity_index");
            RowLevelSecurity.IsolateByTenantOrPlatform(migrationBuilder, "identity", "join_code");
            RowLevelSecurity.IsolateByTenantOrPlatform(migrationBuilder, "identity", "role_code");
            RowLevelSecurity.IsolateByTenantOrPlatform(migrationBuilder, "identity", "magic_link");
            RowLevelSecurity.IsolateByTenantOrPlatform(migrationBuilder, "identity", "transfer_link");
            RowLevelSecurity.IsolateByTenantOrPlatform(migrationBuilder, "identity", "external_provider");
            RowLevelSecurity.IsolateByTenantOrPlatform(migrationBuilder, "identity", "kiosk_device");
            RowLevelSecurity.IsolateByTenantOrPlatform(migrationBuilder, "identity", "recovery_code");
            RowLevelSecurity.IsolateByTenantOrPlatform(migrationBuilder, "identity", "passkey");
            RowLevelSecurity.IsolateByTenantOrPlatform(migrationBuilder, "identity", "login_policy");
            RowLevelSecurity.IsolateByTenant(migrationBuilder, "identity", "email_login");
            RowLevelSecurity.IsolateByTenant(migrationBuilder, "identity", "external_login");
            RowLevelSecurity.IsolateByTenant(migrationBuilder, "identity", "kiosk_credential");
            RowLevelSecurity.IsolateByTenant(migrationBuilder, "identity", "kiosk_failed_attempt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "email_login",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "external_login",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "external_provider",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "identity_index",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "join_code",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "kiosk_credential",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "kiosk_device",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "kiosk_failed_attempt",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "login_policy",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "magic_link",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "passkey",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "recovery_code",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "role_code",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "session",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "transfer_link",
                schema: "identity");

            migrationBuilder.DropColumn(
                name: "left_at",
                schema: "identity",
                table: "person");

            migrationBuilder.DropColumn(
                name: "real_name",
                schema: "identity",
                table: "person");

            migrationBuilder.DropColumn(
                name: "state",
                schema: "identity",
                table: "person");
        }
    }
}
