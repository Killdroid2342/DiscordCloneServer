using DiscordCloneServer.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiscordCloneServer.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApiContext))]
    [Migration("20260429170000_AddModeratorTwoFactorRequirement")]
    public partial class AddModeratorTwoFactorRequirement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RequireTwoFactorForModerators",
                table: "Create_Server",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RequireTwoFactorForModerators",
                table: "Create_Server");
        }
    }
}
