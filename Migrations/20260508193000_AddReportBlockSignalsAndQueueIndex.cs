using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiscordCloneServer.Migrations
{
    /// <inheritdoc />
    public partial class AddReportBlockSignalsAndQueueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ReporterBlockedTarget",
                table: "User_Reports",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_User_Reports_ServerId_TargetUsername_Status_CreatedAt",
                table: "User_Reports",
                columns: new[] { "ServerId", "TargetUsername", "Status", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_User_Reports_ServerId_TargetUsername_Status_CreatedAt",
                table: "User_Reports");

            migrationBuilder.DropColumn(
                name: "ReporterBlockedTarget",
                table: "User_Reports");
        }
    }
}
