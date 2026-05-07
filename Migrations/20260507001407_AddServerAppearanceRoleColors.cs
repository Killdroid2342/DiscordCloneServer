using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiscordCloneServer.Migrations
{
    /// <inheritdoc />
    public partial class AddServerAppearanceRoleColors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Color",
                table: "Server_Roles",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "#949ba4");

            migrationBuilder.Sql("UPDATE Server_Roles SET Color = '#f0b232' WHERE LOWER(Name) = 'owner'");
            migrationBuilder.Sql("UPDATE Server_Roles SET Color = '#ed4245' WHERE LOWER(Name) = 'admin'");
            migrationBuilder.Sql("UPDATE Server_Roles SET Color = '#23a559' WHERE LOWER(Name) = 'moderator'");
            migrationBuilder.Sql("UPDATE Server_Roles SET Color = '#5865f2' WHERE LOWER(Name) = 'user'");

            migrationBuilder.AddColumn<string>(
                name: "ServerBannerUrl",
                table: "Create_Server",
                type: "nvarchar(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ServerIconUrl",
                table: "Create_Server",
                type: "nvarchar(2048)",
                maxLength: 2048,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Color",
                table: "Server_Roles");

            migrationBuilder.DropColumn(
                name: "ServerBannerUrl",
                table: "Create_Server");

            migrationBuilder.DropColumn(
                name: "ServerIconUrl",
                table: "Create_Server");
        }
    }
}
