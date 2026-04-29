using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiscordCloneServer.Migrations
{
    /// <inheritdoc />
    public partial class AddContactVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "EmailVerifiedAt",
                table: "Accounts",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PhoneNumberVerifiedAt",
                table: "Accounts",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Contact_Verifications",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Username = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Target = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    CodeHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ConsumedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Contact_Verifications", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Contact_Verifications_Username_Kind_ConsumedAt",
                table: "Contact_Verifications",
                columns: new[] { "Username", "Kind", "ConsumedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Contact_Verifications");

            migrationBuilder.DropColumn(
                name: "EmailVerifiedAt",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "PhoneNumberVerifiedAt",
                table: "Accounts");
        }
    }
}
