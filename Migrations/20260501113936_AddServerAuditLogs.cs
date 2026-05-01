using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiscordCloneServer.Migrations
{
    /// <inheritdoc />
    public partial class AddServerAuditLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Server_Audit_Logs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ServerId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ActionType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ActorUsername = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    TargetType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    TargetId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    TargetUsername = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    DetailsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Server_Audit_Logs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Server_Audit_Logs_ServerId_CreatedAt",
                table: "Server_Audit_Logs",
                columns: new[] { "ServerId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Server_Audit_Logs");
        }
    }
}
