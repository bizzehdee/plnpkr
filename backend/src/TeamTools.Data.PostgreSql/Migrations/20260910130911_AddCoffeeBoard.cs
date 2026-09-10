using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TeamTools.Data.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddCoffeeBoard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CoffeeBoard",
                columns: table => new
                {
                    RoomId = table.Column<Guid>(type: "uuid", nullable: false),
                    Phase = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    PhaseDurationSeconds = table.Column<int>(type: "integer", nullable: true),
                    PhaseDeadline = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    VoteBudget = table.Column<int>(type: "integer", nullable: false),
                    AllowMultiplePerItem = table.Column<bool>(type: "boolean", nullable: false),
                    CurrentTopicId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExtendVoteOpen = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoffeeBoard", x => x.RoomId);
                    table.ForeignKey(
                        name: "FK_CoffeeBoard_Rooms_RoomId",
                        column: x => x.RoomId,
                        principalTable: "Rooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CoffeeDecision",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BoardId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    TopicId = table.Column<Guid>(type: "uuid", nullable: true),
                    OwnerUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    OwnerName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    DueDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DoneAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoffeeDecision", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CoffeeDecision_CoffeeBoard_BoardId",
                        column: x => x.BoardId,
                        principalTable: "CoffeeBoard",
                        principalColumn: "RoomId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CoffeeExtendVote",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BoardId = table.Column<Guid>(type: "uuid", nullable: false),
                    TopicId = table.Column<Guid>(type: "uuid", nullable: false),
                    VoterUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Choice = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoffeeExtendVote", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CoffeeExtendVote_CoffeeBoard_BoardId",
                        column: x => x.BoardId,
                        principalTable: "CoffeeBoard",
                        principalColumn: "RoomId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CoffeeTopic",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BoardId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Text = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DiscussedSeconds = table.Column<int>(type: "integer", nullable: false),
                    Extensions = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoffeeTopic", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CoffeeTopic_CoffeeBoard_BoardId",
                        column: x => x.BoardId,
                        principalTable: "CoffeeBoard",
                        principalColumn: "RoomId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CoffeeVote",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BoardId = table.Column<Guid>(type: "uuid", nullable: false),
                    VoterUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TargetId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoffeeVote", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CoffeeVote_CoffeeBoard_BoardId",
                        column: x => x.BoardId,
                        principalTable: "CoffeeBoard",
                        principalColumn: "RoomId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CoffeeDecision_BoardId",
                table: "CoffeeDecision",
                column: "BoardId");

            migrationBuilder.CreateIndex(
                name: "IX_CoffeeExtendVote_BoardId",
                table: "CoffeeExtendVote",
                column: "BoardId");

            migrationBuilder.CreateIndex(
                name: "IX_CoffeeTopic_BoardId",
                table: "CoffeeTopic",
                column: "BoardId");

            migrationBuilder.CreateIndex(
                name: "IX_CoffeeVote_BoardId",
                table: "CoffeeVote",
                column: "BoardId");

            migrationBuilder.CreateIndex(
                name: "IX_CoffeeVote_BoardId_VoterUserId",
                table: "CoffeeVote",
                columns: new[] { "BoardId", "VoterUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_CoffeeVote_TargetId",
                table: "CoffeeVote",
                column: "TargetId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CoffeeDecision");

            migrationBuilder.DropTable(
                name: "CoffeeExtendVote");

            migrationBuilder.DropTable(
                name: "CoffeeTopic");

            migrationBuilder.DropTable(
                name: "CoffeeVote");

            migrationBuilder.DropTable(
                name: "CoffeeBoard");
        }
    }
}
