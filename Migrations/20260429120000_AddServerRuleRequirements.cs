using DiscordCloneServer.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiscordCloneServer.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApiContext))]
    [Migration("20260429120000_AddServerRuleRequirements")]
    public partial class AddServerRuleRequirements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MinimumAccountAgeMinutes",
                table: "Create_Server",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MinimumMembershipMinutes",
                table: "Create_Server",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "RequireVerifiedEmail",
                table: "Create_Server",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MinimumAccountAgeMinutes",
                table: "Create_Server");

            migrationBuilder.DropColumn(
                name: "MinimumMembershipMinutes",
                table: "Create_Server");

            migrationBuilder.DropColumn(
                name: "RequireVerifiedEmail",
                table: "Create_Server");
        }
    }
}
