using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiscordCloneServer.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Accounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PassWord = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Friends = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IncomingFriendRequests = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OutgoingFriendRequests = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Accounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Categories",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ServerId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Categories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Channels",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ServerId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CategoryId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Channels", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Create_Server",
                columns: table => new
                {
                    ServerID = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ServerName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ServerOwner = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    InviteLink = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Create_Server", x => x.ServerID);
                });

            migrationBuilder.CreateTable(
                name: "Private_Message_Friend",
                columns: table => new
                {
                    PrivateMessageID = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    FriendMessagesData = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MessageUserReciver = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MessagesUserSender = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Date = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Private_Message_Friend", x => x.PrivateMessageID);
                });

            migrationBuilder.CreateTable(
                name: "Server_Members",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ServerId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Username = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Server_Members", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Server_Message",
                columns: table => new
                {
                    MessageID = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ChannelId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MessagesUserSender = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Date = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    userText = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Server_Message", x => x.MessageID);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Accounts");

            migrationBuilder.DropTable(
                name: "Categories");

            migrationBuilder.DropTable(
                name: "Channels");

            migrationBuilder.DropTable(
                name: "Create_Server");

            migrationBuilder.DropTable(
                name: "Private_Message_Friend");

            migrationBuilder.DropTable(
                name: "Server_Members");

            migrationBuilder.DropTable(
                name: "Server_Message");
        }
    }
}
