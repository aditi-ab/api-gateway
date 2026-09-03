using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApiGateway.Persistence.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AcmeCertificateAutomation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AcmeAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DirectoryUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    ContactEmail = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    ProtectedAccountKey = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    AccountUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    TermsOfServiceUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    TermsAcceptedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ConcurrencyVersion = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcmeAccounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DnsProviderProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ProtectedCredentials = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    ManagedZonesJson = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    ConcurrencyVersion = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DnsProviderProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ManagedCertificates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InboundCertificateId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DnsNamesJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    ChallengeKind = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DnsProviderProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    State = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FailedAttemptCount = table.Column<int>(type: "int", nullable: false),
                    LastErrorCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LastErrorMessage = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    LastAttemptAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastSuccessAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    AriWindowStartUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    AriWindowEndUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastAriCheckAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    NextAttemptAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ConcurrencyVersion = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManagedCertificates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ManagedCertificates_DnsProviderProfiles_DnsProviderProfileId",
                        column: x => x.DnsProviderProfileId,
                        principalTable: "DnsProviderProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ManagedCertificates_InboundCertificates_InboundCertificateId",
                        column: x => x.InboundCertificateId,
                        principalTable: "InboundCertificates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AcmeOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ManagedCertificateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ProtectedCertificateKey = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    State = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcmeOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AcmeOrders_ManagedCertificates_ManagedCertificateId",
                        column: x => x.ManagedCertificateId,
                        principalTable: "ManagedCertificates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AcmeChallenges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AcmeOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Host = table.Column<string>(type: "nvarchar(253)", maxLength: 253, nullable: false),
                    Token = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    KeyAuthorization = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DnsRecordName = table.Column<string>(type: "nvarchar(253)", maxLength: 253, nullable: true),
                    DnsRecordValue = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ProviderRecordId = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcmeChallenges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AcmeChallenges_AcmeOrders_AcmeOrderId",
                        column: x => x.AcmeOrderId,
                        principalTable: "AcmeOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AcmeChallenges_AcmeOrderId",
                table: "AcmeChallenges",
                column: "AcmeOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_AcmeChallenges_Token_ExpiresAtUtc",
                table: "AcmeChallenges",
                columns: new[] { "Token", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AcmeOrders_ManagedCertificateId",
                table: "AcmeOrders",
                column: "ManagedCertificateId");

            migrationBuilder.CreateIndex(
                name: "IX_DnsProviderProfiles_Name",
                table: "DnsProviderProfiles",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ManagedCertificates_DnsProviderProfileId",
                table: "ManagedCertificates",
                column: "DnsProviderProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_ManagedCertificates_InboundCertificateId",
                table: "ManagedCertificates",
                column: "InboundCertificateId",
                unique: true,
                filter: "[InboundCertificateId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ManagedCertificates_Name",
                table: "ManagedCertificates",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AcmeAccounts");

            migrationBuilder.DropTable(
                name: "AcmeChallenges");

            migrationBuilder.DropTable(
                name: "AcmeOrders");

            migrationBuilder.DropTable(
                name: "ManagedCertificates");

            migrationBuilder.DropTable(
                name: "DnsProviderProfiles");
        }
    }
}
