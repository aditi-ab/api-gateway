using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApiGateway.Persistence.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class ConfigurationPublishing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PendingRevisionId",
                table: "Environments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PublishingMode",
                table: "Environments",
                type: "TEXT",
                nullable: false,
                defaultValue: "Immediate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PendingRevisionId",
                table: "Environments");

            migrationBuilder.DropColumn(
                name: "PublishingMode",
                table: "Environments");
        }
    }
}
