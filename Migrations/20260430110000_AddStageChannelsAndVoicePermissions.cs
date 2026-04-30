using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiscordCloneServer.Migrations
{
    /// <inheritdoc />
    public partial class AddStageChannelsAndVoicePermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "StageSpeakerRestricted",
                table: "Channels",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "StageSpeakerRolesJson",
                table: "Channels",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<bool>(
                name: "VoiceAccessRestricted",
                table: "Channels",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "VoiceAllowedRolesJson",
                table: "Channels",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StageSpeakerRestricted",
                table: "Channels");

            migrationBuilder.DropColumn(
                name: "StageSpeakerRolesJson",
                table: "Channels");

            migrationBuilder.DropColumn(
                name: "VoiceAccessRestricted",
                table: "Channels");

            migrationBuilder.DropColumn(
                name: "VoiceAllowedRolesJson",
                table: "Channels");
        }
    }
}
