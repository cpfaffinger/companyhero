using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CompanyHero.Migrations.Migrations.Privacy
{
    /// <inheritdoc />
    public partial class Protokolle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_entry",
                schema: "privacy",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actor_person_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_roles = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    subject_ref = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    detail = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_entry", x => new { x.tenant_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "security_event",
                schema: "privacy",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    event_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    success = table.Column<bool>(type: "boolean", nullable: false),
                    pseudonym = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    detail = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_security_event", x => new { x.tenant_id, x.id });
                });

            migrationBuilder.CreateIndex(
                name: "IX_audit_entry_tenant_id_occurred_at",
                schema: "privacy",
                table: "audit_entry",
                columns: new[] { "tenant_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "IX_security_event_tenant_id_occurred_at",
                schema: "privacy",
                table: "security_event",
                columns: new[] { "tenant_id", "occurred_at" });

            // Row Level Security (A-011, Backend 5.1 Nr. 4)
            RowLevelSecurity.IsolateByTenant(migrationBuilder, "privacy", "audit_entry");
            RowLevelSecurity.IsolateByTenant(migrationBuilder, "privacy", "security_event");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_entry",
                schema: "privacy");

            migrationBuilder.DropTable(
                name: "security_event",
                schema: "privacy");
        }
    }
}
