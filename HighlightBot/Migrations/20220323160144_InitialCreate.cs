using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HighlightBot.Migrations
{
    public partial class InitialCreate : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    DiscordGuildId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    DiscordUserId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    LastActivity = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    HighlightDelay = table.Column<TimeSpan>(type: "interval", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => new { x.DiscordGuildId, x.DiscordUserId });
                });

            migrationBuilder.CreateTable(
                name: "Terms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_serverid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    user_userid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Terms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Terms_Users_user_serverid_user_userid",
                        columns: x => new { x.user_serverid, x.user_userid },
                        principalTable: "Users",
                        principalColumns: new[] { "DiscordGuildId", "DiscordUserId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Terms_user_serverid_user_userid",
                table: "Terms",
                columns: new[] { "user_serverid", "user_userid" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Terms");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
