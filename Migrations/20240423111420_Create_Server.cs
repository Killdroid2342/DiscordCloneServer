using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiscordCloneServer.Migrations
{
    /// <inheritdoc />
    public partial class Create_Server : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Username",
                table: "Accounts",
                newName: "UserName");

            migrationBuilder.RenameColumn(
                name: "Password",
                table: "Accounts",
                newName: "PassWord");

            migrationBuilder.CreateTable(
                name: "Create_Server",
                columns: table => new
                {
                    ServerID = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ServerName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ServerOwner = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Create_Server", x => x.ServerID);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Create_Server");

            migrationBuilder.RenameColumn(
                name: "UserName",
                table: "Accounts",
                newName: "Username");

            migrationBuilder.RenameColumn(
                name: "PassWord",
                table: "Accounts",
                newName: "Password");
        }
    }
}
