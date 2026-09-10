using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TeamTools.Data.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddRetroAnonymity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Anonymous",
                table: "RetroBoard",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Anonymous",
                table: "RetroBoard");
        }
    }
}
