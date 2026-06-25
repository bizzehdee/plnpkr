using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlanningPoker.Data.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddRoundResults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RoundResult",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Story = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    FinalEstimate = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    Average = table.Column<double>(type: "double precision", nullable: true),
                    Consensus = table.Column<bool>(type: "boolean", nullable: false),
                    VoteCount = table.Column<int>(type: "integer", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoundResult", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RoundResult_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RoundResult_SessionId",
                table: "RoundResult",
                column: "SessionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RoundResult");
        }
    }
}
