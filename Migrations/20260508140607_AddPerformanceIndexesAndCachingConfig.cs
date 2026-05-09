using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiscordCloneServer.Migrations
{
    /// <inheritdoc />
    public partial class AddPerformanceIndexesAndCachingConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "ReplyToMessageId",
                table: "Server_Message",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "MessagesUserSender",
                table: "Server_Message",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "ChannelId",
                table: "Server_Message",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Username",
                table: "Server_Members",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "ServerId",
                table: "Server_Members",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Role",
                table: "Server_Members",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "ServerId",
                table: "Server_Invites",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "ReplyToMessageId",
                table: "Private_Message_Friend",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "MessagesUserSender",
                table: "Private_Message_Friend",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "MessageUserReciver",
                table: "Private_Message_Friend",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Sender",
                table: "GroupMessages",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "ServerOwner",
                table: "Create_Server",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Type",
                table: "Channels",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "ServerId",
                table: "Channels",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Channels",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "CategoryId",
                table: "Channels",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ServerId",
                table: "Categories",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Categories",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "UserName",
                table: "Accounts",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.CreateIndex(
                name: "IX_Server_Thread_Messages_MessagesUserSender",
                table: "Server_Thread_Messages",
                column: "MessagesUserSender");

            migrationBuilder.CreateIndex(
                name: "IX_Server_Message_ChannelId_IsPinned",
                table: "Server_Message",
                columns: new[] { "ChannelId", "IsPinned" });

            migrationBuilder.CreateIndex(
                name: "IX_Server_Message_MessagesUserSender",
                table: "Server_Message",
                column: "MessagesUserSender");

            migrationBuilder.CreateIndex(
                name: "IX_Server_Members_ServerId_Username",
                table: "Server_Members",
                columns: new[] { "ServerId", "Username" });

            migrationBuilder.CreateIndex(
                name: "IX_Server_Members_Username_ServerId",
                table: "Server_Members",
                columns: new[] { "Username", "ServerId" });

            migrationBuilder.CreateIndex(
                name: "IX_Server_Invites_ServerId_RevokedAt_ExpiresAt",
                table: "Server_Invites",
                columns: new[] { "ServerId", "RevokedAt", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Private_Message_Friend_MessagesUserSender_MessageUserReciver",
                table: "Private_Message_Friend",
                columns: new[] { "MessagesUserSender", "MessageUserReciver" });

            migrationBuilder.CreateIndex(
                name: "IX_Private_Message_Friend_MessageUserReciver_MessagesUserSender",
                table: "Private_Message_Friend",
                columns: new[] { "MessageUserReciver", "MessagesUserSender" });

            migrationBuilder.CreateIndex(
                name: "IX_GroupMessages_GroupId",
                table: "GroupMessages",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_GroupMessages_Sender",
                table: "GroupMessages",
                column: "Sender");

            migrationBuilder.CreateIndex(
                name: "IX_Create_Server_ServerOwner",
                table: "Create_Server",
                column: "ServerOwner");

            migrationBuilder.CreateIndex(
                name: "IX_Channels_CategoryId_Position",
                table: "Channels",
                columns: new[] { "CategoryId", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_Channels_ServerId_Position",
                table: "Channels",
                columns: new[] { "ServerId", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_Categories_ServerId_Position",
                table: "Categories",
                columns: new[] { "ServerId", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_Email",
                table: "Accounts",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_UserName",
                table: "Accounts",
                column: "UserName");

            migrationBuilder.CreateIndex(
                name: "IX_Account_Sessions_Username_ExpiresAt_LastSeenAt",
                table: "Account_Sessions",
                columns: new[] { "Username", "ExpiresAt", "LastSeenAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Server_Thread_Messages_MessagesUserSender",
                table: "Server_Thread_Messages");

            migrationBuilder.DropIndex(
                name: "IX_Server_Message_ChannelId_IsPinned",
                table: "Server_Message");

            migrationBuilder.DropIndex(
                name: "IX_Server_Message_MessagesUserSender",
                table: "Server_Message");

            migrationBuilder.DropIndex(
                name: "IX_Server_Members_ServerId_Username",
                table: "Server_Members");

            migrationBuilder.DropIndex(
                name: "IX_Server_Members_Username_ServerId",
                table: "Server_Members");

            migrationBuilder.DropIndex(
                name: "IX_Server_Invites_ServerId_RevokedAt_ExpiresAt",
                table: "Server_Invites");

            migrationBuilder.DropIndex(
                name: "IX_Private_Message_Friend_MessagesUserSender_MessageUserReciver",
                table: "Private_Message_Friend");

            migrationBuilder.DropIndex(
                name: "IX_Private_Message_Friend_MessageUserReciver_MessagesUserSender",
                table: "Private_Message_Friend");

            migrationBuilder.DropIndex(
                name: "IX_GroupMessages_GroupId",
                table: "GroupMessages");

            migrationBuilder.DropIndex(
                name: "IX_GroupMessages_Sender",
                table: "GroupMessages");

            migrationBuilder.DropIndex(
                name: "IX_Create_Server_ServerOwner",
                table: "Create_Server");

            migrationBuilder.DropIndex(
                name: "IX_Channels_CategoryId_Position",
                table: "Channels");

            migrationBuilder.DropIndex(
                name: "IX_Channels_ServerId_Position",
                table: "Channels");

            migrationBuilder.DropIndex(
                name: "IX_Categories_ServerId_Position",
                table: "Categories");

            migrationBuilder.DropIndex(
                name: "IX_Accounts_Email",
                table: "Accounts");

            migrationBuilder.DropIndex(
                name: "IX_Accounts_UserName",
                table: "Accounts");

            migrationBuilder.DropIndex(
                name: "IX_Account_Sessions_Username_ExpiresAt_LastSeenAt",
                table: "Account_Sessions");

            migrationBuilder.AlterColumn<string>(
                name: "ReplyToMessageId",
                table: "Server_Message",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "MessagesUserSender",
                table: "Server_Message",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(256)",
                oldMaxLength: 256);

            migrationBuilder.AlterColumn<string>(
                name: "ChannelId",
                table: "Server_Message",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "Username",
                table: "Server_Members",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(256)",
                oldMaxLength: 256);

            migrationBuilder.AlterColumn<string>(
                name: "ServerId",
                table: "Server_Members",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "Role",
                table: "Server_Members",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(40)",
                oldMaxLength: 40);

            migrationBuilder.AlterColumn<string>(
                name: "ServerId",
                table: "Server_Invites",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "ReplyToMessageId",
                table: "Private_Message_Friend",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "MessagesUserSender",
                table: "Private_Message_Friend",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(256)",
                oldMaxLength: 256);

            migrationBuilder.AlterColumn<string>(
                name: "MessageUserReciver",
                table: "Private_Message_Friend",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(256)",
                oldMaxLength: 256);

            migrationBuilder.AlterColumn<string>(
                name: "Sender",
                table: "GroupMessages",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(256)",
                oldMaxLength: 256);

            migrationBuilder.AlterColumn<string>(
                name: "ServerOwner",
                table: "Create_Server",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(256)",
                oldMaxLength: 256);

            migrationBuilder.AlterColumn<string>(
                name: "Type",
                table: "Channels",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(32)",
                oldMaxLength: 32);

            migrationBuilder.AlterColumn<string>(
                name: "ServerId",
                table: "Channels",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Channels",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(120)",
                oldMaxLength: 120);

            migrationBuilder.AlterColumn<string>(
                name: "CategoryId",
                table: "Channels",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ServerId",
                table: "Categories",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Categories",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(120)",
                oldMaxLength: 120);

            migrationBuilder.AlterColumn<string>(
                name: "UserName",
                table: "Accounts",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(256)",
                oldMaxLength: 256);
        }
    }
}
