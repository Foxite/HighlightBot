using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HighlightBot.Migrations
{
    public partial class IgnoreUsers : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HighlightUserIgnoredUser",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserDiscordGuildId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    UserDiscordUserId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    IgnoredUserId = table.Column<decimal>(type: "numeric(20,0)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HighlightUserIgnoredUser", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HighlightUserIgnoredUser_Users_UserDiscordGuildId_UserDisco~",
                        columns: x => new { x.UserDiscordGuildId, x.UserDiscordUserId },
                        principalTable: "Users",
                        principalColumns: new[] { "DiscordGuildId", "DiscordUserId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HighlightUserIgnoredUser_UserDiscordGuildId_UserDiscordUser~",
                table: "HighlightUserIgnoredUser",
                columns: new[] { "UserDiscordGuildId", "UserDiscordUserId" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HighlightUserIgnoredUser");
        }
    }
}
