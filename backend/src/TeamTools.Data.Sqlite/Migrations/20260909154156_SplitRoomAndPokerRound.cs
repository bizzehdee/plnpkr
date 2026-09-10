using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TeamTools.Data.Sqlite.Migrations
{
    /// <summary>
    /// Splits <c>Sessions</c> into the tool-agnostic <c>Rooms</c> plus the estimation payload
    /// <c>PokerRounds</c> (#19).
    /// <para>
    /// <b>Hand-written on purpose.</b> The scaffolded version of this migration dropped
    /// <c>Sessions</c> and created the two new tables empty — silently discarding every existing
    /// session, participant seat, story note and round-history row. The data motion below is the
    /// point of the migration: create the new tables, copy every row across (stamping the existing
    /// rooms as <c>Poker</c>, the only tool that existed), re-point the child tables, and only then
    /// drop the old one. <c>Down</c> reverses it the same way, so a rollback keeps the data too.
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
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ShortCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Tool = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    OrganiserUserId = table.Column<string>(type: "TEXT", nullable: true),
                    PasswordHash = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    ReactionsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    AllowRoleChange = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastActivityAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ClosedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    DeletedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Rooms", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PokerRounds",
                columns: table => new
                {
                    RoomId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DeckType = table.Column<string>(type: "TEXT", nullable: false),
                    CustomCards = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    State = table.Column<string>(type: "TEXT", nullable: false),
                    AutoReveal = table.Column<bool>(type: "INTEGER", nullable: false),
                    CurrentStory = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CurrentStoryNote = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    LinkedProvider = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    LinkedIssue_Key = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    LinkedIssue_Title = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    LinkedIssue_Description = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: true),
                    LinkedIssue_Url = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    LinkedIssue_StoryPoints = table.Column<double>(type: "REAL", nullable: true),
                    LinkedIssue_StoryPointsFieldAvailable = table.Column<bool>(type: "INTEGER", nullable: true),
                    TicketQueue = table.Column<string>(type: "TEXT", nullable: false),
                    TimerDurationSeconds = table.Column<int>(type: "INTEGER", nullable: true),
                    TimerDeadline = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    TimerPausedRemainingSeconds = table.Column<int>(type: "INTEGER", nullable: true)
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
            //
            // Written as raw SQL rather than DropForeignKey/RenameColumn/AddForeignKey **because the
            // EF operations lose the data on SQLite.** SQLite cannot alter a foreign key in place, so
            // EF defers those tables to a rebuild emitted at the *end* of the migration — while
            // hoisting `DROP TABLE "Sessions"` ahead of it. With foreign keys enforced, SQLite's DROP
            // TABLE performs an implicit DELETE FROM that fires the children's ON DELETE CASCADE, so
            // every participant seat and round-history row is deleted before the rebuild copies them.
            // (The `PRAGMA foreign_keys = 0` EF wraps its rebuild in is also a no-op inside the
            // migration transaction, so it cannot save us either.) Rebuilding the children here, in
            // order, and dropping "Sessions" only once nothing references it, is what keeps the rows.
            // Covered by SplitRoomMigrationTests.
            migrationBuilder.Sql(@"
                CREATE TABLE ""Participants_new"" (
                    ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_Participants"" PRIMARY KEY AUTOINCREMENT,
                    ""ChangedAfterReveal"" INTEGER NOT NULL,
                    ""DisplayName"" TEXT NOT NULL,
                    ""HasVoted"" INTEGER NOT NULL,
                    ""IsConnected"" INTEGER NOT NULL,
                    ""IsOrganiser"" INTEGER NOT NULL,
                    ""LastSeenAt"" TEXT NOT NULL,
                    ""NormalizedName"" TEXT NOT NULL,
                    ""Role"" TEXT NOT NULL,
                    ""RoomId"" TEXT NOT NULL,
                    ""UserId"" TEXT NOT NULL,
                    ""Vote"" TEXT NULL,
                    CONSTRAINT ""FK_Participants_Rooms_RoomId"" FOREIGN KEY (""RoomId"")
                        REFERENCES ""Rooms"" (""Id"") ON DELETE CASCADE
                );");

            migrationBuilder.Sql(@"
                INSERT INTO ""Participants_new"" (
                    ""Id"", ""ChangedAfterReveal"", ""DisplayName"", ""HasVoted"", ""IsConnected"",
                    ""IsOrganiser"", ""LastSeenAt"", ""NormalizedName"", ""Role"", ""RoomId"",
                    ""UserId"", ""Vote"")
                SELECT
                    ""Id"", ""ChangedAfterReveal"", ""DisplayName"", ""HasVoted"", ""IsConnected"",
                    ""IsOrganiser"", ""LastSeenAt"", ""NormalizedName"", ""Role"", ""SessionId"",
                    ""UserId"", ""Vote""
                FROM ""Participants"";");

            migrationBuilder.Sql(@"DROP TABLE ""Participants"";");
            migrationBuilder.Sql(@"ALTER TABLE ""Participants_new"" RENAME TO ""Participants"";");
            migrationBuilder.Sql(@"
                CREATE UNIQUE INDEX ""IX_Participants_RoomId_UserId""
                    ON ""Participants"" (""RoomId"", ""UserId"");");
            migrationBuilder.Sql(@"
                CREATE UNIQUE INDEX ""IX_Participants_RoomId_NormalizedName""
                    ON ""Participants"" (""RoomId"", ""NormalizedName"");");

            migrationBuilder.Sql(@"
                CREATE TABLE ""RoundResult_new"" (
                    ""Id"" TEXT NOT NULL CONSTRAINT ""PK_RoundResult"" PRIMARY KEY,
                    ""Average"" REAL NULL,
                    ""Consensus"" INTEGER NOT NULL,
                    ""FinalEstimate"" TEXT NULL,
                    ""Note"" TEXT NULL,
                    ""RecordedAt"" TEXT NOT NULL,
                    ""RoomId"" TEXT NOT NULL,
                    ""Story"" TEXT NULL,
                    ""VoteCount"" INTEGER NOT NULL,
                    CONSTRAINT ""FK_RoundResult_PokerRounds_RoomId"" FOREIGN KEY (""RoomId"")
                        REFERENCES ""PokerRounds"" (""RoomId"") ON DELETE CASCADE
                );");

            migrationBuilder.Sql(@"
                INSERT INTO ""RoundResult_new"" (
                    ""Id"", ""Average"", ""Consensus"", ""FinalEstimate"", ""Note"", ""RecordedAt"",
                    ""RoomId"", ""Story"", ""VoteCount"")
                SELECT
                    ""Id"", ""Average"", ""Consensus"", ""FinalEstimate"", ""Note"", ""RecordedAt"",
                    ""SessionId"", ""Story"", ""VoteCount""
                FROM ""RoundResult"";");

            migrationBuilder.Sql(@"DROP TABLE ""RoundResult"";");
            migrationBuilder.Sql(@"ALTER TABLE ""RoundResult_new"" RENAME TO ""RoundResult"";");
            migrationBuilder.Sql(@"CREATE INDEX ""IX_RoundResult_RoomId"" ON ""RoundResult"" (""RoomId"");");

            // --- 4. Only now is the old table redundant ---------------------
            // Raw SQL, not DropTable: EF hoists a DropTable operation above the rebuilds above.
            // Nothing references "Sessions" by this point, so the drop cascades to nothing.
            migrationBuilder.Sql(@"DROP TABLE ""Sessions"";");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Rebuild Sessions and copy the two halves back into it, so a rollback is lossless too.
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
                    CurrentStoryNote = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
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

            // Re-point the children back, by hand and in order, for the same reason Up does: an EF
            // foreign-key change on SQLite becomes a deferred rebuild while the DropTable calls get
            // hoisted above it, and the implicit cascade would delete the rows first.
            // A seat belonging to a retro room has no home in the old schema, so the copy filters to
            // rooms that made it back into "Sessions" rather than violating the restored foreign key.
            migrationBuilder.Sql(@"
                CREATE TABLE ""Participants_old"" (
                    ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_Participants"" PRIMARY KEY AUTOINCREMENT,
                    ""ChangedAfterReveal"" INTEGER NOT NULL,
                    ""DisplayName"" TEXT NOT NULL,
                    ""HasVoted"" INTEGER NOT NULL,
                    ""IsConnected"" INTEGER NOT NULL,
                    ""IsOrganiser"" INTEGER NOT NULL,
                    ""LastSeenAt"" TEXT NOT NULL,
                    ""NormalizedName"" TEXT NOT NULL,
                    ""Role"" TEXT NOT NULL,
                    ""SessionId"" TEXT NOT NULL,
                    ""UserId"" TEXT NOT NULL,
                    ""Vote"" TEXT NULL,
                    CONSTRAINT ""FK_Participants_Sessions_SessionId"" FOREIGN KEY (""SessionId"")
                        REFERENCES ""Sessions"" (""Id"") ON DELETE CASCADE
                );");

            migrationBuilder.Sql(@"
                INSERT INTO ""Participants_old"" (
                    ""Id"", ""ChangedAfterReveal"", ""DisplayName"", ""HasVoted"", ""IsConnected"",
                    ""IsOrganiser"", ""LastSeenAt"", ""NormalizedName"", ""Role"", ""SessionId"",
                    ""UserId"", ""Vote"")
                SELECT
                    ""Id"", ""ChangedAfterReveal"", ""DisplayName"", ""HasVoted"", ""IsConnected"",
                    ""IsOrganiser"", ""LastSeenAt"", ""NormalizedName"", ""Role"", ""RoomId"",
                    ""UserId"", ""Vote""
                FROM ""Participants""
                WHERE ""RoomId"" IN (SELECT ""Id"" FROM ""Sessions"");");

            migrationBuilder.Sql(@"DROP TABLE ""Participants"";");
            migrationBuilder.Sql(@"ALTER TABLE ""Participants_old"" RENAME TO ""Participants"";");
            migrationBuilder.Sql(@"
                CREATE UNIQUE INDEX ""IX_Participants_SessionId_UserId""
                    ON ""Participants"" (""SessionId"", ""UserId"");");
            migrationBuilder.Sql(@"
                CREATE UNIQUE INDEX ""IX_Participants_SessionId_NormalizedName""
                    ON ""Participants"" (""SessionId"", ""NormalizedName"");");

            migrationBuilder.Sql(@"
                CREATE TABLE ""RoundResult_old"" (
                    ""Id"" TEXT NOT NULL CONSTRAINT ""PK_RoundResult"" PRIMARY KEY,
                    ""Average"" REAL NULL,
                    ""Consensus"" INTEGER NOT NULL,
                    ""FinalEstimate"" TEXT NULL,
                    ""Note"" TEXT NULL,
                    ""RecordedAt"" TEXT NOT NULL,
                    ""SessionId"" TEXT NOT NULL,
                    ""Story"" TEXT NULL,
                    ""VoteCount"" INTEGER NOT NULL,
                    CONSTRAINT ""FK_RoundResult_Sessions_SessionId"" FOREIGN KEY (""SessionId"")
                        REFERENCES ""Sessions"" (""Id"") ON DELETE CASCADE
                );");

            migrationBuilder.Sql(@"
                INSERT INTO ""RoundResult_old"" (
                    ""Id"", ""Average"", ""Consensus"", ""FinalEstimate"", ""Note"", ""RecordedAt"",
                    ""SessionId"", ""Story"", ""VoteCount"")
                SELECT
                    ""Id"", ""Average"", ""Consensus"", ""FinalEstimate"", ""Note"", ""RecordedAt"",
                    ""RoomId"", ""Story"", ""VoteCount""
                FROM ""RoundResult""
                WHERE ""RoomId"" IN (SELECT ""Id"" FROM ""Sessions"");");

            migrationBuilder.Sql(@"DROP TABLE ""RoundResult"";");
            migrationBuilder.Sql(@"ALTER TABLE ""RoundResult_old"" RENAME TO ""RoundResult"";");
            migrationBuilder.Sql(@"CREATE INDEX ""IX_RoundResult_SessionId"" ON ""RoundResult"" (""SessionId"");");

            migrationBuilder.Sql(@"DROP TABLE ""PokerRounds"";");
            migrationBuilder.Sql(@"DROP TABLE ""Rooms"";");
        }
    }
}
