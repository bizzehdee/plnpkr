using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TeamTools.Data.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddRetroPhase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Phase",
                table: "RetroBoard",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                // Not "" (the scaffolded default): Phase is a string-converted enum, so an empty string
                // is unparseable and would break every board that already exists. A board created before
                // phases existed is, by definition, still collecting.
                defaultValue: "Collect");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PhaseDeadline",
                table: "RetroBoard",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PhaseDurationSeconds",
                table: "RetroBoard",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Phase",
                table: "RetroBoard");

            migrationBuilder.DropColumn(
                name: "PhaseDeadline",
                table: "RetroBoard");

            migrationBuilder.DropColumn(
                name: "PhaseDurationSeconds",
                table: "RetroBoard");
        }
    }
}
