using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiscordCloneServer.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountTrustStandingActivity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AccountStanding",
                table: "Accounts",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "good");

            migrationBuilder.AddColumn<string>(
                name: "ActivityStatus",
                table: "Accounts",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastActiveAt",
                table: "Accounts",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StandingReason",
                table: "Accounts",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "StandingUpdatedAt",
                table: "Accounts",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TrustScore",
                table: "Accounts",
                type: "int",
                nullable: false,
                defaultValue: 60);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AccountStanding",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "ActivityStatus",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "LastActiveAt",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "StandingReason",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "StandingUpdatedAt",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "TrustScore",
                table: "Accounts");
        }
    }
}
