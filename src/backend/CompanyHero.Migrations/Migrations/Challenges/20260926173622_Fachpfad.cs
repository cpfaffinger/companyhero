using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CompanyHero.Migrations.Migrations.Challenges
{
    /// <inheritdoc />
    public partial class Fachpfad : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "activity_event_id",
                schema: "challenges",
                table: "contribution",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid[]>(
                name: "group_ids",
                schema: "challenges",
                table: "contribution",
                type: "uuid[]",
                nullable: false,
                defaultValue: new Guid[0]);

            migrationBuilder.AddColumn<Guid>(
                name: "reversal_of",
                schema: "challenges",
                table: "contribution",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "reversed",
                schema: "challenges",
                table: "contribution",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "milestone_reached",
                schema: "challenges",
                table: "collective_state",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "archived_at",
                schema: "challenges",
                table: "challenge",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "created_by",
                schema: "challenges",
                table: "challenge",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "description",
                schema: "challenges",
                table: "challenge",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ended_at",
                schema: "challenges",
                table: "challenge",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ended_early_reason",
                schema: "challenges",
                table: "challenge",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "planned_at",
                schema: "challenges",
                table: "challenge",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "previewed_at",
                schema: "challenges",
                table: "challenge",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "template_key",
                schema: "challenges",
                table: "challenge",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "visibility",
                schema: "challenges",
                table: "challenge",
                type: "smallint",
                nullable: false,
                defaultValue: (short)3);

            migrationBuilder.CreateIndex(
                name: "IX_challenge_tenant_id_state",
                schema: "challenges",
                table: "challenge",
                columns: new[] { "tenant_id", "state" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_challenge_tenant_id_state",
                schema: "challenges",
                table: "challenge");

            migrationBuilder.DropColumn(
                name: "activity_event_id",
                schema: "challenges",
                table: "contribution");

            migrationBuilder.DropColumn(
                name: "group_ids",
                schema: "challenges",
                table: "contribution");

            migrationBuilder.DropColumn(
                name: "reversal_of",
                schema: "challenges",
                table: "contribution");

            migrationBuilder.DropColumn(
                name: "reversed",
                schema: "challenges",
                table: "contribution");

            migrationBuilder.DropColumn(
                name: "milestone_reached",
                schema: "challenges",
                table: "collective_state");

            migrationBuilder.DropColumn(
                name: "archived_at",
                schema: "challenges",
                table: "challenge");

            migrationBuilder.DropColumn(
                name: "created_by",
                schema: "challenges",
                table: "challenge");

            migrationBuilder.DropColumn(
                name: "description",
                schema: "challenges",
                table: "challenge");

            migrationBuilder.DropColumn(
                name: "ended_at",
                schema: "challenges",
                table: "challenge");

            migrationBuilder.DropColumn(
                name: "ended_early_reason",
                schema: "challenges",
                table: "challenge");

            migrationBuilder.DropColumn(
                name: "planned_at",
                schema: "challenges",
                table: "challenge");

            migrationBuilder.DropColumn(
                name: "previewed_at",
                schema: "challenges",
                table: "challenge");

            migrationBuilder.DropColumn(
                name: "template_key",
                schema: "challenges",
                table: "challenge");

            migrationBuilder.DropColumn(
                name: "visibility",
                schema: "challenges",
                table: "challenge");
        }
    }
}
