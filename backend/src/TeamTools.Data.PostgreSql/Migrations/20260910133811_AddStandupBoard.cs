using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TeamTools.Data.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddStandupBoard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StandupBoard",
                columns: table => new
                {
                    RoomId = table.Column<Guid>(type: "uuid", nullable: false),
                    PreviousBoardShortCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StandupBoard", x => x.RoomId);
                    table.ForeignKey(
                        name: "FK_StandupBoard_Rooms_RoomId",
                        column: x => x.RoomId,
                        principalTable: "Rooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StandupBlocker",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BoardId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Text = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    OwnerUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    OwnerName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CarriedFromBoardId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StandupBlocker", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StandupBlocker_StandupBoard_BoardId",
                        column: x => x.BoardId,
                        principalTable: "StandupBoard",
                        principalColumn: "RoomId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StandupEntry",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BoardId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuestionId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StandupEntry", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StandupEntry_StandupBoard_BoardId",
                        column: x => x.BoardId,
                        principalTable: "StandupBoard",
                        principalColumn: "RoomId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StandupQuestion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BoardId = table.Column<Guid>(type: "uuid", nullable: false),
                    Text = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StandupQuestion", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StandupQuestion_StandupBoard_BoardId",
                        column: x => x.BoardId,
                        principalTable: "StandupBoard",
                        principalColumn: "RoomId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StandupBlocker_BoardId",
                table: "StandupBlocker",
                column: "BoardId");

            migrationBuilder.CreateIndex(
                name: "IX_StandupEntry_BoardId",
                table: "StandupEntry",
                column: "BoardId");

            migrationBuilder.CreateIndex(
                name: "IX_StandupEntry_BoardId_AuthorUserId",
                table: "StandupEntry",
                columns: new[] { "BoardId", "AuthorUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_StandupQuestion_BoardId",
                table: "StandupQuestion",
                column: "BoardId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StandupBlocker");

            migrationBuilder.DropTable(
                name: "StandupEntry");

            migrationBuilder.DropTable(
                name: "StandupQuestion");

            migrationBuilder.DropTable(
                name: "StandupBoard");
        }
    }
}
