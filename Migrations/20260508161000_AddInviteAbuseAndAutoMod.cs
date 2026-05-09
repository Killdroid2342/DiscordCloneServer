using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiscordCloneServer.Migrations
{
    /// <inheritdoc />
    public partial class AddInviteAbuseAndAutoMod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AbuseDetectedAt",
                table: "Server_Invites",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AbuseReason",
                table: "Server_Invites",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastUsedAt",
                table: "Server_Invites",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Server_AutoMod_Rules",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ServerId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    TriggerType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    TriggerValue = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    ActionType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TimesTriggered = table.Column<int>(type: "int", nullable: false),
                    LastTriggeredAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Server_AutoMod_Rules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Server_Invite_Uses",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ServerId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    InviteId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    InviteCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    JoinedUsername = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    IpAddressHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    UsedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    WasBlocked = table.Column<bool>(type: "bit", nullable: false),
                    ReasonCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Server_Invite_Uses", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Server_AutoMod_Rules_ServerId_IsEnabled",
                table: "Server_AutoMod_Rules",
                columns: new[] { "ServerId", "IsEnabled" });

            migrationBuilder.CreateIndex(
                name: "IX_Server_Invite_Uses_InviteId_UsedAt",
                table: "Server_Invite_Uses",
                columns: new[] { "InviteId", "UsedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Server_Invite_Uses_ServerId_IpAddressHash_UsedAt",
                table: "Server_Invite_Uses",
                columns: new[] { "ServerId", "IpAddressHash", "UsedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Server_AutoMod_Rules");

            migrationBuilder.DropTable(
                name: "Server_Invite_Uses");

            migrationBuilder.DropColumn(
                name: "AbuseDetectedAt",
                table: "Server_Invites");

            migrationBuilder.DropColumn(
                name: "AbuseReason",
                table: "Server_Invites");

            migrationBuilder.DropColumn(
                name: "LastUsedAt",
                table: "Server_Invites");
        }
    }
}
