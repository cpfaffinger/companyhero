using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CompanyHero.Migrations.Migrations.Progress
{
    /// <inheritdoc />
    public partial class Fachpfad : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "day",
                schema: "progress",
                table: "activity_event",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<int>(
                name: "points",
                schema: "progress",
                table: "activity_event",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "reversal_of",
                schema: "progress",
                table: "activity_event",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "reversed",
                schema: "progress",
                table: "activity_event",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "badge_award",
                schema: "progress",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    badge_key = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    awarded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_badge_award", x => new { x.tenant_id, x.person_id, x.badge_key });
                });

            migrationBuilder.CreateTable(
                name: "check_in",
                schema: "progress",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    tiles = table.Column<short>(type: "smallint", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    activity_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_check_in", x => new { x.tenant_id, x.person_id, x.day });
                });

            migrationBuilder.CreateTable(
                name: "domain_event",
                schema: "progress",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    caused_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_domain_event", x => new { x.tenant_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "person_progress",
                schema: "progress",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    points_balance = table.Column<int>(type: "integer", nullable: false),
                    level = table.Column<int>(type: "integer", nullable: false),
                    current_streak = table.Column<int>(type: "integer", nullable: false),
                    longest_streak = table.Column<int>(type: "integer", nullable: false),
                    last_active_day = table.Column<DateOnly>(type: "date", nullable: true),
                    protection_used_in = table.Column<DateOnly>(type: "date", nullable: true),
                    daily_goal = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_person_progress", x => new { x.tenant_id, x.person_id });
                });

            migrationBuilder.CreateIndex(
                name: "IX_activity_event_tenant_id_person_id_day",
                schema: "progress",
                table: "activity_event",
                columns: new[] { "tenant_id", "person_id", "day" });

            migrationBuilder.CreateIndex(
                name: "IX_badge_award_tenant_id_awarded_at",
                schema: "progress",
                table: "badge_award",
                columns: new[] { "tenant_id", "awarded_at" });

            migrationBuilder.CreateIndex(
                name: "IX_domain_event_tenant_id_type_occurred_at",
                schema: "progress",
                table: "domain_event",
                columns: new[] { "tenant_id", "type", "occurred_at" });

            // Row Level Security (A-011, Backend 5.1 Nr. 4)
            RowLevelSecurity.IsolateByTenant(migrationBuilder, "progress", "badge_award");
            RowLevelSecurity.IsolateByTenant(migrationBuilder, "progress", "check_in");
            RowLevelSecurity.IsolateByTenant(migrationBuilder, "progress", "domain_event");
            RowLevelSecurity.IsolateByTenant(migrationBuilder, "progress", "person_progress");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "badge_award",
                schema: "progress");

            migrationBuilder.DropTable(
                name: "check_in",
                schema: "progress");

            migrationBuilder.DropTable(
                name: "domain_event",
                schema: "progress");

            migrationBuilder.DropTable(
                name: "person_progress",
                schema: "progress");

            migrationBuilder.DropIndex(
                name: "IX_activity_event_tenant_id_person_id_day",
                schema: "progress",
                table: "activity_event");

            migrationBuilder.DropColumn(
                name: "day",
                schema: "progress",
                table: "activity_event");

            migrationBuilder.DropColumn(
                name: "points",
                schema: "progress",
                table: "activity_event");

            migrationBuilder.DropColumn(
                name: "reversal_of",
                schema: "progress",
                table: "activity_event");

            migrationBuilder.DropColumn(
                name: "reversed",
                schema: "progress",
                table: "activity_event");
        }
    }
}
