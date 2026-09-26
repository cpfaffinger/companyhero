using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CompanyHero.Migrations.Migrations.Metering
{
    /// <inheritdoc />
    public partial class Geld : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "rated",
                schema: "metering",
                table: "ledger_event",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "billing_period",
                schema: "metering",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    opened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    sealed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    protected_salt = table.Column<byte[]>(type: "bytea", nullable: true),
                    salt_destroyed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_billing_period", x => new { x.tenant_id, x.period });
                });

            migrationBuilder.CreateTable(
                name: "daily_aggregate",
                schema: "metering",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    module = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    metric = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    rated_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unrated_quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    event_count = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_daily_aggregate", x => new { x.tenant_id, x.period, x.day, x.module, x.metric });
                });

            migrationBuilder.CreateTable(
                name: "invoice_draft",
                schema: "metering",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    period = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    tax_rate = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    net = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    tax = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    gross = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    would_have_been = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    lines_json = table.Column<string>(type: "jsonb", nullable: false),
                    plan_name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_invoice_draft", x => new { x.tenant_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "price_plan_version",
                schema: "metering",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    valid_from_period = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    plan_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_price_plan_version", x => new { x.tenant_id, x.id });
                });

            migrationBuilder.CreateIndex(
                name: "IX_invoice_draft_tenant_id_period",
                schema: "metering",
                table: "invoice_draft",
                columns: new[] { "tenant_id", "period" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_price_plan_version_tenant_id_valid_from_period",
                schema: "metering",
                table: "price_plan_version",
                columns: new[] { "tenant_id", "valid_from_period" });

            // Row Level Security (A-011, Backend 5.1 Nr. 4)
            RowLevelSecurity.IsolateByTenant(migrationBuilder, "metering", "billing_period");
            RowLevelSecurity.IsolateByTenant(migrationBuilder, "metering", "daily_aggregate");
            RowLevelSecurity.IsolateByTenant(migrationBuilder, "metering", "invoice_draft");
            RowLevelSecurity.IsolateByTenant(migrationBuilder, "metering", "price_plan_version");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "billing_period",
                schema: "metering");

            migrationBuilder.DropTable(
                name: "daily_aggregate",
                schema: "metering");

            migrationBuilder.DropTable(
                name: "invoice_draft",
                schema: "metering");

            migrationBuilder.DropTable(
                name: "price_plan_version",
                schema: "metering");

            migrationBuilder.DropColumn(
                name: "rated",
                schema: "metering",
                table: "ledger_event");
        }
    }
}
