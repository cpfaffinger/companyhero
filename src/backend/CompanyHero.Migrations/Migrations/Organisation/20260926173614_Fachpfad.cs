using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CompanyHero.Migrations.Migrations.Organisation
{
    /// <inheritdoc />
    public partial class Fachpfad : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "deleted_at",
                schema: "organisation",
                table: "organisation",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "suspended_at",
                schema: "organisation",
                table: "organisation",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "suspension_reason",
                schema: "organisation",
                table: "organisation",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "terminated_at",
                schema: "organisation",
                table: "organisation",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "termination_effective_at",
                schema: "organisation",
                table: "organisation",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "time_zone",
                schema: "organisation",
                table: "organisation",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "Europe/Vienna");

            migrationBuilder.CreateTable(
                name: "group_dimension",
                schema: "organisation",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_group_dimension", x => new { x.tenant_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "headcount",
                schema: "organisation",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    count = table.Column<int>(type: "integer", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_headcount", x => new { x.tenant_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "member_group",
                schema: "organisation",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    dimension_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_member_group", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "FK_member_group_group_dimension_tenant_id_dimension_id",
                        columns: x => new { x.tenant_id, x.dimension_id },
                        principalSchema: "organisation",
                        principalTable: "group_dimension",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "group_membership",
                schema: "organisation",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dimension_id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    since = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_group_membership", x => new { x.tenant_id, x.person_id, x.dimension_id });
                    table.ForeignKey(
                        name: "FK_group_membership_member_group_tenant_id_group_id",
                        columns: x => new { x.tenant_id, x.group_id },
                        principalSchema: "organisation",
                        principalTable: "member_group",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_group_dimension_tenant_id_position",
                schema: "organisation",
                table: "group_dimension",
                columns: new[] { "tenant_id", "position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_group_membership_tenant_id_group_id",
                schema: "organisation",
                table: "group_membership",
                columns: new[] { "tenant_id", "group_id" });

            migrationBuilder.CreateIndex(
                name: "IX_headcount_tenant_id_group_id_effective_from",
                schema: "organisation",
                table: "headcount",
                columns: new[] { "tenant_id", "group_id", "effective_from" });

            migrationBuilder.CreateIndex(
                name: "IX_member_group_tenant_id_dimension_id",
                schema: "organisation",
                table: "member_group",
                columns: new[] { "tenant_id", "dimension_id" });

            // Row Level Security (A-011, Backend 5.1 Nr. 4)
            RowLevelSecurity.IsolateByTenant(migrationBuilder, "organisation", "group_dimension");
            RowLevelSecurity.IsolateByTenant(migrationBuilder, "organisation", "headcount");
            RowLevelSecurity.IsolateByTenant(migrationBuilder, "organisation", "member_group");
            RowLevelSecurity.IsolateByTenant(migrationBuilder, "organisation", "group_membership");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "group_membership",
                schema: "organisation");

            migrationBuilder.DropTable(
                name: "headcount",
                schema: "organisation");

            migrationBuilder.DropTable(
                name: "member_group",
                schema: "organisation");

            migrationBuilder.DropTable(
                name: "group_dimension",
                schema: "organisation");

            migrationBuilder.DropColumn(
                name: "deleted_at",
                schema: "organisation",
                table: "organisation");

            migrationBuilder.DropColumn(
                name: "suspended_at",
                schema: "organisation",
                table: "organisation");

            migrationBuilder.DropColumn(
                name: "suspension_reason",
                schema: "organisation",
                table: "organisation");

            migrationBuilder.DropColumn(
                name: "terminated_at",
                schema: "organisation",
                table: "organisation");

            migrationBuilder.DropColumn(
                name: "termination_effective_at",
                schema: "organisation",
                table: "organisation");

            migrationBuilder.DropColumn(
                name: "time_zone",
                schema: "organisation",
                table: "organisation");
        }
    }
}
