using System;
using DiscordCloneServer.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiscordCloneServer.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApiContext))]
    [Migration("20260506123000_AddServerThreadsAndPinnedMessages")]
    public partial class AddServerThreadsAndPinnedMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPinned",
                table: "Server_Message",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "PinnedAt",
                table: "Server_Message",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PinnedBy",
                table: "Server_Message",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Server_Threads",
                columns: table => new
                {
                    ThreadId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ServerId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ChannelId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ParentMessageId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastActivityAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Server_Threads", x => x.ThreadId);
                });

            migrationBuilder.CreateTable(
                name: "Server_Thread_Messages",
                columns: table => new
                {
                    ThreadMessageId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ThreadId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    MessagesUserSender = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Date = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    userText = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AttachmentUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AttachmentContentType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EditedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Server_Thread_Messages", x => x.ThreadMessageId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Server_Thread_Messages_ThreadId",
                table: "Server_Thread_Messages",
                column: "ThreadId");

            migrationBuilder.CreateIndex(
                name: "IX_Server_Threads_ChannelId_LastActivityAt",
                table: "Server_Threads",
                columns: new[] { "ChannelId", "LastActivityAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Server_Threads_ParentMessageId",
                table: "Server_Threads",
                column: "ParentMessageId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Server_Thread_Messages");

            migrationBuilder.DropTable(
                name: "Server_Threads");

            migrationBuilder.DropColumn(
                name: "IsPinned",
                table: "Server_Message");

            migrationBuilder.DropColumn(
                name: "PinnedAt",
                table: "Server_Message");

            migrationBuilder.DropColumn(
                name: "PinnedBy",
                table: "Server_Message");
        }
    }
}
