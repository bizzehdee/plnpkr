using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlanningPoker.Data.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Sessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ShortCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    DeckType = table.Column<string>(type: "TEXT", nullable: false),
                    CustomCards = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    State = table.Column<string>(type: "TEXT", nullable: false),
                    OrganiserUserId = table.Column<string>(type: "TEXT", nullable: true),
                    AutoReveal = table.Column<bool>(type: "INTEGER", nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    CurrentStory = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastActivityAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LinkedProvider = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    LinkedIssue_Key = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    LinkedIssue_Title = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    LinkedIssue_Description = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: true),
                    LinkedIssue_Url = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    LinkedIssue_StoryPoints = table.Column<double>(type: "REAL", nullable: true),
                    LinkedIssue_StoryPointsFieldAvailable = table.Column<bool>(type: "INTEGER", nullable: true),
                    TicketQueue = table.Column<string>(type: "TEXT", nullable: false),
                    ReactionsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    AllowRoleChange = table.Column<bool>(type: "INTEGER", nullable: false),
                    TimerDurationSeconds = table.Column<int>(type: "INTEGER", nullable: true),
                    TimerDeadline = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    TimerPausedRemainingSeconds = table.Column<int>(type: "INTEGER", nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    DeletedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Participants",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SessionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    NormalizedName = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    IsOrganiser = table.Column<bool>(type: "INTEGER", nullable: false),
                    Role = table.Column<string>(type: "TEXT", nullable: false),
                    Vote = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    HasVoted = table.Column<bool>(type: "INTEGER", nullable: false),
                    ChangedAfterReveal = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsConnected = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Participants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Participants_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Participants_SessionId_NormalizedName",
                table: "Participants",
                columns: new[] { "SessionId", "NormalizedName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Participants_SessionId_UserId",
                table: "Participants",
                columns: new[] { "SessionId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_ShortCode",
                table: "Sessions",
                column: "ShortCode",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Participants");

            migrationBuilder.DropTable(
                name: "Sessions");
        }
    }
}
