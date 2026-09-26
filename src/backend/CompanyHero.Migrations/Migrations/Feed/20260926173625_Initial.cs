using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CompanyHero.Migrations.Migrations.Feed
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "feed");

            migrationBuilder.CreateTable(
                name: "feed_entry",
                schema: "feed",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<short>(type: "smallint", nullable: false),
                    event_key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    text_key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    @params = table.Column<string>(name: "params", type: "jsonb", nullable: false),
                    subject_person_id = table.Column<Guid>(type: "uuid", nullable: true),
                    scope = table.Column<short>(type: "smallint", nullable: false),
                    group_ids = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    reference_kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    reference_id = table.Column<Guid>(type: "uuid", nullable: true),
                    body = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    day = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_feed_entry", x => new { x.tenant_id, x.id });
                });

            migrationBuilder.CreateIndex(
                name: "IX_feed_entry_tenant_id_event_key",
                schema: "feed",
                table: "feed_entry",
                columns: new[] { "tenant_id", "event_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_feed_entry_tenant_id_occurred_at",
                schema: "feed",
                table: "feed_entry",
                columns: new[] { "tenant_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "IX_feed_entry_tenant_id_subject_person_id_day",
                schema: "feed",
                table: "feed_entry",
                columns: new[] { "tenant_id", "subject_person_id", "day" });

            // Row Level Security (A-011, Backend 5.1 Nr. 4)
            RowLevelSecurity.IsolateByTenant(migrationBuilder, "feed", "feed_entry");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "feed_entry",
                schema: "feed");
        }
    }
}
