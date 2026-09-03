using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApiGateway.Persistence.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class RouteFirstManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ChangeKind",
                table: "Revisions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ChangeSummary",
                table: "Revisions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ChangedResourceId",
                table: "Revisions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ChangedResourceType",
                table: "Revisions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ParentRevisionId",
                table: "Revisions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RevertsRevisionId",
                table: "Revisions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.Sql("UPDATE Revisions SET State = 'Abandoned' WHERE State = 'Draft';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE Revisions SET State = 'Draft' WHERE State = 'Abandoned';");

            migrationBuilder.DropColumn(
                name: "ChangeKind",
                table: "Revisions");

            migrationBuilder.DropColumn(
                name: "ChangeSummary",
                table: "Revisions");

            migrationBuilder.DropColumn(
                name: "ChangedResourceId",
                table: "Revisions");

            migrationBuilder.DropColumn(
                name: "ChangedResourceType",
                table: "Revisions");

            migrationBuilder.DropColumn(
                name: "ParentRevisionId",
                table: "Revisions");

            migrationBuilder.DropColumn(
                name: "RevertsRevisionId",
                table: "Revisions");
        }
    }
}
