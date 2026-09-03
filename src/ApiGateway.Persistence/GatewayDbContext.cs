using ApiGateway.Domain;
using Microsoft.EntityFrameworkCore;

namespace ApiGateway.Persistence;

public sealed class GatewayDbContext(DbContextOptions<GatewayDbContext> options) : DbContext(options)
{
    public DbSet<GatewayEnvironment> Environments => Set<GatewayEnvironment>();
    public DbSet<ConfigRevision> Revisions => Set<ConfigRevision>();
    public DbSet<GatewayInstance> Instances => Set<GatewayInstance>();
    public DbSet<GatewayActivationEvent> ActivationEvents => Set<GatewayActivationEvent>();
    public DbSet<ManagementApiKey> ManagementApiKeys => Set<ManagementApiKey>();
    public DbSet<ConsumerApiKey> ConsumerApiKeys => Set<ConsumerApiKey>();
    public DbSet<LocalAdministrator> LocalAdministrators => Set<LocalAdministrator>();
    public DbSet<AdminIdentityProviderDocument> AdminIdentityProviders => Set<AdminIdentityProviderDocument>();
    public DbSet<EntraConnectionSettings> EntraConnectionSettings => Set<EntraConnectionSettings>();
    public DbSet<InboundCertificate> InboundCertificates => Set<InboundCertificate>();
    public DbSet<AcmeAccount> AcmeAccounts => Set<AcmeAccount>();
    public DbSet<DnsProviderProfile> DnsProviderProfiles => Set<DnsProviderProfile>();
    public DbSet<ManagedCertificate> ManagedCertificates => Set<ManagedCertificate>();
    public DbSet<AcmeOrder> AcmeOrders => Set<AcmeOrder>();
    public DbSet<AcmeChallenge> AcmeChallenges => Set<AcmeChallenge>();
    public DbSet<InboundSecuritySettings> InboundSecuritySettings => Set<InboundSecuritySettings>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<MaintenanceLease> MaintenanceLeases => Set<MaintenanceLease>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<GatewayEnvironment>(x =>
        {
            x.HasIndex(e => e.Slug).IsUnique();
            x.Property(e => e.PublishingMode).HasConversion<string>();
            x.Property(e => e.ConcurrencyVersion).IsConcurrencyToken();
        });
        model.Entity<ConfigRevision>(x =>
        {
            x.HasIndex(e => new { e.EnvironmentId, e.Number }).IsUnique();
            x.Property(e => e.State).HasConversion<string>();
            x.Property(e => e.ConcurrencyVersion).IsConcurrencyToken();
        });
        model.Entity<GatewayInstance>(x => x.HasIndex(e => new { e.EnvironmentId, e.InstanceId }).IsUnique());
        model.Entity<GatewayActivationEvent>(x =>
        {
            x.Property(e => e.Id).ValueGeneratedOnAdd();
            x.Property(e => e.Outcome).HasConversion<string>();
        });
        model.Entity<ManagementApiKey>(x =>
        {
            x.HasIndex(e => e.Name).IsUnique();
            x.HasIndex(e => e.KeyHash).IsUnique();
        });
        model.Entity<ConsumerApiKey>(x =>
        {
            x.HasIndex(e => e.Name).IsUnique();
            x.HasIndex(e => e.KeyHash).IsUnique();
        });
        model.Entity<LocalAdministrator>(x =>
        {
            x.HasIndex(e => e.NormalizedUsername).IsUnique();
            x.Property(e => e.DisplayName).HasMaxLength(200);
            x.Property(e => e.RolesJson).HasMaxLength(1000);
            x.Property(e => e.ConcurrencyVersion).IsConcurrencyToken();
        });
        model.Entity<AdminIdentityProviderDocument>(x =>
        {
            x.HasKey(e => e.Id);
            x.Property(e => e.Id).HasMaxLength(100);
        });
        model.Entity<EntraConnectionSettings>(x =>
        {
            x.Property(e => e.Authority).HasMaxLength(1000);
            x.Property(e => e.Audience).HasMaxLength(500);
            x.Property(e => e.ClientId).HasMaxLength(100);
            x.Property(e => e.Scope).HasMaxLength(1000);
            x.Property(e => e.ConcurrencyVersion).IsConcurrencyToken();
        });
        model.Entity<InboundCertificate>(x =>
        {
            x.HasIndex(e => e.Name).IsUnique();
            x.Property(e => e.Name).HasMaxLength(200);
            x.Property(e => e.Thumbprint).HasMaxLength(128);
            x.Property(e => e.Subject).HasMaxLength(1000);
            x.Property(e => e.DnsNamesJson).HasMaxLength(4000);
            x.Property(e => e.ConcurrencyVersion).IsConcurrencyToken();
        });
        model.Entity<AcmeAccount>(x =>
        {
            x.HasIndex(e => e.DirectoryUrl).IsUnique();
            x.HasIndex(e => e.Name).IsUnique();
            x.Property(e => e.Name).HasMaxLength(200);
            x.Property(e => e.DirectoryUrl).HasMaxLength(1000);
            x.Property(e => e.ContactEmail).HasMaxLength(320);
            x.Property(e => e.AccountUrl).HasMaxLength(1000);
            x.Property(e => e.TermsOfServiceUrl).HasMaxLength(1000);
            x.Property(e => e.ConcurrencyVersion).IsConcurrencyToken();
        });
        model.Entity<DnsProviderProfile>(x =>
        {
            x.HasIndex(e => e.Name).IsUnique();
            x.Property(e => e.Name).HasMaxLength(200);
            x.Property(e => e.Provider).HasConversion<string>();
            x.Property(e => e.ManagedZonesJson).HasMaxLength(8000);
            x.Property(e => e.ConcurrencyVersion).IsConcurrencyToken();
        });
        model.Entity<ManagedCertificate>(x =>
        {
            x.HasIndex(e => e.Name).IsUnique();
            x.HasIndex(e => e.InboundCertificateId).IsUnique();
            x.Property(e => e.Name).HasMaxLength(200);
            x.Property(e => e.DnsNamesJson).HasMaxLength(4000);
            x.Property(e => e.ChallengeKind).HasConversion<string>();
            x.Property(e => e.State).HasConversion<string>();
            x.Property(e => e.LastErrorCode).HasMaxLength(100);
            x.Property(e => e.LastErrorMessage).HasMaxLength(1000);
            x.Property(e => e.ConcurrencyVersion).IsConcurrencyToken();
            x.HasOne(e => e.AcmeAccount).WithMany().HasForeignKey(e => e.AcmeAccountId)
                .OnDelete(DeleteBehavior.Restrict);
            x.HasOne(e => e.InboundCertificate).WithOne().HasForeignKey<ManagedCertificate>(e => e.InboundCertificateId)
                .OnDelete(DeleteBehavior.Restrict);
            x.HasOne(e => e.DnsProviderProfile).WithMany().HasForeignKey(e => e.DnsProviderProfileId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<AcmeOrder>(x =>
        {
            x.Property(e => e.OrderUrl).HasMaxLength(1000);
            x.Property(e => e.State).HasMaxLength(50);
            x.HasOne(e => e.ManagedCertificate).WithMany().HasForeignKey(e => e.ManagedCertificateId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<AcmeChallenge>(x =>
        {
            x.Property(e => e.Kind).HasConversion<string>();
            x.Property(e => e.Host).HasMaxLength(253);
            x.Property(e => e.Token).HasMaxLength(512);
            x.Property(e => e.KeyAuthorization).HasMaxLength(1000);
            x.Property(e => e.DnsRecordName).HasMaxLength(253);
            x.Property(e => e.DnsRecordValue).HasMaxLength(1000);
            x.Property(e => e.ProviderRecordId).HasMaxLength(1000);
            x.HasIndex(e => new { e.Token, e.ExpiresAtUtc });
            x.HasOne(e => e.AcmeOrder).WithMany().HasForeignKey(e => e.AcmeOrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<InboundSecuritySettings>(x => x.Property(e => e.ConcurrencyVersion).IsConcurrencyToken());
        model.Entity<AuditEvent>(x => x.Property(e => e.Id).ValueGeneratedOnAdd());
        model.Entity<MaintenanceLease>(x =>
        {
            x.HasKey(e => e.JobName);
            x.Property(e => e.ConcurrencyVersion).IsConcurrencyToken();
        });
    }
}
