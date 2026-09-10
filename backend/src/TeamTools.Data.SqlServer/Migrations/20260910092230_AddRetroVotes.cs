using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TeamTools.Data.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddRetroVotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowMultiplePerItem",
                table: "RetroBoard",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "VoteBudget",
                table: "RetroBoard",
                type: "int",
                nullable: false,
                // Not 0 (the scaffolded default): a board with a zero budget lets nobody vote. Boards
                // created before dot voting existed get the standard three dots.
                defaultValue: 3);

            migrationBuilder.CreateTable(
                name: "RetroVote",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BoardId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VoterUserId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TargetKind = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    TargetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RetroVote", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RetroVote_RetroBoard_BoardId",
                        column: x => x.BoardId,
                        principalTable: "RetroBoard",
                        principalColumn: "RoomId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RetroVote_BoardId",
                table: "RetroVote",
                column: "BoardId");

            migrationBuilder.CreateIndex(
                name: "IX_RetroVote_BoardId_VoterUserId",
                table: "RetroVote",
                columns: new[] { "BoardId", "VoterUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_RetroVote_TargetKind_TargetId",
                table: "RetroVote",
                columns: new[] { "TargetKind", "TargetId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RetroVote");

            migrationBuilder.DropColumn(
                name: "AllowMultiplePerItem",
                table: "RetroBoard");

            migrationBuilder.DropColumn(
                name: "VoteBudget",
                table: "RetroBoard");
        }
    }
}
