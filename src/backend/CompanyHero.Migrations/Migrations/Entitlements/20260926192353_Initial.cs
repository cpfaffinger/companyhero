using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CompanyHero.Migrations.Migrations.Entitlements
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "entitlements");

            migrationBuilder.CreateTable(
                name: "entitlement",
                schema: "entitlements",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    module = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    state = table.Column<short>(type: "smallint", nullable: false),
                    active_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    active_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    trial_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    source = table.Column<short>(type: "smallint", nullable: false),
                    trial_used = table.Column<bool>(type: "boolean", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_entitlement", x => new { x.tenant_id, x.module });
                });

            migrationBuilder.CreateTable(
                name: "entitlement_history",
                schema: "entitlements",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    module = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    from_state = table.Column<short>(type: "smallint", nullable: true),
                    to_state = table.Column<short>(type: "smallint", nullable: false),
                    actor_roles = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    source = table.Column<short>(type: "smallint", nullable: false),
                    reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_entitlement_history", x => new { x.tenant_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "tenant_limit",
                schema: "entitlements",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    value = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenant_limit", x => new { x.tenant_id, x.name });
                });

            migrationBuilder.CreateIndex(
                name: "IX_entitlement_history_tenant_id_module_occurred_at",
                schema: "entitlements",
                table: "entitlement_history",
                columns: new[] { "tenant_id", "module", "occurred_at" });

            // Row Level Security (A-011, Backend 5.1 Nr. 4)
            RowLevelSecurity.IsolateByTenant(migrationBuilder, "entitlements", "entitlement");
            RowLevelSecurity.IsolateByTenant(migrationBuilder, "entitlements", "entitlement_history");
            RowLevelSecurity.IsolateByTenant(migrationBuilder, "entitlements", "tenant_limit");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "entitlement",
                schema: "entitlements");

            migrationBuilder.DropTable(
                name: "entitlement_history",
                schema: "entitlements");

            migrationBuilder.DropTable(
                name: "tenant_limit",
                schema: "entitlements");
        }
    }
}
