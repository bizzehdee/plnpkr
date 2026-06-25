using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlanningPoker.Data.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddCurrentStoryNote : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CurrentStoryNote",
                table: "Sessions",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CurrentStoryNote",
                table: "Sessions");
        }
    }
}
