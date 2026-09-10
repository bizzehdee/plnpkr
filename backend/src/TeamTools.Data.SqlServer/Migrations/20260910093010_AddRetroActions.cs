using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TeamTools.Data.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddRetroActions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RetroActionItem",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BoardId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    OwnerUserId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    OwnerName = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    DueDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DoneAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SourceGroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CarriedFromBoardId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RetroActionItem", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RetroActionItem_RetroBoard_BoardId",
                        column: x => x.BoardId,
                        principalTable: "RetroBoard",
                        principalColumn: "RoomId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RetroActionItem_BoardId",
                table: "RetroActionItem",
                column: "BoardId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RetroActionItem");
        }
    }
}
