using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HighlightBot.Migrations
{
    public partial class IgnoreBotsPerUser : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IgnoreBots",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IgnoreBots",
                table: "Users");
        }
    }
}
