using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiscordCloneServer.Migrations
{
    /// <inheritdoc />
    public partial class AddMessagePolls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Message_Poll_Options",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    PollId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Text = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Position = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Message_Poll_Options", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Message_Poll_Votes",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    PollId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    OptionId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Username = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Message_Poll_Votes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Message_Polls",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ScopeType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    MessageId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Question = table.Column<string>(type: "nvarchar(280)", maxLength: 280, nullable: false),
                    AllowMultiple = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Message_Polls", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Message_Poll_Options_PollId_Position",
                table: "Message_Poll_Options",
                columns: new[] { "PollId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Message_Poll_Votes_PollId_OptionId_Username",
                table: "Message_Poll_Votes",
                columns: new[] { "PollId", "OptionId", "Username" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Message_Polls_ScopeType_MessageId",
                table: "Message_Polls",
                columns: new[] { "ScopeType", "MessageId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Message_Poll_Options");

            migrationBuilder.DropTable(
                name: "Message_Poll_Votes");

            migrationBuilder.DropTable(
                name: "Message_Polls");
        }
    }
}
