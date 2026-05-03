using System;
using DiscordCloneServer.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiscordCloneServer.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApiContext))]
    [Migration("20260502223000_AddServerWelcomeOnboarding")]
    public partial class AddServerWelcomeOnboarding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "WelcomeEnabled",
                table: "Create_Server",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "WelcomeMessage",
                table: "Create_Server",
                type: "nvarchar(600)",
                maxLength: 600,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WelcomeChecklistJson",
                table: "Create_Server",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OnboardingCompletedAt",
                table: "Server_Members",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WelcomeEnabled",
                table: "Create_Server");

            migrationBuilder.DropColumn(
                name: "WelcomeMessage",
                table: "Create_Server");

            migrationBuilder.DropColumn(
                name: "WelcomeChecklistJson",
                table: "Create_Server");

            migrationBuilder.DropColumn(
                name: "OnboardingCompletedAt",
                table: "Server_Members");
        }
    }
}
