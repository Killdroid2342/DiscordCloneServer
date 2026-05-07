using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiscordCloneServer.Migrations
{
    /// <inheritdoc />
    public partial class AddSlashCommandsAndOAuthApps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OAuth_Access_Tokens",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ApplicationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    AuthorizationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Username = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ServerId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    TokenHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ScopesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OAuth_Access_Tokens", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OAuth_App_Authorizations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ApplicationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Username = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ServerId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ScopesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OAuth_App_Authorizations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OAuth_Applications",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(240)", maxLength: 240, nullable: true),
                    IconUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    OwnerUsername = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ClientSecretHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RedirectUrisJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AllowedScopesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BotAccountId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SecretLastRotatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OAuth_Applications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OAuth_Authorization_Codes",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ApplicationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    AuthorizationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Username = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ServerId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    CodeHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RedirectUri = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    ScopesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ConsumedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OAuth_Authorization_Codes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Slash_Command_Interactions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    SlashCommandId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ServerId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ChannelId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    BotAccountId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CommandName = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    InvokedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Arguments = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AcknowledgedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RespondedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResponseMessageId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Slash_Command_Interactions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Slash_Commands",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ServerId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    BotAccountId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Usage = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Slash_Commands", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OAuth_Access_Tokens_ApplicationId_Username_ExpiresAt",
                table: "OAuth_Access_Tokens",
                columns: new[] { "ApplicationId", "Username", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OAuth_Access_Tokens_TokenHash",
                table: "OAuth_Access_Tokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OAuth_App_Authorizations_ApplicationId_Username_ServerId",
                table: "OAuth_App_Authorizations",
                columns: new[] { "ApplicationId", "Username", "ServerId" });

            migrationBuilder.CreateIndex(
                name: "IX_OAuth_Applications_OwnerUsername",
                table: "OAuth_Applications",
                column: "OwnerUsername");

            migrationBuilder.CreateIndex(
                name: "IX_OAuth_Authorization_Codes_ApplicationId_ExpiresAt",
                table: "OAuth_Authorization_Codes",
                columns: new[] { "ApplicationId", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OAuth_Authorization_Codes_CodeHash",
                table: "OAuth_Authorization_Codes",
                column: "CodeHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Slash_Command_Interactions_BotAccountId_Status_CreatedAt",
                table: "Slash_Command_Interactions",
                columns: new[] { "BotAccountId", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Slash_Command_Interactions_ServerId_ChannelId_CreatedAt",
                table: "Slash_Command_Interactions",
                columns: new[] { "ServerId", "ChannelId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Slash_Commands_BotAccountId_IsEnabled",
                table: "Slash_Commands",
                columns: new[] { "BotAccountId", "IsEnabled" });

            migrationBuilder.CreateIndex(
                name: "IX_Slash_Commands_ServerId_Name",
                table: "Slash_Commands",
                columns: new[] { "ServerId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OAuth_Access_Tokens");

            migrationBuilder.DropTable(
                name: "OAuth_App_Authorizations");

            migrationBuilder.DropTable(
                name: "OAuth_Applications");

            migrationBuilder.DropTable(
                name: "OAuth_Authorization_Codes");

            migrationBuilder.DropTable(
                name: "Slash_Command_Interactions");

            migrationBuilder.DropTable(
                name: "Slash_Commands");
        }
    }
}
