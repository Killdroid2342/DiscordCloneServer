using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiscordCloneServer.Migrations
{
    /// <inheritdoc />
    public partial class CompleteDiscordFeatureSweep : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AttachmentContentType",
                table: "Server_Message",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AttachmentUrl",
                table: "Server_Message",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EditedAt",
                table: "Server_Message",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReplyToMessageId",
                table: "Server_Message",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AttachmentContentType",
                table: "Private_Message_Friend",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AttachmentUrl",
                table: "Private_Message_Friend",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EditedAt",
                table: "Private_Message_Friend",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReplyToMessageId",
                table: "Private_Message_Friend",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AttachmentContentType",
                table: "GroupMessages",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AttachmentUrl",
                table: "GroupMessages",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EditedAt",
                table: "GroupMessages",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReplyToMessageId",
                table: "GroupMessages",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AvatarUrl",
                table: "GroupChats",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "Create_Server",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Position",
                table: "Channels",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Position",
                table: "Categories",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "Message_Reactions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ScopeType = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    MessageId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Emoji = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Username = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Message_Reactions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Server_Bans",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ServerId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Username = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    BannedBy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Server_Bans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Server_Invites",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ServerId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    MaxUses = table.Column<int>(type: "int", nullable: true),
                    Uses = table.Column<int>(type: "int", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Server_Invites", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Server_Roles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ServerId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Position = table.Column<int>(type: "int", nullable: false),
                    CanManageServer = table.Column<bool>(type: "bit", nullable: false),
                    CanManageChannels = table.Column<bool>(type: "bit", nullable: false),
                    CanManageMembers = table.Column<bool>(type: "bit", nullable: false),
                    CanBanMembers = table.Column<bool>(type: "bit", nullable: false),
                    CanCreateInvites = table.Column<bool>(type: "bit", nullable: false),
                    CanSendMessages = table.Column<bool>(type: "bit", nullable: false),
                    CanJoinVoice = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Server_Roles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Unread_States",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Username = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ScopeType = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ScopeId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    LastReadMessageId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastReadAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Unread_States", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Message_Reactions_ScopeType_MessageId_Emoji_Username",
                table: "Message_Reactions",
                columns: new[] { "ScopeType", "MessageId", "Emoji", "Username" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Server_Bans_ServerId_Username",
                table: "Server_Bans",
                columns: new[] { "ServerId", "Username" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Server_Invites_Code",
                table: "Server_Invites",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Server_Roles_ServerId_Name",
                table: "Server_Roles",
                columns: new[] { "ServerId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Unread_States_Username_ScopeType_ScopeId",
                table: "Unread_States",
                columns: new[] { "Username", "ScopeType", "ScopeId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Message_Reactions");

            migrationBuilder.DropTable(
                name: "Server_Bans");

            migrationBuilder.DropTable(
                name: "Server_Invites");

            migrationBuilder.DropTable(
                name: "Server_Roles");

            migrationBuilder.DropTable(
                name: "Unread_States");

            migrationBuilder.DropColumn(
                name: "AttachmentContentType",
                table: "Server_Message");

            migrationBuilder.DropColumn(
                name: "AttachmentUrl",
                table: "Server_Message");

            migrationBuilder.DropColumn(
                name: "EditedAt",
                table: "Server_Message");

            migrationBuilder.DropColumn(
                name: "ReplyToMessageId",
                table: "Server_Message");

            migrationBuilder.DropColumn(
                name: "AttachmentContentType",
                table: "Private_Message_Friend");

            migrationBuilder.DropColumn(
                name: "AttachmentUrl",
                table: "Private_Message_Friend");

            migrationBuilder.DropColumn(
                name: "EditedAt",
                table: "Private_Message_Friend");

            migrationBuilder.DropColumn(
                name: "ReplyToMessageId",
                table: "Private_Message_Friend");

            migrationBuilder.DropColumn(
                name: "AttachmentContentType",
                table: "GroupMessages");

            migrationBuilder.DropColumn(
                name: "AttachmentUrl",
                table: "GroupMessages");

            migrationBuilder.DropColumn(
                name: "EditedAt",
                table: "GroupMessages");

            migrationBuilder.DropColumn(
                name: "ReplyToMessageId",
                table: "GroupMessages");

            migrationBuilder.DropColumn(
                name: "AvatarUrl",
                table: "GroupChats");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "Create_Server");

            migrationBuilder.DropColumn(
                name: "Position",
                table: "Channels");

            migrationBuilder.DropColumn(
                name: "Position",
                table: "Categories");
        }
    }
}
