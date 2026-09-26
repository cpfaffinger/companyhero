using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CompanyHero.Migrations.Migrations.Notifications
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "notifications");

            migrationBuilder.CreateTable(
                name: "aushang",
                schema: "notifications",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    week = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    generated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    pdf = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_aushang", x => new { x.tenant_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "challenge_snapshot",
                schema: "notifications",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    milestone = table.Column<int>(type: "integer", nullable: false),
                    ended = table.Column<bool>(type: "boolean", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_challenge_snapshot", x => new { x.tenant_id, x.challenge_id });
                });

            migrationBuilder.CreateTable(
                name: "delivery",
                schema: "notifications",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel = table.Column<short>(type: "smallint", nullable: false),
                    dedupe_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    held_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    error = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_delivery", x => new { x.tenant_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "notification_entry",
                schema: "notifications",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category = table.Column<short>(type: "smallint", nullable: false),
                    event_key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    text_key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    @params = table.Column<string>(name: "params", type: "jsonb", nullable: false),
                    target = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_entry", x => new { x.tenant_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "person_settings",
                schema: "notifications",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    push_challenge = table.Column<bool>(type: "boolean", nullable: false),
                    push_progress = table.Column<bool>(type: "boolean", nullable: false),
                    email_challenge = table.Column<bool>(type: "boolean", nullable: false),
                    email_progress = table.Column<bool>(type: "boolean", nullable: false),
                    quiet_start = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    quiet_end = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    email_failure_count = table.Column<int>(type: "integer", nullable: false),
                    email_paused_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_person_settings", x => new { x.tenant_id, x.person_id });
                });

            migrationBuilder.CreateTable(
                name: "push_subscription",
                schema: "notifications",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    endpoint = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    p256dh = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    auth = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    device_label = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_success_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failure_count = table.Column<int>(type: "integer", nullable: false),
                    paused_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_push_subscription", x => new { x.tenant_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "tenant_settings",
                schema: "notifications",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    push_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    email_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    aushang_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    quiet_start = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    quiet_end = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenant_settings", x => x.tenant_id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_aushang_tenant_id_generated_at",
                schema: "notifications",
                table: "aushang",
                columns: new[] { "tenant_id", "generated_at" });

            migrationBuilder.CreateIndex(
                name: "IX_delivery_tenant_id_dedupe_key",
                schema: "notifications",
                table: "delivery",
                columns: new[] { "tenant_id", "dedupe_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_delivery_tenant_id_person_id_channel_day",
                schema: "notifications",
                table: "delivery",
                columns: new[] { "tenant_id", "person_id", "channel", "day" });

            migrationBuilder.CreateIndex(
                name: "IX_notification_entry_tenant_id_person_id_event_key",
                schema: "notifications",
                table: "notification_entry",
                columns: new[] { "tenant_id", "person_id", "event_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_notification_entry_tenant_id_person_id_occurred_at",
                schema: "notifications",
                table: "notification_entry",
                columns: new[] { "tenant_id", "person_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "IX_push_subscription_tenant_id_person_id",
                schema: "notifications",
                table: "push_subscription",
                columns: new[] { "tenant_id", "person_id" });

            // Row Level Security (A-011, Backend 5.1 Nr. 4)
            RowLevelSecurity.IsolateByTenant(migrationBuilder, "notifications", "aushang");
            RowLevelSecurity.IsolateByTenant(migrationBuilder, "notifications", "challenge_snapshot");
            RowLevelSecurity.IsolateByTenant(migrationBuilder, "notifications", "delivery");
            RowLevelSecurity.IsolateByTenant(migrationBuilder, "notifications", "notification_entry");
            RowLevelSecurity.IsolateByTenant(migrationBuilder, "notifications", "person_settings");
            RowLevelSecurity.IsolateByTenant(migrationBuilder, "notifications", "push_subscription");
            RowLevelSecurity.IsolateByTenant(migrationBuilder, "notifications", "tenant_settings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "aushang",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "challenge_snapshot",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "delivery",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "notification_entry",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "person_settings",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "push_subscription",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "tenant_settings",
                schema: "notifications");
        }
    }
}
