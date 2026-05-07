using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiscordCloneServer.Migrations
{
    /// <inheritdoc />
    public partial class AddChannelPermissionsAndServerTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MessageSendAllowedRolesJson",
                table: "Channels",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<bool>(
                name: "MessageSendRestricted",
                table: "Channels",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ViewAccessRestricted",
                table: "Channels",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ViewAllowedRolesJson",
                table: "Channels",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MessageSendAllowedRolesJson",
                table: "Channels");

            migrationBuilder.DropColumn(
                name: "MessageSendRestricted",
                table: "Channels");

            migrationBuilder.DropColumn(
                name: "ViewAccessRestricted",
                table: "Channels");

            migrationBuilder.DropColumn(
                name: "ViewAllowedRolesJson",
                table: "Channels");
        }
    }
}
