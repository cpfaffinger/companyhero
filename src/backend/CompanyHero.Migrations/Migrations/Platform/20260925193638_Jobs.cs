using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CompanyHero.Migrations.Migrations.Platform
{
    /// <inheritdoc />
    public partial class Jobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "job",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    job_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    run_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    lease_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    max_attempts = table.Column<int>(type: "integer", nullable: false),
                    worker_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    last_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    replayed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_job", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "job_schedule",
                schema: "platform",
                columns: table => new
                {
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    interval_seconds = table.Column<int>(type: "integer", nullable: false),
                    next_run_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_run_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_duration_ms = table.Column<int>(type: "integer", nullable: true),
                    last_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_job_schedule", x => x.name);
                });

            migrationBuilder.CreateIndex(
                name: "IX_job_job_type_idempotency_key_tenant_id",
                schema: "platform",
                table: "job",
                columns: new[] { "job_type", "idempotency_key", "tenant_id" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_job_status_run_at",
                schema: "platform",
                table: "job",
                columns: new[] { "status", "run_at" });

            migrationBuilder.CreateIndex(
                name: "IX_job_tenant_id_status_completed_at",
                schema: "platform",
                table: "job",
                columns: new[] { "tenant_id", "status", "completed_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "job",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "job_schedule",
                schema: "platform");
        }
    }
}
