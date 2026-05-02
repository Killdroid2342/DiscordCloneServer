using DiscordCloneServer.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiscordCloneServer.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApiContext))]
    [Migration("20260501233000_AddServerDiscovery")]
    public partial class AddServerDiscovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DiscoveryCategory",
                table: "Create_Server",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsPublic",
                table: "Create_Server",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Create_Server_IsPublic_DiscoveryCategory",
                table: "Create_Server",
                columns: new[] { "IsPublic", "DiscoveryCategory" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Create_Server_IsPublic_DiscoveryCategory",
                table: "Create_Server");

            migrationBuilder.DropColumn(
                name: "DiscoveryCategory",
                table: "Create_Server");

            migrationBuilder.DropColumn(
                name: "IsPublic",
                table: "Create_Server");
        }
    }
}
