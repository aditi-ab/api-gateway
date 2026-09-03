using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApiGateway.Persistence.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class LocalUsersInboundTls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ConcurrencyVersion",
                table: "LocalAdministrators",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<bool>(
                name: "Enabled",
                table: "LocalAdministrators",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "MustChangePassword",
                table: "LocalAdministrators",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "RolesJson",
                table: "LocalAdministrators",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: false,
                defaultValue: "[\"Administrator\"]");

            migrationBuilder.CreateTable(
                name: "InboundCertificates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ProtectedPkcs12 = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    Thumbprint = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    DnsNamesJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    NotBeforeUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    NotAfterUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ConcurrencyVersion = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InboundCertificates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InboundSecuritySettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HstsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    HstsHostsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    HstsMaxAgeSeconds = table.Column<int>(type: "int", nullable: false),
                    HstsIncludeSubDomains = table.Column<bool>(type: "bit", nullable: false),
                    HstsPreload = table.Column<bool>(type: "bit", nullable: false),
                    ConcurrencyVersion = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InboundSecuritySettings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InboundCertificates_Name",
                table: "InboundCertificates",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InboundCertificates");

            migrationBuilder.DropTable(
                name: "InboundSecuritySettings");

            migrationBuilder.DropColumn(
                name: "ConcurrencyVersion",
                table: "LocalAdministrators");

            migrationBuilder.DropColumn(
                name: "Enabled",
                table: "LocalAdministrators");

            migrationBuilder.DropColumn(
                name: "MustChangePassword",
                table: "LocalAdministrators");

            migrationBuilder.DropColumn(
                name: "RolesJson",
                table: "LocalAdministrators");
        }
    }
}
