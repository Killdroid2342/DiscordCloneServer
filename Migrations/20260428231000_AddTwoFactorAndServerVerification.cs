using System;
using DiscordCloneServer.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiscordCloneServer.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApiContext))]
    [Migration("20260428231000_AddTwoFactorAndServerVerification")]
    public partial class AddTwoFactorAndServerVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AuthenticatorSecretProtected",
                table: "Accounts",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "Accounts",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "SYSUTCDATETIME()");

            migrationBuilder.AddColumn<string>(
                name: "TwoFactorBackupCodeHashes",
                table: "Accounts",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "TwoFactorEnabled",
                table: "Accounts",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "TwoFactorLoginTicketExpiresAt",
                table: "Accounts",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TwoFactorLoginTicketHash",
                table: "Accounts",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "JoinedAt",
                table: "Server_Members",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "SYSUTCDATETIME()");

            migrationBuilder.AddColumn<string>(
                name: "VerificationLevel",
                table: "Create_Server",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "none");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AuthenticatorSecretProtected",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "TwoFactorBackupCodeHashes",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "TwoFactorEnabled",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "TwoFactorLoginTicketExpiresAt",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "TwoFactorLoginTicketHash",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "JoinedAt",
                table: "Server_Members");

            migrationBuilder.DropColumn(
                name: "VerificationLevel",
                table: "Create_Server");
        }
    }
}
