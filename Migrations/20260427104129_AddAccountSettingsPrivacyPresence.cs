using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiscordCloneServer.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountSettingsPrivacyPresence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BlockedUsers",
                table: "Accounts",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "Accounts",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PhoneNumber",
                table: "Accounts",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PresenceStatus",
                table: "Accounts",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "online");

            migrationBuilder.AddColumn<bool>(
                name: "PrivacyAllowFriendRequestsEveryone",
                table: "Accounts",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "PrivacyAllowFriendRequestsFriendsOfFriends",
                table: "Accounts",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "PrivacyAllowFriendRequestsServerMembers",
                table: "Accounts",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "PrivacyDmPolicy",
                table: "Accounts",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "friends");

            migrationBuilder.AddColumn<bool>(
                name: "PrivacyShowActivity",
                table: "Accounts",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "ProfileBannerColor",
                table: "Accounts",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProfileBannerUrl",
                table: "Accounts",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SettingsJson",
                table: "Accounts",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<string>(
                name: "VoiceChangerSettingsJson",
                table: "Accounts",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "{}");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BlockedUsers",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "PhoneNumber",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "PresenceStatus",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "PrivacyAllowFriendRequestsEveryone",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "PrivacyAllowFriendRequestsFriendsOfFriends",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "PrivacyAllowFriendRequestsServerMembers",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "PrivacyDmPolicy",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "PrivacyShowActivity",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "ProfileBannerColor",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "ProfileBannerUrl",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "SettingsJson",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "VoiceChangerSettingsJson",
                table: "Accounts");
        }
    }
}
