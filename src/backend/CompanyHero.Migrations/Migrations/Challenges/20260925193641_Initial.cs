using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CompanyHero.Migrations.Migrations.Challenges
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "challenges");

            migrationBuilder.CreateTable(
                name: "challenge",
                schema: "challenges",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    metric = table.Column<short>(type: "smallint", nullable: false),
                    state = table.Column<short>(type: "smallint", nullable: false),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_challenge", x => new { x.tenant_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "contribution_key",
                schema: "challenges",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    regime = table.Column<short>(type: "smallint", nullable: false),
                    state = table.Column<short>(type: "smallint", nullable: false),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    contribution_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reserved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    committed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contribution_key", x => new { x.tenant_id, x.person_id, x.key });
                });

            migrationBuilder.CreateTable(
                name: "domain_event",
                schema: "challenges",
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
                name: "collective_state",
                schema: "challenges",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    total = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    contribution_count = table.Column<int>(type: "integer", nullable: false),
                    contributor_count = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collective_state", x => new { x.tenant_id, x.challenge_id });
                    table.ForeignKey(
                        name: "FK_collective_state_challenge_tenant_id_challenge_id",
                        columns: x => new { x.tenant_id, x.challenge_id },
                        principalSchema: "challenges",
                        principalTable: "challenge",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "contribution",
                schema: "challenges",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    value = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    channel = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contribution", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "FK_contribution_challenge_tenant_id_challenge_id",
                        columns: x => new { x.tenant_id, x.challenge_id },
                        principalSchema: "challenges",
                        principalTable: "challenge",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_contribution_tenant_id_challenge_id_person_id_recorded_at",
                schema: "challenges",
                table: "contribution",
                columns: new[] { "tenant_id", "challenge_id", "person_id", "recorded_at" });

            migrationBuilder.CreateIndex(
                name: "IX_domain_event_tenant_id_type_occurred_at",
                schema: "challenges",
                table: "domain_event",
                columns: new[] { "tenant_id", "type", "occurred_at" });

            // Row Level Security (A-011, Backend 5.1 Nr. 4)
            RowLevelSecurity.IsolateByTenant(migrationBuilder, "challenges", "challenge");
            RowLevelSecurity.IsolateByTenant(migrationBuilder, "challenges", "contribution");
            RowLevelSecurity.IsolateByTenant(migrationBuilder, "challenges", "contribution_key");
            RowLevelSecurity.IsolateByTenant(migrationBuilder, "challenges", "domain_event");
            RowLevelSecurity.IsolateByTenant(migrationBuilder, "challenges", "collective_state");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "collective_state",
                schema: "challenges");

            migrationBuilder.DropTable(
                name: "contribution",
                schema: "challenges");

            migrationBuilder.DropTable(
                name: "contribution_key",
                schema: "challenges");

            migrationBuilder.DropTable(
                name: "domain_event",
                schema: "challenges");

            migrationBuilder.DropTable(
                name: "challenge",
                schema: "challenges");
        }
    }
}
