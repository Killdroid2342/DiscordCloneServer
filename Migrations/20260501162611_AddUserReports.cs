using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiscordCloneServer.Migrations
{
    /// <inheritdoc />
    public partial class AddUserReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "User_Reports",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ScopeType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    TargetType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ServerId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ChannelId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    GroupId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    MessageId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    TargetUsername = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ReportedByUsername = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    MessagePreview = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReviewedByUsername = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResolutionNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_User_Reports", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_User_Reports_ReportedByUsername_CreatedAt",
                table: "User_Reports",
                columns: new[] { "ReportedByUsername", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_User_Reports_ServerId_Status_CreatedAt",
                table: "User_Reports",
                columns: new[] { "ServerId", "Status", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "User_Reports");
        }
    }
}
