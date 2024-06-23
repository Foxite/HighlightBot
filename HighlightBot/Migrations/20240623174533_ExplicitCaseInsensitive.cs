using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HighlightBot.Migrations
{
    public partial class ExplicitCaseInsensitive : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RegexOptions",
                table: "Terms");

            migrationBuilder.AddColumn<bool>(
                name: "IsCaseSensitive",
                table: "Terms",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsCaseSensitive",
                table: "Terms");

            migrationBuilder.AddColumn<int>(
                name: "RegexOptions",
                table: "Terms",
                type: "integer",
                nullable: false,
                defaultValue: 1);
        }
    }
}
