using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApiGateway.Persistence.SqlServer.Migrations
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
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PublishingMode",
                table: "Environments",
                type: "nvarchar(max)",
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
