using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CompanyHero.Migrations.Migrations.Metering
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "metering");

            migrationBuilder.CreateTable(
                name: "ledger_event",
                schema: "metering",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    module = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    metric = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    subject_ref = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    source = table.Column<short>(type: "smallint", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    period = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    late = table.Column<bool>(type: "boolean", nullable: false),
                    reversal_of = table.Column<Guid>(type: "uuid", nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ledger_event", x => new { x.tenant_id, x.id });
                });

            migrationBuilder.CreateIndex(
                name: "IX_ledger_event_tenant_id_idempotency_key",
                schema: "metering",
                table: "ledger_event",
                columns: new[] { "tenant_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ledger_event_tenant_id_period_metric",
                schema: "metering",
                table: "ledger_event",
                columns: new[] { "tenant_id", "period", "metric" });

            // Row Level Security (A-011, Backend 5.1 Nr. 4)
            RowLevelSecurity.IsolateByTenant(migrationBuilder, "metering", "ledger_event");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ledger_event",
                schema: "metering");
        }
    }
}
