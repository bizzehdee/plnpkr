using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TeamTools.Data.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddRetroGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "GroupId",
                table: "RetroCard",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AllowParticipantGrouping",
                table: "RetroBoard",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "RetroGroup",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BoardId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Label = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Order = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RetroGroup", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RetroGroup_RetroBoard_BoardId",
                        column: x => x.BoardId,
                        principalTable: "RetroBoard",
                        principalColumn: "RoomId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RetroCard_GroupId",
                table: "RetroCard",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_RetroGroup_BoardId",
                table: "RetroGroup",
                column: "BoardId");

            migrationBuilder.AddForeignKey(
                name: "FK_RetroCard_RetroGroup_GroupId",
                table: "RetroCard",
                column: "GroupId",
                principalTable: "RetroGroup",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RetroCard_RetroGroup_GroupId",
                table: "RetroCard");

            migrationBuilder.DropTable(
                name: "RetroGroup");

            migrationBuilder.DropIndex(
                name: "IX_RetroCard_GroupId",
                table: "RetroCard");

            migrationBuilder.DropColumn(
                name: "GroupId",
                table: "RetroCard");

            migrationBuilder.DropColumn(
                name: "AllowParticipantGrouping",
                table: "RetroBoard");
        }
    }
}
