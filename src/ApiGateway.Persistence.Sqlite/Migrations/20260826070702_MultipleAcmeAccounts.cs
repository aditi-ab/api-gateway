using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApiGateway.Persistence.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class MultipleAcmeAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AcmeAccountId",
                table: "ManagedCertificates",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDefault",
                table: "AcmeAccounts",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsStaging",
                table: "AcmeAccounts",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "AcmeAccounts",
                type: "TEXT",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("""
                UPDATE AcmeAccounts
                SET Name = CASE WHEN lower(DirectoryUrl) LIKE '%staging%'
                                THEN 'Let''s Encrypt Staging'
                                ELSE 'Let''s Encrypt Production' END,
                    IsStaging = CASE WHEN lower(DirectoryUrl) LIKE '%staging%' THEN 1 ELSE 0 END,
                    IsDefault = 1;
                UPDATE ManagedCertificates
                SET AcmeAccountId = (SELECT Id FROM AcmeAccounts ORDER BY IsDefault DESC LIMIT 1);
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "AcmeAccountId",
                table: "ManagedCertificates",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ManagedCertificates_AcmeAccountId",
                table: "ManagedCertificates",
                column: "AcmeAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_AcmeAccounts_DirectoryUrl",
                table: "AcmeAccounts",
                column: "DirectoryUrl",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcmeAccounts_Name",
                table: "AcmeAccounts",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ManagedCertificates_AcmeAccounts_AcmeAccountId",
                table: "ManagedCertificates",
                column: "AcmeAccountId",
                principalTable: "AcmeAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ManagedCertificates_AcmeAccounts_AcmeAccountId",
                table: "ManagedCertificates");

            migrationBuilder.DropIndex(
                name: "IX_ManagedCertificates_AcmeAccountId",
                table: "ManagedCertificates");

            migrationBuilder.DropIndex(
                name: "IX_AcmeAccounts_DirectoryUrl",
                table: "AcmeAccounts");

            migrationBuilder.DropIndex(
                name: "IX_AcmeAccounts_Name",
                table: "AcmeAccounts");

            migrationBuilder.DropColumn(
                name: "AcmeAccountId",
                table: "ManagedCertificates");

            migrationBuilder.DropColumn(
                name: "IsDefault",
                table: "AcmeAccounts");

            migrationBuilder.DropColumn(
                name: "IsStaging",
                table: "AcmeAccounts");

            migrationBuilder.DropColumn(
                name: "Name",
                table: "AcmeAccounts");
        }
    }
}
