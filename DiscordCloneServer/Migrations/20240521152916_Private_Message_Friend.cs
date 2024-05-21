using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiscordCloneServer.Migrations
{
    /// <inheritdoc />
    public partial class Private_Message_Friend : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Private_Message_Friend");
        }
    }
}
