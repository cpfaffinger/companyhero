using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CompanyHero.Migrations.Migrations.Organisation
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "organisation");

            migrationBuilder.CreateTable(
                name: "membership",
                schema: "organisation",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<short>(type: "smallint", nullable: false),
                    joined_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_membership", x => new { x.tenant_id, x.person_id });
                });

            migrationBuilder.CreateTable(
                name: "organisation",
                schema: "organisation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<short>(type: "smallint", nullable: false),
                    parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    state = table.Column<short>(type: "smallint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_organisation", x => x.id);
                    table.ForeignKey(
                        name: "FK_organisation_organisation_parent_id",
                        column: x => x.parent_id,
                        principalSchema: "organisation",
                        principalTable: "organisation",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "role_assignment",
                schema: "organisation",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    assigned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_role_assignment", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "FK_role_assignment_membership_tenant_id_person_id",
                        columns: x => new { x.tenant_id, x.person_id },
                        principalSchema: "organisation",
                        principalTable: "membership",
                        principalColumns: new[] { "tenant_id", "person_id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_organisation_parent_id",
                schema: "organisation",
                table: "organisation",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "IX_role_assignment_tenant_id_person_id_role",
                schema: "organisation",
                table: "role_assignment",
                columns: new[] { "tenant_id", "person_id", "role" },
                unique: true);

            // Mitgliedschaften gehören zu einem existierenden Tenant (zusammengesetzte Schlüssel entlang tenant_id, A-011).
            migrationBuilder.AddForeignKey(
                name: "FK_membership_organisation_tenant_id",
                schema: "organisation",
                table: "membership",
                column: "tenant_id",
                principalSchema: "organisation",
                principalTable: "organisation",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // Row Level Security (A-011, Backend 5.1 Nr. 4, 5.3): Organisationen als Plattformdaten, Rest tenantbezogen.
            RowLevelSecurity.IsolateOrganisationRows(migrationBuilder, "organisation", "organisation");
            RowLevelSecurity.IsolateByTenant(migrationBuilder, "organisation", "membership");
            RowLevelSecurity.IsolateByTenant(migrationBuilder, "organisation", "role_assignment");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_membership_organisation_tenant_id",
                schema: "organisation",
                table: "membership");

            migrationBuilder.DropTable(
                name: "organisation",
                schema: "organisation");

            migrationBuilder.DropTable(
                name: "role_assignment",
                schema: "organisation");

            migrationBuilder.DropTable(
                name: "membership",
                schema: "organisation");
        }
    }
}
