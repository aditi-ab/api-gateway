namespace ApiGateway.Domain;

public enum RevisionState
{
    Draft,
    Published,
    Abandoned
}

public enum ConfigurationPublishingMode
{
    Immediate,
    Staged
}

public enum ActivationOutcome
{
    Succeeded,
    Failed
}

public sealed class GatewayEnvironment
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public required string Slug { get; set; }
    public required string DisplayName { get; set; }
    public string? Description { get; set; }
    public Guid? ActiveRevisionId { get; set; }
    public Guid? PendingRevisionId { get; set; }
    public ConfigurationPublishingMode PublishingMode { get; set; } = ConfigurationPublishingMode.Immediate;
    public DateTimeOffset? ArchivedAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public Guid ConcurrencyVersion { get; set; } = Guid.NewGuid();
}

public sealed class ConfigRevision
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid EnvironmentId { get; set; }
    public long Number { get; set; }
    public RevisionState State { get; set; }
    public required string ConfigJson { get; set; }
    public required string ContentHash { get; set; }
    public Guid ConcurrencyVersion { get; set; } = Guid.NewGuid();
    public string? Comment { get; set; }
    public required string CreatedBy { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public string? PublishedBy { get; set; }
    public DateTimeOffset? PublishedAtUtc { get; set; }
    public Guid? ParentRevisionId { get; set; }
    public string? ChangeKind { get; set; }
    public string? ChangeSummary { get; set; }
    public string? ChangedResourceType { get; set; }
    public string? ChangedResourceId { get; set; }
    public Guid? RevertsRevisionId { get; set; }
}

public sealed class GatewayInstance
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid EnvironmentId { get; set; }
    public required string InstanceId { get; set; }
    public required string DisplayName { get; set; }
    public string? AdvertisedAddress { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset LastHeartbeatAtUtc { get; set; }
    public Guid? ActivatedRevisionId { get; set; }
    public string? ActivatedContentHash { get; set; }
    public string? LastActivationStatus { get; set; }
    public DateTimeOffset? LastActivationAtUtc { get; set; }
    public string? LastActivationErrorCode { get; set; }
    public DateTimeOffset? StoppedAtUtc { get; set; }
    public required string RuntimeVersion { get; set; }
    public string ActiveRouteRequestsJson { get; set; } = "{}";
}

public sealed class GatewayActivationEvent
{
    public long Id { get; set; }
    public Guid EnvironmentId { get; set; }
    public required string InstanceId { get; set; }
    public Guid? DesiredRevisionId { get; set; }
    public string? ContentHash { get; set; }
    public ActivationOutcome Outcome { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset CompletedAtUtc { get; set; }
}

public abstract class ApiKeyRecord
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public required string Name { get; set; }
    public required string KeyPrefix { get; set; }
    public required byte[] KeyHash { get; set; }
    public string AllowedCidrsJson { get; set; } = "[]";
    public DateTimeOffset? ExpiresAtUtc { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
    public required string CreatedBy { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastUsedAtUtc { get; set; }
}

public sealed class ManagementApiKey : ApiKeyRecord
{
    public string ScopesJson { get; set; } = "[]";
}

public sealed class ConsumerApiKey : ApiKeyRecord
{
    public string EnvironmentIdsJson { get; set; } = "[]";
    public string RouteIdsJson { get; set; } = "[]";
    public string ClaimsJson { get; set; } = "{}";
}

public sealed class LocalAdministrator
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public required string Username { get; set; }
    public required string NormalizedUsername { get; set; }
    public string? DisplayName { get; set; }
    public required string PasswordHash { get; set; }
    public required string SecurityStamp { get; set; }
    public string RolesJson { get; set; } = "[\"Administrator\"]";
    public bool Enabled { get; set; } = true;
    public bool MustChangePassword { get; set; }
    public Guid ConcurrencyVersion { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastLoginAtUtc { get; set; }
    public string ExternalIdentitiesJson { get; set; } = "[]";
    public string RoleGrantsJson { get; set; } = "[]";
}

public sealed class AdminIdentityProviderDocument
{
    public required string Id { get; set; }
    public required string Json { get; set; }
}

public sealed class InboundCertificate
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public required string Name { get; set; }
    public required byte[] ProtectedPkcs12 { get; set; }
    public required string Thumbprint { get; set; }
    public required string Subject { get; set; }
    public string DnsNamesJson { get; set; } = "[]";
    public DateTimeOffset NotBeforeUtc { get; set; }
    public DateTimeOffset NotAfterUtc { get; set; }
    public Guid ConcurrencyVersion { get; set; } = Guid.NewGuid();
    public required string CreatedBy { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public enum AcmeChallengeKind
{
    Http01,
    Dns01,
    ManualDns01
}

public enum DnsProviderKind
{
    Cloudflare,
    Route53,
    AzureDns,
    GoogleCloudDns,
    DigitalOcean,
    Loopia,
    Simply
}

public enum ManagedCertificateState
{
    Pending,
    Issuing,
    Active,
    Renewing,
    Failed
}

public sealed class AcmeAccount
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public required string Name { get; set; }
    public required string DirectoryUrl { get; set; }
    public bool IsStaging { get; set; }
    public bool IsDefault { get; set; }
    public required string ContactEmail { get; set; }
    public required byte[] ProtectedAccountKey { get; set; }
    public string? AccountUrl { get; set; }
    public string? TermsOfServiceUrl { get; set; }
    public DateTimeOffset TermsAcceptedAtUtc { get; set; }
    public Guid ConcurrencyVersion { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class DnsProviderProfile
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public required string Name { get; set; }
    public DnsProviderKind Provider { get; set; }
    public required byte[] ProtectedCredentials { get; set; }
    public string ManagedZonesJson { get; set; } = "[]";
    public Guid ConcurrencyVersion { get; set; } = Guid.NewGuid();
    public required string CreatedBy { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ManagedCertificate
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid AcmeAccountId { get; set; }
    public AcmeAccount AcmeAccount { get; set; } = null!;
    public Guid? InboundCertificateId { get; set; }
    public InboundCertificate? InboundCertificate { get; set; }
    public required string Name { get; set; }
    public string DnsNamesJson { get; set; } = "[]";
    public AcmeChallengeKind ChallengeKind { get; set; }
    public Guid? DnsProviderProfileId { get; set; }
    public DnsProviderProfile? DnsProviderProfile { get; set; }
    public ManagedCertificateState State { get; set; } = ManagedCertificateState.Pending;
    public int FailedAttemptCount { get; set; }
    public string? LastErrorCode { get; set; }
    public string? LastErrorMessage { get; set; }
    public DateTimeOffset? LastAttemptAtUtc { get; set; }
    public DateTimeOffset? LastSuccessAtUtc { get; set; }
    public DateTimeOffset? AriWindowStartUtc { get; set; }
    public DateTimeOffset? AriWindowEndUtc { get; set; }
    public DateTimeOffset? LastAriCheckAtUtc { get; set; }
    public DateTimeOffset NextAttemptAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public Guid ConcurrencyVersion { get; set; } = Guid.NewGuid();
    public required string CreatedBy { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class AcmeOrder
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ManagedCertificateId { get; set; }
    public ManagedCertificate ManagedCertificate { get; set; } = null!;
    public string? OrderUrl { get; set; }
    public byte[]? ProtectedCertificateKey { get; set; }
    public string State { get; set; } = "Pending";
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAtUtc { get; set; } = DateTimeOffset.UtcNow.AddHours(1);
}

public sealed class AcmeChallenge
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid AcmeOrderId { get; set; }
    public AcmeOrder AcmeOrder { get; set; } = null!;
    public AcmeChallengeKind Kind { get; set; }
    public required string Host { get; set; }
    public string? Token { get; set; }
    public string? KeyAuthorization { get; set; }
    public string? DnsRecordName { get; set; }
    public string? DnsRecordValue { get; set; }
    public string? ProviderRecordId { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; } = DateTimeOffset.UtcNow.AddHours(1);
}

public sealed class EntraConnectionSettings
{
    public Guid Id { get; set; } = Guid.Parse("00000000-0000-0000-0000-000000000003");
    public bool Enabled { get; set; }
    public string Authority { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
    public Guid ConcurrencyVersion { get; set; } = Guid.NewGuid();
}

public sealed class InboundSecuritySettings
{
    public Guid Id { get; set; } = Guid.Parse("00000000-0000-0000-0000-000000000002");
    public bool HstsEnabled { get; set; }
    public string HstsHostsJson { get; set; } = "[]";
    public int HstsMaxAgeSeconds { get; set; } = 15_552_000;
    public bool HstsIncludeSubDomains { get; set; }
    public bool HstsPreload { get; set; }
    public Guid ConcurrencyVersion { get; set; } = Guid.NewGuid();
}

public sealed class AuditEvent
{
    public long Id { get; set; }
    public Guid? EnvironmentId { get; set; }
    public required string ActorType { get; set; }
    public required string ActorId { get; set; }
    public required string Action { get; set; }
    public required string TargetType { get; set; }
    public required string TargetId { get; set; }
    public string DetailsJson { get; set; } = "{}";
    public required string CorrelationId { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class MaintenanceLease
{
    public required string JobName { get; set; }
    public required string OwnerInstance { get; set; }
    public DateTimeOffset LeaseExpiresAtUtc { get; set; }
    public Guid ConcurrencyVersion { get; set; } = Guid.NewGuid();
}
