using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HighlightBot.Migrations
{
    public partial class IgnoredChannels : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HighlightUserIgnoredChannel",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserDiscordGuildId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    UserDiscordUserId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    ChannelId = table.Column<decimal>(type: "numeric(20,0)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HighlightUserIgnoredChannel", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HighlightUserIgnoredChannel_Users_UserDiscordGuildId_UserDi~",
                        columns: x => new { x.UserDiscordGuildId, x.UserDiscordUserId },
                        principalTable: "Users",
                        principalColumns: new[] { "DiscordGuildId", "DiscordUserId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HighlightUserIgnoredChannel_UserDiscordGuildId_UserDiscordU~",
                table: "HighlightUserIgnoredChannel",
                columns: new[] { "UserDiscordGuildId", "UserDiscordUserId" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HighlightUserIgnoredChannel");
        }
    }
}
