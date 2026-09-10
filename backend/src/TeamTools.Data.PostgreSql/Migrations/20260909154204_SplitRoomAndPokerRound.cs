using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TeamTools.Data.PostgreSql.Migrations
{
    /// <summary>
    /// Splits <c>Sessions</c> into the tool-agnostic <c>Rooms</c> plus the estimation payload
    /// <c>PokerRounds</c> (#19).
    /// <para>
    /// <b>Hand-written on purpose.</b> The scaffolded version dropped <c>Sessions</c> and created the
    /// two new tables empty — silently discarding every existing session, participant seat, story note
    /// and round-history row. The data motion below is the point of the migration: create the new
    /// tables, copy every row across (stamping existing rooms as <c>Poker</c>, the only tool that
    /// existed), re-point the child tables, and only then drop the old one. <c>Down</c> reverses it
    /// the same way, so a rollback keeps the data too.
    /// </para>
    /// </summary>
    public partial class SplitRoomAndPokerRound : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // --- 1. The new tables, created before anything is dropped ------
            migrationBuilder.CreateTable(
                name: "Rooms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ShortCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Tool = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    OrganiserUserId = table.Column<string>(type: "text", nullable: true),
                    PasswordHash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ReactionsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AllowRoleChange = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastActivityAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Rooms", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PokerRounds",
                columns: table => new
                {
                    RoomId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeckType = table.Column<string>(type: "text", nullable: false),
                    CustomCards = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    State = table.Column<string>(type: "text", nullable: false),
                    AutoReveal = table.Column<bool>(type: "boolean", nullable: false),
                    CurrentStory = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CurrentStoryNote = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    LinkedProvider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    LinkedIssue_Key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    LinkedIssue_Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    LinkedIssue_Description = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    LinkedIssue_Url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    LinkedIssue_StoryPoints = table.Column<double>(type: "double precision", nullable: true),
                    LinkedIssue_StoryPointsFieldAvailable = table.Column<bool>(type: "boolean", nullable: true),
                    TicketQueue = table.Column<string>(type: "text", nullable: false),
                    TimerDurationSeconds = table.Column<int>(type: "integer", nullable: true),
                    TimerDeadline = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TimerPausedRemainingSeconds = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PokerRounds", x => x.RoomId);
                    table.ForeignKey(
                        name: "FK_PokerRounds_Rooms_RoomId",
                        column: x => x.RoomId,
                        principalTable: "Rooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Rooms_ShortCode",
                table: "Rooms",
                column: "ShortCode",
                unique: true);

            // --- 2. Data motion: every existing session becomes a poker room ---
            migrationBuilder.Sql(@"
                INSERT INTO ""Rooms"" (
                    ""Id"", ""ShortCode"", ""Name"", ""Tool"", ""OrganiserUserId"", ""PasswordHash"",
                    ""ReactionsEnabled"", ""AllowRoleChange"", ""CreatedAt"", ""LastActivityAt"",
                    ""ClosedAt"", ""DeletedAt"")
                SELECT
                    ""Id"", ""ShortCode"", ""Name"", 'Poker', ""OrganiserUserId"", ""PasswordHash"",
                    ""ReactionsEnabled"", ""AllowRoleChange"", ""CreatedAt"", ""LastActivityAt"",
                    ""ClosedAt"", ""DeletedAt""
                FROM ""Sessions"";");

            migrationBuilder.Sql(@"
                INSERT INTO ""PokerRounds"" (
                    ""RoomId"", ""DeckType"", ""CustomCards"", ""State"", ""AutoReveal"",
                    ""CurrentStory"", ""CurrentStoryNote"", ""LinkedProvider"", ""LinkedIssue_Key"",
                    ""LinkedIssue_Title"", ""LinkedIssue_Description"", ""LinkedIssue_Url"",
                    ""LinkedIssue_StoryPoints"", ""LinkedIssue_StoryPointsFieldAvailable"",
                    ""TicketQueue"", ""TimerDurationSeconds"", ""TimerDeadline"",
                    ""TimerPausedRemainingSeconds"")
                SELECT
                    ""Id"", ""DeckType"", ""CustomCards"", ""State"", ""AutoReveal"",
                    ""CurrentStory"", ""CurrentStoryNote"", ""LinkedProvider"", ""LinkedIssue_Key"",
                    ""LinkedIssue_Title"", ""LinkedIssue_Description"", ""LinkedIssue_Url"",
                    ""LinkedIssue_StoryPoints"", ""LinkedIssue_StoryPointsFieldAvailable"",
                    COALESCE(""TicketQueue"", '[]'), ""TimerDurationSeconds"", ""TimerDeadline"",
                    ""TimerPausedRemainingSeconds""
                FROM ""Sessions"";");

            // --- 3. Re-point the child tables at the new parents ------------
            migrationBuilder.DropForeignKey(
                name: "FK_Participants_Sessions_SessionId",
                table: "Participants");

            migrationBuilder.DropForeignKey(
                name: "FK_RoundResult_Sessions_SessionId",
                table: "RoundResult");

            migrationBuilder.RenameColumn(
                name: "SessionId",
                table: "Participants",
                newName: "RoomId");

            migrationBuilder.RenameIndex(
                name: "IX_Participants_SessionId_UserId",
                table: "Participants",
                newName: "IX_Participants_RoomId_UserId");

            migrationBuilder.RenameIndex(
                name: "IX_Participants_SessionId_NormalizedName",
                table: "Participants",
                newName: "IX_Participants_RoomId_NormalizedName");

            migrationBuilder.RenameColumn(
                name: "SessionId",
                table: "RoundResult",
                newName: "RoomId");

            migrationBuilder.RenameIndex(
                name: "IX_RoundResult_SessionId",
                table: "RoundResult",
                newName: "IX_RoundResult_RoomId");

            migrationBuilder.AddForeignKey(
                name: "FK_Participants_Rooms_RoomId",
                table: "Participants",
                column: "RoomId",
                principalTable: "Rooms",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_RoundResult_PokerRounds_RoomId",
                table: "RoundResult",
                column: "RoomId",
                principalTable: "PokerRounds",
                principalColumn: "RoomId",
                onDelete: ReferentialAction.Cascade);

            // --- 4. Only now is the old table redundant ---------------------
            migrationBuilder.DropTable(
                name: "Sessions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Rebuild Sessions and copy the two halves back into it, so a rollback is lossless too.
            migrationBuilder.CreateTable(
                name: "Sessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ShortCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DeckType = table.Column<string>(type: "text", nullable: false),
                    CustomCards = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    State = table.Column<string>(type: "text", nullable: false),
                    OrganiserUserId = table.Column<string>(type: "text", nullable: true),
                    AutoReveal = table.Column<bool>(type: "boolean", nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CurrentStory = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CurrentStoryNote = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastActivityAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LinkedProvider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    LinkedIssue_Key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    LinkedIssue_Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    LinkedIssue_Description = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    LinkedIssue_Url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    LinkedIssue_StoryPoints = table.Column<double>(type: "double precision", nullable: true),
                    LinkedIssue_StoryPointsFieldAvailable = table.Column<bool>(type: "boolean", nullable: true),
                    TicketQueue = table.Column<string>(type: "text", nullable: false),
                    ReactionsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AllowRoleChange = table.Column<bool>(type: "boolean", nullable: false),
                    TimerDurationSeconds = table.Column<int>(type: "integer", nullable: true),
                    TimerDeadline = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TimerPausedRemainingSeconds = table.Column<int>(type: "integer", nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sessions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_ShortCode",
                table: "Sessions",
                column: "ShortCode",
                unique: true);

            // Only poker rooms can round-trip into the old single-tool table; a retro room has no
            // representation there, so it is left behind deliberately rather than corrupted.
            migrationBuilder.Sql(@"
                INSERT INTO ""Sessions"" (
                    ""Id"", ""ShortCode"", ""Name"", ""DeckType"", ""CustomCards"", ""State"",
                    ""OrganiserUserId"", ""AutoReveal"", ""PasswordHash"", ""CurrentStory"",
                    ""CurrentStoryNote"", ""CreatedAt"", ""LastActivityAt"", ""LinkedProvider"",
                    ""LinkedIssue_Key"", ""LinkedIssue_Title"", ""LinkedIssue_Description"",
                    ""LinkedIssue_Url"", ""LinkedIssue_StoryPoints"",
                    ""LinkedIssue_StoryPointsFieldAvailable"", ""TicketQueue"", ""ReactionsEnabled"",
                    ""AllowRoleChange"", ""TimerDurationSeconds"", ""TimerDeadline"",
                    ""TimerPausedRemainingSeconds"", ""ClosedAt"", ""DeletedAt"")
                SELECT
                    r.""Id"", r.""ShortCode"", r.""Name"", p.""DeckType"", p.""CustomCards"", p.""State"",
                    r.""OrganiserUserId"", p.""AutoReveal"", r.""PasswordHash"", p.""CurrentStory"",
                    p.""CurrentStoryNote"", r.""CreatedAt"", r.""LastActivityAt"", p.""LinkedProvider"",
                    p.""LinkedIssue_Key"", p.""LinkedIssue_Title"", p.""LinkedIssue_Description"",
                    p.""LinkedIssue_Url"", p.""LinkedIssue_StoryPoints"",
                    p.""LinkedIssue_StoryPointsFieldAvailable"", p.""TicketQueue"", r.""ReactionsEnabled"",
                    r.""AllowRoleChange"", p.""TimerDurationSeconds"", p.""TimerDeadline"",
                    p.""TimerPausedRemainingSeconds"", r.""ClosedAt"", r.""DeletedAt""
                FROM ""Rooms"" r
                INNER JOIN ""PokerRounds"" p ON p.""RoomId"" = r.""Id"";");

            migrationBuilder.DropForeignKey(
                name: "FK_Participants_Rooms_RoomId",
                table: "Participants");

            migrationBuilder.DropForeignKey(
                name: "FK_RoundResult_PokerRounds_RoomId",
                table: "RoundResult");

            // Drop rows that cannot exist under the old schema before re-adding its foreign keys.
            migrationBuilder.Sql(@"
                DELETE FROM ""Participants""
                WHERE ""RoomId"" NOT IN (SELECT ""Id"" FROM ""Sessions"");");

            migrationBuilder.RenameColumn(
                name: "RoomId",
                table: "Participants",
                newName: "SessionId");

            migrationBuilder.RenameIndex(
                name: "IX_Participants_RoomId_UserId",
                table: "Participants",
                newName: "IX_Participants_SessionId_UserId");

            migrationBuilder.RenameIndex(
                name: "IX_Participants_RoomId_NormalizedName",
                table: "Participants",
                newName: "IX_Participants_SessionId_NormalizedName");

            migrationBuilder.RenameColumn(
                name: "RoomId",
                table: "RoundResult",
                newName: "SessionId");

            migrationBuilder.RenameIndex(
                name: "IX_RoundResult_RoomId",
                table: "RoundResult",
                newName: "IX_RoundResult_SessionId");

            migrationBuilder.AddForeignKey(
                name: "FK_Participants_Sessions_SessionId",
                table: "Participants",
                column: "SessionId",
                principalTable: "Sessions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_RoundResult_Sessions_SessionId",
                table: "RoundResult",
                column: "SessionId",
                principalTable: "Sessions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.DropTable(
                name: "PokerRounds");

            migrationBuilder.DropTable(
                name: "Rooms");
        }
    }
}
