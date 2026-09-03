using ApiGateway.Domain;
using ApiGateway.Management;
using ApiGateway.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ApiGateway.IntegrationTests;

public sealed class CertificateManagementTests
{
    [Theory]
    [InlineData("sjögrässtigen.se")]
    [InlineData("xn--sjgrsstigen-o8a5u.se")]
    public async Task Certificate_binding_accepts_equivalent_internationalized_hosts(string host)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(ct);
        await using var db = new GatewayDbContext(
            new DbContextOptionsBuilder<GatewayDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync(ct);
        var certificate = Certificate("Swedish website");
        certificate.DnsNamesJson = "[\"xn--sjgrsstigen-o8a5u.se\"]";
        db.InboundCertificates.Add(certificate);
        await db.SaveChangesAsync(ct);
        var document = new GatewayConfigDocument
        {
            Routes =
            [
                new GatewayRoute
                {
                    Id = "swedish-website", ClusterId = "upstream",
                    Match = new RouteMatch { Path = "/{**remainder}", Hosts = [host] },
                    Inbound = new InboundRoutePolicy(InboundScheme.HttpsRedirect, certificate.Id)
                }
            ]
        };

        var issues = await new InboundCertificatePublicationValidator(db).ValidateAsync(document, ct);

        Assert.DoesNotContain(issues, issue => issue.Code == "INBOUND_CERTIFICATE_HOST_MISMATCH");
    }

    [Fact]
    public async Task Uploaded_certificate_can_be_renamed_with_concurrency_and_audit()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(ct);
        await using var db = new GatewayDbContext(
            new DbContextOptionsBuilder<GatewayDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync(ct);
        var certificate = Certificate("Original");
        db.InboundCertificates.Add(certificate);
        await db.SaveChangesAsync(ct);
        var previousVersion = certificate.ConcurrencyVersion;
        var keys = Path.Combine(Path.GetTempPath(), "ApiGateway.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var service = new InboundCertificateService(db, new CertificateMaterialProtector(keys));

            var renamed = await service.RenameAsync(certificate.Id, previousVersion,
                "  Public TLS  ", "test", ct);

            Assert.Equal("Public TLS", renamed.Name);
            Assert.NotEqual(previousVersion, renamed.ConcurrencyVersion);
            Assert.Equal("InboundCertificateRenamed", (await db.AuditEvents.SingleAsync(ct)).Action);
        }
        finally
        {
            if (Directory.Exists(keys)) Directory.Delete(keys, true);
        }
    }

    [Fact]
    public async Task Managed_certificate_rename_keeps_the_issued_certificate_name_in_sync()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(ct);
        await using var db = new GatewayDbContext(
            new DbContextOptionsBuilder<GatewayDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync(ct);
        var account = new AcmeAccount
        {
            Name = "Let's Encrypt", DirectoryUrl = "https://acme.example/directory",
            ContactEmail = "admin@example.com", ProtectedAccountKey = [1], TermsAcceptedAtUtc = DateTimeOffset.UtcNow
        };
        var issued = Certificate("Original");
        var managed = new ManagedCertificate
        {
            Name = "Original", AcmeAccount = account, InboundCertificate = issued,
            DnsNamesJson = "[\"example.com\"]", CreatedBy = "test"
        };
        db.ManagedCertificates.Add(managed);
        await db.SaveChangesAsync(ct);
        var previousVersion = managed.ConcurrencyVersion;

        var renamed = await new ManagedCertificateService(db).RenameAsync(managed.Id, previousVersion,
            "Managed TLS", "test", ct);

        Assert.Equal("Managed TLS", renamed.Name);
        Assert.Equal("Managed TLS", renamed.InboundCertificate!.Name);
        Assert.NotEqual(previousVersion, renamed.ConcurrencyVersion);
        Assert.Equal("ManagedCertificateRenamed", (await db.AuditEvents.SingleAsync(ct)).Action);
    }

    private static InboundCertificate Certificate(string name)
    {
        return new InboundCertificate
        {
            Name = name, ProtectedPkcs12 = [1], Thumbprint = "thumbprint", Subject = "CN=example.com",
            NotBeforeUtc = DateTimeOffset.UtcNow.AddDays(-1), NotAfterUtc = DateTimeOffset.UtcNow.AddDays(30),
            CreatedBy = "test"
        };
    }
}