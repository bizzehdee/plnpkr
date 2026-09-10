using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TeamTools.Data.SqlServer.Migrations
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
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AllowParticipantGrouping",
                table: "RetroBoard",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "RetroGroup",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BoardId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Label = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false)
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
