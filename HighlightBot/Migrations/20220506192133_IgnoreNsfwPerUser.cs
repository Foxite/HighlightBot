using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HighlightBot.Migrations
{
    public partial class IgnoreNsfwPerUser : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IgnoreNsfw",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IgnoreNsfw",
                table: "Users");
        }
    }
}
