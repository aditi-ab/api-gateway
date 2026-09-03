using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using ApiGateway.Application;
using ApiGateway.Domain;
using ApiGateway.Persistence;
using HotChocolate.Authorization;
using HotChocolate.Execution;
using Microsoft.EntityFrameworkCore;
using KeyNotFoundException = System.Collections.Generic.KeyNotFoundException;
using Path = System.IO.Path;

namespace ApiGateway.Management;

[Authorize(Policy = ManagementAuth.ReaderPolicy)]
public sealed class Query
{
    public ManagementIdentity GetMe(IHttpContextAccessor http)
    {
        var user = http.HttpContext?.User;
        return new ManagementIdentity(user?.FindFirst(ClaimTypes.NameIdentifier)?.Value, user?.Identity?.Name,
            user?.Identity?.AuthenticationType,
            user?.FindAll(ClaimTypes.Role).Select(x => x.Value).Distinct().Order().ToArray() ?? [],
            user?.FindAll("apigateway.scope").Select(x => x.Value).Distinct().Order().ToArray() ?? []);
    }

    [Authorize(Policy = ManagementAuth.ReaderPolicy)]
    public Task<List<GatewayEnvironment>> GetEnvironments(GatewayDbContext db, CancellationToken ct)
    {
        return db.Environments.AsNoTracking().OrderBy(x => x.Slug).ToListAsync(ct);
    }

    public Task<GatewayEnvironment?> GetEnvironment(Guid id, GatewayDbContext db, CancellationToken ct)
    {
        return db.Environments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
    }

    public Task<List<ConfigRevision>> GetRevisions(Guid environmentId, RevisionState? state, GatewayDbContext db,
        CancellationToken ct)
    {
        return db.Revisions.AsNoTracking()
            .Where(x => x.EnvironmentId == environmentId && (state == null || x.State == state))
            .OrderByDescending(x => x.Number).ToListAsync(ct);
    }

    public Task<ConfigRevision?> GetRevision(Guid id, GatewayDbContext db, CancellationToken ct)
    {
        return db.Revisions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task<ConfigRevision?> GetActiveRevision(Guid environmentId, GatewayDbContext db, CancellationToken ct)
    {
        var active = await db.Environments.AsNoTracking().Where(x => x.Id == environmentId)
            .Select(x => x.ActiveRevisionId).SingleOrDefaultAsync(ct);
        return active is null ? null : await db.Revisions.AsNoTracking().SingleAsync(x => x.Id == active, ct);
    }

    [Authorize(Policy = ManagementAuth.InstancesReadPolicy)]
    public Task<List<GatewayInstance>> GetInstances(Guid environmentId, GatewayDbContext db, CancellationToken ct)
    {
        return db.Instances.AsNoTracking().Where(x => x.EnvironmentId == environmentId).OrderBy(x => x.InstanceId)
            .ToListAsync(ct);
    }

    [Authorize(Policy = ManagementAuth.InstancesReadPolicy)]
    public Task<List<GatewayActivationEvent>> GetActivationHistory(Guid environmentId, int take, GatewayDbContext db,
        CancellationToken ct)
    {
        return db.ActivationEvents.AsNoTracking().Where(x => x.EnvironmentId == environmentId)
            .OrderByDescending(x => x.Id).Take(Math.Clamp(take, 1, 200)).ToListAsync(ct);
    }

    [Authorize(Policy = ManagementAuth.AuditReadPolicy)]
    public Task<List<AuditEvent>> GetAuditEvents(Guid? environmentId, int take, GatewayDbContext db,
        CancellationToken ct)
    {
        return db.AuditEvents.AsNoTracking().Where(x => environmentId == null || x.EnvironmentId == environmentId)
            .OrderByDescending(x => x.Id).Take(Math.Clamp(take, 1, 200)).ToListAsync(ct);
    }

    public async Task<ValidationReport> ValidateRevision(Guid id, GatewayLifecycleService lifecycle,
        CancellationToken ct)
    {
        return await lifecycle.ValidateAsync(id, ct);
    }

    [Authorize(Policy = ManagementAuth.ReaderPolicy)]
    public SystemStatus GetSystemStatus()
    {
        return new SystemStatus(typeof(Query).Assembly.GetName().Version?.ToString() ?? "development",
            DateTimeOffset.UtcNow);
    }

    [Authorize(Policy = ManagementAuth.CredentialsReadPolicy)]
    public async Task<List<ApiKeyInfo>> GetManagementApiKeys(GatewayDbContext db, CancellationToken ct)
    {
        return await db.ManagementApiKeys.AsNoTracking().OrderBy(x => x.Name).Select(x =>
            new ApiKeyInfo(x.Id, x.Name, x.KeyPrefix, x.ExpiresAtUtc, x.RevokedAtUtc, x.LastUsedAtUtc)).ToListAsync(ct);
    }

    [Authorize(Policy = ManagementAuth.CredentialsReadPolicy)]
    public async Task<List<ApiKeyInfo>> GetConsumerApiKeys(GatewayDbContext db, CancellationToken ct)
    {
        return await db.ConsumerApiKeys.AsNoTracking().OrderBy(x => x.Name).Select(x =>
            new ApiKeyInfo(x.Id, x.Name, x.KeyPrefix, x.ExpiresAtUtc, x.RevokedAtUtc, x.LastUsedAtUtc)).ToListAsync(ct);
    }

    [Authorize(Policy = ManagementAuth.AdministratorPolicy)]
    public async Task<IReadOnlyList<LocalUserInfo>> GetLocalUsers(LocalAdministratorService users,
        CancellationToken ct)
    {
        return (await users.ListAsync(ct)).Select(LocalUserInfo.From).ToArray();
    }

    public IReadOnlyList<string> GetLocalRoleCatalog()
    {
        return ManagementAuth.LocalRoles;
    }

    [Authorize(Policy = ManagementAuth.AdministratorPolicy)]
    public EntraConnectionInfo GetEntraConnection(EntraConnectionService connection)
    {
        return EntraConnectionInfo.From(connection.Get());
    }

    [Authorize(Policy = ManagementAuth.AdministratorPolicy)]
    public async Task<IReadOnlyList<InboundCertificateInfo>> GetInboundCertificates(InboundCertificateService service,
        CancellationToken ct)
    {
        return (await service.ListAsync(ct)).Select(InboundCertificateInfo.From).ToArray();
    }

    [Authorize(Policy = ManagementAuth.AdministratorPolicy)]
    public Task<AcmeDirectorySnapshot> GetAcmeDirectory(AcmeAccountService service, CancellationToken ct)
    {
        return service.DirectoryAsync(ct);
    }

    [Authorize(Policy = ManagementAuth.AdministratorPolicy)]
    public async Task<AcmeAccountInfo?> GetAcmeAccount(AcmeAccountService service, CancellationToken ct)
    {
        var value = await service.GetAsync(ct);
        return value is null ? null : AcmeAccountInfo.From(value);
    }

    [Authorize(Policy = ManagementAuth.AdministratorPolicy)]
    public async Task<IReadOnlyList<AcmeDirectorySnapshot>> GetAcmeDirectories(AcmeAccountService service,
        CancellationToken ct)
    {
        return await service.DirectoriesAsync(ct);
    }

    [Authorize(Policy = ManagementAuth.AdministratorPolicy)]
    public async Task<IReadOnlyList<AcmeAccountInfo>> GetAcmeAccounts(AcmeAccountService service,
        CancellationToken ct)
    {
        return (await service.ListAsync(ct)).Select(AcmeAccountInfo.From).ToArray();
    }

    [Authorize(Policy = ManagementAuth.AdministratorPolicy)]
    public async Task<IReadOnlyList<DnsProviderProfileInfo>> GetDnsProviderProfiles(
        DnsProviderProfileService service, CancellationToken ct)
    {
        return (await service.ListAsync(ct)).Select(DnsProviderProfileInfo.From).ToArray();
    }

    [Authorize(Policy = ManagementAuth.AdministratorPolicy)]
    public async Task<IReadOnlyList<ManagedCertificateInfo>> GetManagedCertificates(
        ManagedCertificateService service, CancellationToken ct)
    {
        return (await service.ListAsync(ct)).Select(ManagedCertificateInfo.From).ToArray();
    }

    [Authorize(Policy = ManagementAuth.AdministratorPolicy)]
    public async Task<List<ManagedCertificateActivityInfo>> GetManagedCertificateActivity(Guid managedCertificateId,
        int take, GatewayDbContext db, CancellationToken ct)
    {
        var targetId = managedCertificateId.ToString();
        var values = await db.AuditEvents.AsNoTracking()
            .Where(x => x.TargetType == nameof(ManagedCertificate) && x.TargetId == targetId &&
                        x.Action.StartsWith("ManagedCertificate"))
            .OrderByDescending(x => x.Id).Take(Math.Clamp(take, 1, 100))
            .Select(x => new ManagedCertificateActivityInfo(x.Id, x.Action, x.DetailsJson, x.OccurredAtUtc))
            .ToListAsync(ct);
        return values.OrderBy(x => x.Id).ToList();
    }

    [Authorize(Policy = ManagementAuth.AdministratorPolicy)]
    public async Task<List<ManagedCertificateDnsChallengeInfo>> GetManagedCertificateDnsChallenges(
        Guid managedCertificateId, GatewayDbContext db, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var values = await db.AcmeChallenges.AsNoTracking()
            .Where(x => x.AcmeOrder.ManagedCertificateId == managedCertificateId)
            .Where(x => x.Kind == AcmeChallengeKind.Dns01 || x.Kind == AcmeChallengeKind.ManualDns01)
            .Where(x => x.DnsRecordName != null && x.DnsRecordValue != null)
            .Select(x => new ManagedCertificateDnsChallengeInfo(x.Id, x.DnsRecordName!, x.DnsRecordValue!,
                x.ExpiresAtUtc))
            .ToListAsync(ct);
        return values.Where(x => x.ExpiresAtUtc > now).OrderBy(x => x.RecordName).ToList();
    }

    [Authorize(Policy = ManagementAuth.AdministratorPolicy)]
    public async Task<InboundSecuritySettingsInfo> GetInboundSecuritySettings(InboundSecuritySettingsService service,
        CancellationToken ct)
    {
        return InboundSecuritySettingsInfo.From(await service.GetAsync(ct));
    }

    [UsePaging(MaxPageSize = 200)]
    public IQueryable<GatewayEnvironment> GetEnvironmentConnection(GatewayDbContext db)
    {
        return db.Environments.AsNoTracking().OrderBy(x => x.Slug);
    }

    [UsePaging(MaxPageSize = 200)]
    public IQueryable<ConfigRevision> GetRevisionConnection(Guid environmentId, GatewayDbContext db)
    {
        return db.Revisions.AsNoTracking().Where(x => x.EnvironmentId == environmentId)
            .OrderByDescending(x => x.Number);
    }

    [Authorize(Policy = ManagementAuth.InstancesReadPolicy)]
    [UsePaging(MaxPageSize = 200)]
    public IQueryable<GatewayInstance> GetInstanceConnection(Guid environmentId, GatewayDbContext db)
    {
        return db.Instances.AsNoTracking().Where(x => x.EnvironmentId == environmentId).OrderBy(x => x.InstanceId);
    }

    [Authorize(Policy = ManagementAuth.AuditReadPolicy)]
    [UsePaging(MaxPageSize = 200)]
    public IQueryable<AuditEvent> GetAuditConnection(Guid? environmentId, GatewayDbContext db)
    {
        return db.AuditEvents.AsNoTracking().Where(x => environmentId == null || x.EnvironmentId == environmentId)
            .OrderByDescending(x => x.Id);
    }

    [Authorize(Policy = ManagementAuth.InstancesReadPolicy)]
    [UsePaging(MaxPageSize = 200)]
    public IQueryable<GatewayActivationEvent> GetActivationConnection(Guid environmentId, GatewayDbContext db)
    {
        return db.ActivationEvents.AsNoTracking().Where(x => x.EnvironmentId == environmentId)
            .OrderByDescending(x => x.Id);
    }

    public async Task<List<GatewayRoute>> GetActiveRoutes(Guid environmentId, GatewayDbContext db, CancellationToken ct)
    {
        return (await ActiveDocument(environmentId, db, ct))?.Routes.ToList() ?? [];
    }

    public async Task<List<GatewayCluster>> GetActiveClusters(Guid environmentId, GatewayDbContext db,
        CancellationToken ct)
    {
        return (await ActiveDocument(environmentId, db, ct))?.Clusters.ToList() ?? [];
    }

    public Task<IReadOnlyList<ManagedRoute>> GetRoutes(Guid environmentId, string? filter,
        GatewayConfigurationService configuration, CancellationToken ct)
    {
        return configuration.RoutesAsync(environmentId, filter, ct);
    }

    public Task<ManagedRoute?> GetRoute(Guid environmentId, string routeId,
        GatewayConfigurationService configuration, CancellationToken ct)
    {
        return configuration.RouteAsync(environmentId, routeId, ct);
    }

    public Task<IReadOnlyList<NamedUpstream>> GetUpstreams(Guid environmentId,
        GatewayConfigurationService configuration, CancellationToken ct)
    {
        return configuration.UpstreamsAsync(environmentId, ct);
    }

    public Task<NamedUpstream?> GetUpstream(Guid environmentId, string upstreamId,
        GatewayConfigurationService configuration, CancellationToken ct)
    {
        return configuration.UpstreamAsync(environmentId, upstreamId, ct);
    }

    public IReadOnlyList<RouteFeatureDescriptor> GetRouteFeatureCatalog()
    {
        return ManagedRouteCompiler.FeatureCatalog;
    }

    public Task<IReadOnlyList<RouteUnavailableResponseProfile>> GetRouteUnavailableResponseProfiles(
        Guid environmentId, GatewayConfigurationService configuration, CancellationToken ct)
    {
        return configuration.UnavailableResponseProfilesAsync(environmentId, ct);
    }

    public Task<RouteOperationalDefaults> GetRouteOperationalDefaults(Guid environmentId,
        GatewayConfigurationService configuration, CancellationToken ct)
    {
        return configuration.OperationalDefaultsAsync(environmentId, ct);
    }

    public Task<PendingConfigurationInfo?> GetPendingConfiguration(Guid environmentId,
        GatewayConfigurationService configuration, CancellationToken ct)
    {
        return configuration.PendingAsync(environmentId, ct);
    }

    public async Task<IReadOnlyList<RouteRuntimeStatus>> GetRouteRuntimeStatuses(Guid environmentId,
        GatewayDbContext db, CancellationToken ct)
    {
        var cutoff = DateTimeOffset.UtcNow.AddSeconds(-45);
        var instances = (await db.Instances.AsNoTracking()
                .Where(x => x.EnvironmentId == environmentId && x.StoppedAtUtc == null)
                .ToListAsync(ct))
            .Where(x => x.LastHeartbeatAtUtc >= cutoff)
            .ToList();
        var totals = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var instance in instances)
        {
            var counts = string.IsNullOrWhiteSpace(instance.ActiveRouteRequestsJson)
                ? []
                : JsonSerializer.Deserialize<Dictionary<string, long>>(instance.ActiveRouteRequestsJson) ?? [];
            foreach (var count in counts)
                totals[count.Key] = totals.GetValueOrDefault(count.Key) + count.Value;
        }

        var document = await ActiveDocument(environmentId, db, ct);
        return (document?.Routes.Select(x => x.Id) ?? [])
            .Union(totals.Keys, StringComparer.OrdinalIgnoreCase)
            .Select(routeId => new RouteRuntimeStatus(routeId, totals.GetValueOrDefault(routeId), instances.Count))
            .ToArray();
    }

    public Task<List<ConfigRevision>> GetConfigurationHistory(Guid environmentId, GatewayDbContext db,
        CancellationToken ct)
    {
        return db.Revisions.AsNoTracking()
            .Where(x => x.EnvironmentId == environmentId && x.State == RevisionState.Published)
            .OrderByDescending(x => x.Number).ToListAsync(ct);
    }

    public Task<string> GetExportConfiguration(Guid environmentId, GatewayConfigurationService configuration,
        CancellationToken ct)
    {
        return configuration.ExportActiveAsync(environmentId, ct);
    }

    [UsePaging(MaxPageSize = 200)]
    public async Task<IEnumerable<GatewayRoute>> GetRevisionRoutes(Guid revisionId, string? filter, GatewayDbContext db,
        CancellationToken ct)
    {
        var document =
            ConfigDocuments.Parse(
                (await db.Revisions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == revisionId, ct) ??
                 throw new KeyNotFoundException()).ConfigJson);
        return document.Routes.Where(x =>
            string.IsNullOrWhiteSpace(filter) || x.Id.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            x.Match.Path.Contains(filter, StringComparison.OrdinalIgnoreCase));
    }

    [UsePaging(MaxPageSize = 200)]
    public async Task<IEnumerable<GatewayCluster>> GetClusters(Guid revisionId, string? filter, GatewayDbContext db,
        CancellationToken ct)
    {
        var document =
            ConfigDocuments.Parse(
                (await db.Revisions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == revisionId, ct) ??
                 throw new KeyNotFoundException()).ConfigJson);
        return document.Clusters.Where(x =>
            string.IsNullOrWhiteSpace(filter) || x.Id.Contains(filter, StringComparison.OrdinalIgnoreCase));
    }

    [Authorize(Policy = ManagementAuth.CredentialsReadPolicy)]
    public Task<ApiKeyInfo?> GetManagementApiKey(Guid id, GatewayDbContext db, CancellationToken ct)
    {
        return db.ManagementApiKeys.AsNoTracking().Where(x => x.Id == id).Select(x =>
                new ApiKeyInfo(x.Id, x.Name, x.KeyPrefix, x.ExpiresAtUtc, x.RevokedAtUtc, x.LastUsedAtUtc))
            .SingleOrDefaultAsync(ct);
    }

    [Authorize(Policy = ManagementAuth.CredentialsReadPolicy)]
    public Task<ApiKeyInfo?> GetConsumerApiKey(Guid id, GatewayDbContext db, CancellationToken ct)
    {
        return db.ConsumerApiKeys.AsNoTracking().Where(x => x.Id == id).Select(x =>
                new ApiKeyInfo(x.Id, x.Name, x.KeyPrefix, x.ExpiresAtUtc, x.RevokedAtUtc, x.LastUsedAtUtc))
            .SingleOrDefaultAsync(ct);
    }

    [Authorize(Policy = ManagementAuth.InstancesReadPolicy)]
    public async Task<GatewayDiagnostics> GetGatewayDiagnostics(Guid environmentId, GatewayDbContext db,
        CancellationToken ct)
    {
        var environment = await db.Environments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == environmentId, ct) ??
                          throw new KeyNotFoundException();
        var now = DateTimeOffset.UtcNow;
        var instances = await db.Instances.AsNoTracking().Where(x => x.EnvironmentId == environmentId).ToListAsync(ct);
        var failures = await db.ActivationEvents.AsNoTracking()
            .Where(x => x.EnvironmentId == environmentId && x.Outcome == ActivationOutcome.Failed)
            .OrderByDescending(x => x.Id).Take(20).ToListAsync(ct);
        return new GatewayDiagnostics(environment.Id, environment.ActiveRevisionId, instances.Count,
            instances.Count(x =>
                x.StoppedAtUtc is null && x.LastHeartbeatAtUtc >= now.AddSeconds(-45) &&
                x.ActivatedRevisionId == environment.ActiveRevisionId),
            instances.Count(x => x.StoppedAtUtc is null && x.LastHeartbeatAtUtc < now.AddSeconds(-45)),
            instances.Count(x =>
                x.StoppedAtUtc is null && x.LastHeartbeatAtUtc >= now.AddSeconds(-45) &&
                x.ActivatedRevisionId != environment.ActiveRevisionId), failures);
    }

    public async Task<string> GetConfigurationSchema(int version, IWebHostEnvironment environment, CancellationToken ct)
    {
        if (version is not (1 or 2 or 3)) throw new KeyNotFoundException("Configuration schema version not found.");
        return await File.ReadAllTextAsync(
            Path.Combine(environment.WebRootPath, "schemas", $"gateway-config.v{version}.schema.json"), ct);
    }

    [Authorize(Policy = ManagementAuth.InstancesReadPolicy)]
    public async Task<List<InstanceDrift>> GetDrift(Guid environmentId, GatewayDbContext db, CancellationToken ct)
    {
        var environment = await db.Environments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == environmentId, ct) ??
                          throw new KeyNotFoundException();
        var now = DateTimeOffset.UtcNow;
        return await db.Instances.AsNoTracking().Where(x => x.EnvironmentId == environmentId).OrderBy(x => x.InstanceId)
            .Select(x => new InstanceDrift(x.Id, x.InstanceId, x.ActivatedRevisionId, environment.ActiveRevisionId,
                x.StoppedAtUtc != null ? "Stopped" :
                x.LastHeartbeatAtUtc < now.AddSeconds(-45) ? "Stale" :
                x.ActivatedRevisionId != environment.ActiveRevisionId ? "Drifted" : "Healthy")).ToListAsync(ct);
    }

    public async Task<RevisionDiff> GetRevisionDiff(Guid fromRevisionId, Guid toRevisionId, GatewayDbContext db,
        CancellationToken ct)
    {
        var revisions = await db.Revisions.AsNoTracking().Where(x => x.Id == fromRevisionId || x.Id == toRevisionId)
            .ToListAsync(ct);
        if (revisions.Count != 2) throw new KeyNotFoundException();
        var from = revisions.Single(x => x.Id == fromRevisionId);
        var to = revisions.Single(x => x.Id == toRevisionId);
        if (from.EnvironmentId != to.EnvironmentId)
            throw new InvalidOperationException("Revision diffs require revisions from the same environment.");
        var paths = new List<string>();
        var changes = new List<RevisionChange>();
        CollectChanges(JsonNode.Parse(from.ConfigJson), JsonNode.Parse(to.ConfigJson), "$", paths, changes);
        return new RevisionDiff(from.Id, to.Id, paths, changes);
    }

    private static async Task<GatewayConfigDocument?> ActiveDocument(Guid environmentId, GatewayDbContext db,
        CancellationToken ct)
    {
        var json = await db.Environments.AsNoTracking().Where(x => x.Id == environmentId && x.ActiveRevisionId != null)
            .Join(db.Revisions.AsNoTracking(), x => x.ActiveRevisionId, x => x.Id, (_, revision) => revision.ConfigJson)
            .SingleOrDefaultAsync(ct);
        return json is null ? null : ConfigDocuments.Parse(json);
    }

    private static void CollectChanges(JsonNode? left, JsonNode? right, string path, List<string> result,
        List<RevisionChange> changes)
    {
        if (JsonNode.DeepEquals(left, right)) return;
        if (left is JsonObject lo && right is JsonObject ro)
        {
            foreach (var key in lo.Select(x => x.Key).Union(ro.Select(x => x.Key)).Order())
                CollectChanges(lo[key], ro[key], $"{path}.{key}", result, changes);
            return;
        }

        if (left is JsonArray la && right is JsonArray ra)
        {
            for (var i = 0; i < Math.Max(la.Count, ra.Count); i++)
                CollectChanges(i < la.Count ? la[i] : null, i < ra.Count ? ra[i] : null, $"{path}[{i}]", result,
                    changes);
            return;
        }

        result.Add(path);
        changes.Add(new RevisionChange(path, left?.ToJsonString(), right?.ToJsonString()));
    }
}

public sealed record SystemStatus(string Version, DateTimeOffset CheckedAtUtc);

public sealed record ManagementIdentity(
    string? Id,
    string? Name,
    string? AuthenticationType,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Scopes);

public sealed record GatewayDiagnostics(
    Guid EnvironmentId,
    Guid? DesiredRevisionId,
    int InstanceCount,
    int HealthyCount,
    int StaleCount,
    int DriftedCount,
    IReadOnlyList<GatewayActivationEvent> RecentFailures);

public sealed record RevisionExport(Guid RevisionId, long Number, string ContentHash, string Json);

public sealed record DraftPayload(ConfigRevision Revision);

public sealed record SecretPayload(Guid Id, string Prefix, string Secret);

public sealed record ApiKeyInfo(
    Guid Id,
    string Name,
    string Prefix,
    DateTimeOffset? ExpiresAtUtc,
    DateTimeOffset? RevokedAtUtc,
    DateTimeOffset? LastUsedAtUtc);

public sealed record LocalUserInfo(
    Guid Id,
    string Username,
    string? DisplayName,
    IReadOnlyList<string> Roles,
    bool Enabled,
    bool MustChangePassword,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? LastLoginAtUtc,
    Guid Version)
{
    public static LocalUserInfo From(LocalAdministrator user)
    {
        return new LocalUserInfo(user.Id, user.Username, user.DisplayName,
            LocalAdministratorService.Roles(user), user.Enabled, user.MustChangePassword, user.CreatedAtUtc,
            user.LastLoginAtUtc, user.ConcurrencyVersion);
    }
}

public sealed record LocalUserSecretPayload(LocalUserInfo User, string TemporaryPassword);

public sealed record EntraConnectionInfo(
    bool Enabled,
    bool Configured,
    string Authority,
    string Audience,
    string ClientId,
    string Scope,
    Guid Version)
{
    public static EntraConnectionInfo From(EntraConnectionSnapshot value)
    {
        return new EntraConnectionInfo(value.Enabled, value.Configured, value.Authority, value.Audience, value.ClientId,
            value.Scope,
            value.Version);
    }
}

public sealed record InboundCertificateInfo(
    Guid Id,
    string Name,
    string Thumbprint,
    string Subject,
    IReadOnlyList<string> DnsNames,
    DateTimeOffset NotBeforeUtc,
    DateTimeOffset NotAfterUtc,
    Guid Version)
{
    public static InboundCertificateInfo From(InboundCertificate value)
    {
        return new InboundCertificateInfo(value.Id, value.Name, value.Thumbprint,
            value.Subject, JsonSerializer.Deserialize<string[]>(value.DnsNamesJson) ?? [], value.NotBeforeUtc,
            value.NotAfterUtc, value.ConcurrencyVersion);
    }
}

public sealed record AcmeAccountInfo(
    Guid Id,
    string Name,
    string DirectoryUrl,
    bool IsStaging,
    bool IsDefault,
    string ContactEmail,
    string? AccountUrl,
    string? TermsOfServiceUrl,
    DateTimeOffset TermsAcceptedAtUtc,
    Guid Version)
{
    public static AcmeAccountInfo From(AcmeAccount value)
    {
        return new AcmeAccountInfo(value.Id, value.Name, value.DirectoryUrl, value.IsStaging, value.IsDefault,
            value.ContactEmail,
            value.AccountUrl, value.TermsOfServiceUrl, value.TermsAcceptedAtUtc, value.ConcurrencyVersion);
    }
}

public sealed record DnsProviderProfileInfo(
    Guid Id,
    string Name,
    DnsProviderKind Provider,
    IReadOnlyList<string> ManagedZones,
    DateTimeOffset UpdatedAtUtc,
    Guid Version)
{
    public static DnsProviderProfileInfo From(DnsProviderProfile value)
    {
        return new DnsProviderProfileInfo(value.Id, value.Name, value.Provider,
            DnsProviderProfileService.Zones(value).Select(x => x.Name).ToArray(), value.UpdatedAtUtc,
            value.ConcurrencyVersion);
    }
}

public sealed record ManagedCertificateInfo(
    Guid Id,
    string Name,
    IReadOnlyList<string> DnsNames,
    Guid AcmeAccountId,
    string AcmeAccountName,
    bool IsStaging,
    AcmeChallengeKind ChallengeKind,
    Guid? DnsProviderProfileId,
    string? DnsProviderProfileName,
    ManagedCertificateState State,
    int FailedAttemptCount,
    string? LastErrorCode,
    string? LastErrorMessage,
    DateTimeOffset? LastAttemptAtUtc,
    DateTimeOffset? LastSuccessAtUtc,
    DateTimeOffset NextAttemptAtUtc,
    InboundCertificateInfo? Certificate,
    Guid Version)
{
    public static ManagedCertificateInfo From(ManagedCertificate value)
    {
        return new ManagedCertificateInfo(value.Id, value.Name,
            JsonSerializer.Deserialize<string[]>(value.DnsNamesJson) ?? [], value.AcmeAccountId,
            value.AcmeAccount.Name, value.AcmeAccount.IsStaging, value.ChallengeKind,
            value.DnsProviderProfileId, value.DnsProviderProfile?.Name, value.State, value.FailedAttemptCount,
            value.LastErrorCode, value.LastErrorMessage, value.LastAttemptAtUtc, value.LastSuccessAtUtc,
            value.NextAttemptAtUtc,
            value.InboundCertificate is null ? null : InboundCertificateInfo.From(value.InboundCertificate),
            value.ConcurrencyVersion);
    }
}

public sealed record ManagedCertificateActivityInfo(
    long Id,
    string Action,
    string DetailsJson,
    DateTimeOffset OccurredAtUtc);

public sealed record ManagedCertificateDnsChallengeInfo(
    Guid Id,
    string RecordName,
    string RecordValue,
    DateTimeOffset ExpiresAtUtc);

public sealed record InboundSecuritySettingsInfo(
    bool HstsEnabled,
    IReadOnlyList<string> HstsHosts,
    int HstsMaxAgeSeconds,
    bool HstsIncludeSubDomains,
    bool HstsPreload,
    Guid Version)
{
    public static InboundSecuritySettingsInfo From(InboundSecuritySettings value)
    {
        return new InboundSecuritySettingsInfo(value.HstsEnabled,
            JsonSerializer.Deserialize<string[]>(value.HstsHostsJson) ?? [], value.HstsMaxAgeSeconds,
            value.HstsIncludeSubDomains, value.HstsPreload, value.ConcurrencyVersion);
    }
}

public sealed record InstanceDrift(
    Guid InstanceRecordId,
    string InstanceId,
    Guid? ActivatedRevisionId,
    Guid? DesiredRevisionId,
    string State);

public sealed record RouteRuntimeStatus(string RouteId, long ActiveRequests, int ReportingInstances);

public sealed record RevisionChange(string Path, string? BeforeJson, string? AfterJson);

public sealed record RevisionDiff(
    Guid FromRevisionId,
    Guid ToRevisionId,
    IReadOnlyList<string> ChangedPaths,
    IReadOnlyList<RevisionChange> Changes);

public sealed class Mutation
{
    [Authorize(Policy = ManagementAuth.AdministratorPolicy)]
    public async Task<AcmeAccountInfo> RegisterAcmeAccount(string contactEmail, bool termsAccepted,
        string? directoryUrl,
        AcmeAccountService service, IHttpContextAccessor http, CancellationToken ct)
    {
        return AcmeAccountInfo.From(
            await service.RegisterAsync(directoryUrl, contactEmail, termsAccepted, Actor(http), ct));
    }

    [Authorize(Policy = ManagementAuth.AdministratorPolicy)]
    public async Task<AcmeAccountInfo> UpdateAcmeAccountContact(Guid expectedVersion, string contactEmail,
        AcmeAccountService service, IHttpContextAccessor http, CancellationToken ct)
    {
        return AcmeAccountInfo.From(
            await service.UpdateContactAsync(expectedVersion, contactEmail, Actor(http), ct));
    }

    [Authorize(Policy = ManagementAuth.AdministratorPolicy)]
    public async Task<AcmeAccountInfo> UpdateAcmeAccount(Guid id, Guid expectedVersion, string contactEmail,
        AcmeAccountService service, IHttpContextAccessor http, CancellationToken ct)
    {
        return AcmeAccountInfo.From(
            await service.UpdateContactAsync(id, expectedVersion, contactEmail, Actor(http), ct));
    }

    [Authorize(Policy = ManagementAuth.AdministratorPolicy)]
    public async Task<AcmeAccountInfo> SetDefaultAcmeAccount(Guid id, Guid expectedVersion,
        AcmeAccountService service, IHttpContextAccessor http, CancellationToken ct)
    {
        return AcmeAccountInfo.From(await service.SetDefaultAsync(id, expectedVersion, Actor(http), ct));
    }

    [Authorize(Policy = ManagementAuth.AdministratorPolicy)]
    public async Task<bool> DeleteAcmeAccount(Guid id, AcmeAccountService service, IHttpContextAccessor http,
        CancellationToken ct)
    {
        await service.DeleteAsync(id, Actor(http), ct);
        return true;
    }

    [Authorize(Policy = ManagementAuth.AdministratorPolicy)]
    public async Task<DnsProviderProfileInfo> CreateDnsProviderProfile(string name, DnsProviderKind provider,
        DnsProviderCredentials credentials, DnsProviderProfileService service, IHttpContextAccessor http,
        CancellationToken ct)
    {
        return DnsProviderProfileInfo.From(
            await service.CreateAsync(name, provider, credentials, Actor(http), ct));
    }

    [Authorize(Policy = ManagementAuth.AdministratorPolicy)]
    public async Task<DnsProviderProfileInfo> UpdateDnsProviderProfile(Guid id, Guid expectedVersion, string name,
        DnsProviderCredentials? credentials, DnsProviderProfileService service, IHttpContextAccessor http,
        CancellationToken ct)
    {
        return DnsProviderProfileInfo.From(
            await service.UpdateAsync(id, expectedVersion, name, credentials, Actor(http), ct));
    }

    [Authorize(Policy = ManagementAuth.AdministratorPolicy)]
    public Task<IReadOnlyList<DnsManagedZone>> TestDnsProviderProfile(Guid id, DnsProviderProfileService service,
        CancellationToken ct)
    {
        return service.TestAsync(id, ct);
    }

    [Authorize(Policy = ManagementAuth.AdministratorPolicy)]
    public async Task<bool> DeleteDnsProviderProfile(Guid id, DnsProviderProfileService service,
        IHttpContextAccessor http, CancellationToken ct)
    {
        await service.DeleteAsync(id, Actor(http), ct);
        return true;
    }

    [Authorize(Policy = ManagementAuth.AdministratorPolicy)]
    public async Task<ManagedCertificateInfo> IssueAcmeCertificate(string name, IReadOnlyList<string> dnsNames,
        AcmeChallengeKind challengeKind, Guid? dnsProviderProfileId, Guid? acmeAccountId,
        ManagedCertificateService service,
        IHttpContextAccessor http, CancellationToken ct)
    {
        return ManagedCertificateInfo.From(await service.IssueAsync(name, dnsNames, challengeKind,
            dnsProviderProfileId, acmeAccountId, Actor(http), ct));
    }

    [Authorize(Policy = ManagementAuth.AdministratorPolicy)]
    public async Task<ManagedCertificateInfo> RenewAcmeCertificate(Guid id, Guid expectedVersion,
        ManagedCertificateService service, IHttpContextAccessor http, CancellationToken ct)
    {
        return ManagedCertificateInfo.From(await service.RenewAsync(id, expectedVersion, Actor(http), ct));
    }

    [Authorize(Policy = ManagementAuth.AdministratorPolicy)]
    public async Task<ManagedCertificateInfo> RenameManagedCertificate(Guid id, Guid expectedVersion, string name,
        ManagedCertificateService service, IHttpContextAccessor http, CancellationToken ct)
    {
        return ManagedCertificateInfo.From(await service.RenameAsync(id, expectedVersion, name, Actor(http), ct));
    }

    [Authorize(Policy = ManagementAuth.AdministratorPolicy)]
    public async Task<bool> DeleteManagedCertificate(Guid id, ManagedCertificateService service,
        InboundCertificateService inbound, IHttpContextAccessor http, CancellationToken ct)
    {
        await service.DeleteAsync(id, Actor(http), inbound, ct);
        return true;
    }

    [Authorize(Policy = ManagementAuth.AdministratorPolicy)]
    public async Task<InboundCertificateInfo> UploadInboundCertificate(string name, string pkcs12Base64,
        string? password, InboundCertificateService certificates, IHttpContextAccessor http, CancellationToken ct)
    {
        return InboundCertificateInfo.From(await certificates.UploadAsync(name, Convert.FromBase64String(pkcs12Base64),
            password, Actor(http), ct));
    }

    [Authorize(Policy = ManagementAuth.AdministratorPolicy)]
    public async Task<InboundCertificateInfo> ReplaceInboundCertificate(Guid id, Guid expectedVersion,
        string pkcs12Base64, string? password, InboundCertificateService certificates, IHttpContextAccessor http,
        CancellationToken ct)
    {
        return InboundCertificateInfo.From(await certificates.ReplaceAsync(id, expectedVersion,
            Convert.FromBase64String(pkcs12Base64), password, Actor(http), ct));
    }

    [Authorize(Policy = ManagementAuth.AdministratorPolicy)]
    public async Task<InboundCertificateInfo> RenameInboundCertificate(Guid id, Guid expectedVersion, string name,
        InboundCertificateService certificates, IHttpContextAccessor http, CancellationToken ct)
    {
        return InboundCertificateInfo.From(
            await certificates.RenameAsync(id, expectedVersion, name, Actor(http), ct));
    }

    [Authorize(Policy = ManagementAuth.AdministratorPolicy)]
    public async Task<bool> DeleteInboundCertificate(Guid id, InboundCertificateService certificates,
        IHttpContextAccessor http, CancellationToken ct)
    {
        await certificates.DeleteAsync(id, Actor(http), ct);
        return true;
    }

    [Authorize(Policy = ManagementAuth.AdministratorPolicy)]
    public async Task<InboundSecuritySettingsInfo> UpdateInboundSecuritySettings(Guid? expectedVersion, bool enabled,
        IReadOnlyList<string> hosts, int maxAgeSeconds, bool includeSubDomains, bool preload,
        InboundSecuritySettingsService settings, IHttpContextAccessor http, CancellationToken ct)
    {
        return InboundSecuritySettingsInfo.From(await settings.UpdateAsync(expectedVersion, enabled, hosts,
            maxAgeSeconds,
            includeSubDomains, preload, Actor(http), ct));
    }

    [Authorize(Policy = ManagementAuth.AdministratorPolicy)]
    public async Task<LocalUserSecretPayload> CreateLocalUser(string username, string? displayName,
        IReadOnlyList<string> roles,
        LocalAdministratorService users, IHttpContextAccessor http, CancellationToken ct)
    {
        var result = await users.CreateAsync(username, displayName, roles, Actor(http), ct);
        return new LocalUserSecretPayload(LocalUserInfo.From(result.User), result.TemporaryPassword);
    }

    [Authorize(Policy = ManagementAuth.AdministratorPolicy)]
    public async Task<LocalUserInfo> UpdateLocalUser(Guid id, Guid expectedVersion, string? displayName,
        IReadOnlyList<string> roles, bool enabled, LocalAdministratorService users, IHttpContextAccessor http,
        CancellationToken ct)
    {
        return LocalUserInfo.From(
            await users.UpdateAsync(id, expectedVersion, displayName, roles, enabled, Actor(http), ct));
    }

    [Authorize(Policy = ManagementAuth.AdministratorPolicy)]
    public async Task<EntraConnectionInfo> UpdateEntraConnection(bool enabled, string authority, string audience,
        string clientId, string scope, Guid expectedVersion, EntraConnectionService connection,
        IHttpContextAccessor http, CancellationToken ct)
    {
        return EntraConnectionInfo.From(await connection.UpdateAsync(enabled, authority, audience, clientId, scope,
            expectedVersion, Actor(http), Correlation(http), ct));
    }

    [Authorize(Policy = ManagementAuth.AdministratorPolicy)]
    public async Task<LocalUserSecretPayload> ResetLocalUserPassword(Guid id, LocalAdministratorService users,
        IHttpContextAccessor http, CancellationToken ct)
    {
        var result = await users.ResetAsync(id, Actor(http), ct);
        return new LocalUserSecretPayload(LocalUserInfo.From(result.User), result.TemporaryPassword);
    }

    [Authorize(Policy = ManagementAuth.AdministratorPolicy)]
    public async Task<bool> DeleteLocalUser(Guid id, LocalAdministratorService users, IHttpContextAccessor http,
        CancellationToken ct)
    {
        await users.DeleteAsync(id, Actor(http), ct);
        return true;
    }

    [Authorize(Policy = ManagementAuth.ManageConfigurationPolicy)]
    public Task<ConfigurationChangeResult> CreateRoute(Guid environmentId, CreateManagedRouteInput input,
        GatewayConfigurationService configuration, IHttpContextAccessor http, CancellationToken ct)
    {
        return configuration.CreateRouteAsync(environmentId, input, Actor(http), Correlation(http), ct);
    }

    [Authorize(Policy = ManagementAuth.ManageConfigurationPolicy)]
    public Task<ConfigurationChangeResult> CreateUpstream(Guid environmentId, SaveNamedUpstreamInput input,
        GatewayConfigurationService configuration, IHttpContextAccessor http, CancellationToken ct)
    {
        return configuration.CreateUpstreamAsync(environmentId, input, Actor(http), Correlation(http), ct);
    }

    [Authorize(Policy = ManagementAuth.ManageConfigurationPolicy)]
    public Task<ConfigurationChangeResult> UpdateUpstream(Guid environmentId, string upstreamId,
        string expectedUpstreamVersion, SaveNamedUpstreamInput input, GatewayConfigurationService configuration,
        IHttpContextAccessor http, CancellationToken ct)
    {
        return configuration.UpdateUpstreamAsync(environmentId, upstreamId, expectedUpstreamVersion, input,
            Actor(http), Correlation(http), ct);
    }

    [Authorize(Policy = ManagementAuth.ManageConfigurationPolicy)]
    public Task<ConfigurationChangeResult> DeleteUpstream(Guid environmentId, string upstreamId,
        string expectedUpstreamVersion, GatewayConfigurationService configuration, IHttpContextAccessor http,
        CancellationToken ct)
    {
        return configuration.DeleteUpstreamAsync(environmentId, upstreamId, expectedUpstreamVersion, Actor(http),
            Correlation(http), ct);
    }

    [Authorize(Policy = ManagementAuth.ManageConfigurationPolicy)]
    public Task<ConfigurationChangeResult> UpdateRoute(Guid environmentId, string routeId,
        string expectedRouteVersion, UpdateManagedRouteInput input, GatewayConfigurationService configuration,
        IHttpContextAccessor http, CancellationToken ct)
    {
        return configuration.UpdateRouteAsync(environmentId, routeId,
            expectedRouteVersion, input, Actor(http), Correlation(http), ct);
    }

    [Authorize(Policy = ManagementAuth.ManageConfigurationPolicy)]
    public Task<ConfigurationChangeResult> DuplicateRoute(Guid environmentId, string routeId,
        string expectedRouteVersion, string name, GatewayConfigurationService configuration,
        IHttpContextAccessor http, CancellationToken ct)
    {
        return configuration.DuplicateRouteAsync(environmentId, routeId, expectedRouteVersion, name,
            Actor(http), Correlation(http), ct);
    }

    [Authorize(Policy = ManagementAuth.ManageConfigurationPolicy)]
    public Task<ConfigurationChangeResult> UpdateRouteFeatures(Guid environmentId, string routeId,
        string expectedRouteVersion, ManagedRouteFeatures input, GatewayConfigurationService configuration,
        IHttpContextAccessor http, CancellationToken ct)
    {
        return configuration.UpdateRouteFeaturesAsync(environmentId,
            routeId, expectedRouteVersion, input, Actor(http), Correlation(http), ct);
    }

    [Authorize(Policy = ManagementAuth.ManageConfigurationPolicy)]
    public Task<ConfigurationChangeResult> UpdateRouteBasics(Guid environmentId, string routeId,
        string expectedRouteVersion, UpdateManagedRouteBasicsInput input, GatewayConfigurationService configuration,
        IHttpContextAccessor http, CancellationToken ct)
    {
        return configuration.UpdateRouteBasicsAsync(environmentId,
            routeId, expectedRouteVersion, input, Actor(http), Correlation(http), ct);
    }

    [Authorize(Policy = ManagementAuth.ManageConfigurationPolicy)]
    public Task<ConfigurationChangeResult> SetRouteEnabled(Guid environmentId, string routeId,
        string expectedRouteVersion, bool enabled, GatewayConfigurationService configuration,
        IHttpContextAccessor http, CancellationToken ct)
    {
        return configuration.SetRouteEnabledAsync(environmentId, routeId,
            expectedRouteVersion, enabled, Actor(http), Correlation(http), ct);
    }

    [Authorize(Policy = ManagementAuth.ManageConfigurationPolicy)]
    public Task<ConfigurationChangeResult> SetRouteOperationalState(Guid environmentId, string routeId,
        string expectedRouteVersion, UpdateRouteOperationalStateInput input,
        GatewayConfigurationService configuration, IHttpContextAccessor http, CancellationToken ct)
    {
        return configuration.SetRouteOperationalStateAsync(environmentId, routeId, expectedRouteVersion, input,
            Actor(http), Correlation(http), ct);
    }

    [Authorize(Policy = ManagementAuth.ManageConfigurationPolicy)]
    public Task<ConfigurationChangeResult> SaveRouteUnavailableResponseProfile(Guid environmentId,
        Guid? expectedConfigurationVersion, SaveRouteUnavailableResponseProfileInput input,
        GatewayConfigurationService configuration, IHttpContextAccessor http, CancellationToken ct)
    {
        return configuration.SaveUnavailableResponseProfileAsync(environmentId, expectedConfigurationVersion, input,
            Actor(http), Correlation(http), ct);
    }

    [Authorize(Policy = ManagementAuth.ManageConfigurationPolicy)]
    public Task<ConfigurationChangeResult> DeleteRouteUnavailableResponseProfile(Guid environmentId,
        Guid? expectedConfigurationVersion, string profileId, GatewayConfigurationService configuration,
        IHttpContextAccessor http, CancellationToken ct)
    {
        return configuration.DeleteUnavailableResponseProfileAsync(environmentId, expectedConfigurationVersion,
            profileId, Actor(http), Correlation(http), ct);
    }

    [Authorize(Policy = ManagementAuth.ManageConfigurationPolicy)]
    public Task<ConfigurationChangeResult> UpdateRouteOperationalDefaults(Guid environmentId,
        Guid? expectedConfigurationVersion, UpdateRouteOperationalDefaultsInput input,
        GatewayConfigurationService configuration, IHttpContextAccessor http, CancellationToken ct)
    {
        return configuration.UpdateOperationalDefaultsAsync(environmentId, expectedConfigurationVersion, input,
            Actor(http), Correlation(http), ct);
    }

    [Authorize(Policy = ManagementAuth.ManageConfigurationPolicy)]
    public Task<ConfigurationChangeResult> DeleteRoute(Guid environmentId, string routeId,
        string expectedRouteVersion, GatewayConfigurationService configuration, IHttpContextAccessor http,
        CancellationToken ct)
    {
        return configuration.DeleteRouteAsync(environmentId, routeId, expectedRouteVersion,
            Actor(http), Correlation(http), ct);
    }

    [Authorize(Policy = ManagementAuth.ManageConfigurationPolicy)]
    public Task<ConfigurationChangeResult> RevertConfigurationChange(Guid environmentId, Guid changeId,
        Guid expectedConfigurationVersion, GatewayConfigurationService configuration, IHttpContextAccessor http,
        CancellationToken ct)
    {
        return configuration.RevertAsync(environmentId, changeId, expectedConfigurationVersion,
            Actor(http), Correlation(http), ct);
    }

    [Authorize(Policy = ManagementAuth.ManageConfigurationPolicy)]
    public Task<ConfigurationChangeResult> RestoreConfigurationSnapshot(Guid environmentId, Guid revisionId,
        Guid expectedConfigurationVersion, GatewayConfigurationService configuration, IHttpContextAccessor http,
        CancellationToken ct)
    {
        return configuration.RestoreSnapshotAsync(environmentId, revisionId,
            expectedConfigurationVersion, Actor(http), Correlation(http), ct);
    }

    [Authorize(Policy = ManagementAuth.ManageConfigurationPolicy)]
    public Task<ConfigurationChangeResult> ImportConfiguration(Guid environmentId,
        Guid expectedConfigurationVersion, string json, GatewayConfigurationService configuration,
        IHttpContextAccessor http, CancellationToken ct)
    {
        return configuration.ImportAsync(environmentId,
            expectedConfigurationVersion, json, Actor(http), Correlation(http), ct);
    }

    [Authorize(Policy = ManagementAuth.ManageConfigurationPolicy)]
    public Task<ConfigurationChangeResult> CopyConfiguration(Guid sourceEnvironmentId, Guid targetEnvironmentId,
        Guid expectedTargetVersion, GatewayConfigurationService configuration, IHttpContextAccessor http,
        CancellationToken ct)
    {
        return configuration.CopyAsync(sourceEnvironmentId, targetEnvironmentId,
            expectedTargetVersion, Actor(http), Correlation(http), ct);
    }

    [Authorize(Policy = ManagementAuth.ManageConfigurationPolicy)]
    public Task<ManagedOpenApiPreview> PreviewOpenApiRoutes(Guid environmentId,
        Guid expectedConfigurationVersion, string source, string upstreamUrl, string? routeIdPrefix,
        OpenApiImportService importer, CancellationToken ct)
    {
        return importer.PreviewManagedAsync(environmentId,
            expectedConfigurationVersion, source, upstreamUrl, routeIdPrefix, ct);
    }

    [Authorize(Policy = ManagementAuth.ManageConfigurationPolicy)]
    public Task<ConfigurationChangeResult> ApplyOpenApiRoutes(string previewToken, IReadOnlyList<string> routeIds,
        OpenApiImportService importer, IHttpContextAccessor http, CancellationToken ct)
    {
        return importer.ApplyManagedAsync(previewToken, routeIds, Actor(http), Correlation(http), ct);
    }

    [Authorize(Policy = ManagementAuth.WritePolicy)]
    public Task<GatewayEnvironment> CreateEnvironment(string slug, string displayName, string? description,
        GatewayLifecycleService lifecycle, IHttpContextAccessor http, CancellationToken ct)
    {
        return lifecycle.CreateEnvironmentAsync(slug, displayName, description, Actor(http), Correlation(http), ct);
    }

    [Authorize(Policy = ManagementAuth.WritePolicy)]
    public Task<GatewayEnvironment> UpdateEnvironment(Guid id, Guid expectedVersion, string displayName,
        string? description, GatewayLifecycleService lifecycle, IHttpContextAccessor http, CancellationToken ct)
    {
        return lifecycle.UpdateEnvironmentAsync(id, expectedVersion, displayName, description, Actor(http),
            Correlation(http), ct);
    }

    [Authorize(Policy = ManagementAuth.AdministratorPolicy)]
    public Task<GatewayEnvironment> SetEnvironmentPublishingMode(Guid id, Guid expectedVersion,
        ConfigurationPublishingMode mode, GatewayLifecycleService lifecycle, IHttpContextAccessor http,
        CancellationToken ct)
    {
        return lifecycle.SetPublishingModeAsync(id, expectedVersion, mode, Actor(http), Correlation(http), ct);
    }

    [Authorize(Policy = ManagementAuth.PublishPolicy)]
    public Task<ConfigRevision> PublishPendingConfiguration(Guid environmentId, Guid expectedVersion,
        string? comment, GatewayConfigurationService configuration, IHttpContextAccessor http, CancellationToken ct)
    {
        return configuration.PublishPendingAsync(environmentId, expectedVersion, comment, Actor(http),
            Correlation(http), ct);
    }

    [Authorize(Policy = ManagementAuth.PublishPolicy)]
    public Task<bool> DiscardPendingConfiguration(Guid environmentId, Guid expectedVersion,
        GatewayConfigurationService configuration, IHttpContextAccessor http, CancellationToken ct)
    {
        return configuration.DiscardPendingAsync(environmentId, expectedVersion, Actor(http), Correlation(http), ct);
    }

    [Authorize(Policy = ManagementAuth.AdministratorPolicy)]
    public Task<GatewayEnvironment> SetEnvironmentArchived(Guid id, Guid expectedVersion, bool archived,
        GatewayLifecycleService lifecycle, IHttpContextAccessor http, CancellationToken ct)
    {
        return lifecycle.SetEnvironmentArchivedAsync(id, expectedVersion, archived, Actor(http), Correlation(http), ct);
    }

    [Authorize(Policy = ManagementAuth.AdministratorPolicy)]
    public async Task<bool> DeleteEnvironment(Guid id, Guid expectedVersion, GatewayLifecycleService lifecycle,
        CancellationToken ct)
    {
        await lifecycle.DeleteEnvironmentAsync(id, expectedVersion, ct);
        return true;
    }

    [Authorize(Policy = ManagementAuth.WritePolicy)]
    [GraphQLIgnore]
    public async Task<DraftPayload> CreateDraft(Guid environmentId, Guid? baseRevisionId,
        GatewayLifecycleService lifecycle, IHttpContextAccessor http, CancellationToken ct)
    {
        return new DraftPayload(await lifecycle.CreateDraftAsync(environmentId, baseRevisionId, Actor(http),
            Correlation(http), ct));
    }

    [Authorize(Policy = ManagementAuth.PublishPolicy)]
    [GraphQLIgnore]
    public async Task<DraftPayload> PromoteRevision(Guid sourceRevisionId, Guid targetEnvironmentId,
        GatewayLifecycleService lifecycle, IHttpContextAccessor http, CancellationToken ct)
    {
        return new DraftPayload(await lifecycle.PromoteAsync(sourceRevisionId, targetEnvironmentId, Actor(http),
            Correlation(http),
            ct));
    }

    [Authorize(Policy = ManagementAuth.WritePolicy)]
    [GraphQLIgnore]
    public async Task<DraftPayload> SetDraftContent(Guid draftId, Guid expectedVersion, string json,
        GatewayLifecycleService lifecycle, IHttpContextAccessor http, CancellationToken ct)
    {
        return new DraftPayload(await lifecycle.SetDraftContentAsync(draftId, expectedVersion, json, Actor(http),
            Correlation(http),
            ct));
    }

    [Authorize(Policy = ManagementAuth.WritePolicy)]
    [GraphQLIgnore]
    public async Task<DraftPayload> ImportDraft(Guid draftId, Guid expectedVersion, string json,
        GatewayLifecycleService lifecycle, IHttpContextAccessor http, CancellationToken ct)
    {
        return new DraftPayload(await lifecycle.ImportDraftAsync(draftId, expectedVersion, json, Actor(http),
            Correlation(http),
            ct));
    }

    [Authorize(Policy = ManagementAuth.ReaderPolicy)]
    [GraphQLIgnore]
    public async Task<RevisionExport> ExportRevision(Guid revisionId, GatewayDbContext db,
        GatewayLifecycleService lifecycle, CancellationToken ct)
    {
        var revision = await db.Revisions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == revisionId, ct) ??
                       throw new KeyNotFoundException();
        return new RevisionExport(revision.Id, revision.Number, revision.ContentHash,
            await lifecycle.ExportRevisionAsync(revisionId, ct));
    }

    [Authorize(Policy = ManagementAuth.WritePolicy)]
    [GraphQLIgnore]
    public async Task<DraftPayload> UpsertRoute(Guid draftId, Guid expectedVersion, string routeJson,
        GatewayLifecycleService lifecycle, IHttpContextAccessor http, CancellationToken ct)
    {
        return new DraftPayload(await lifecycle.UpsertRouteAsync(draftId, expectedVersion, routeJson, Actor(http),
            Correlation(http),
            ct));
    }

    [Authorize(Policy = ManagementAuth.WritePolicy)]
    [GraphQLIgnore]
    public async Task<DraftPayload> DeleteDraftRoute(Guid draftId, Guid expectedVersion, string routeId,
        GatewayLifecycleService lifecycle, IHttpContextAccessor http, CancellationToken ct)
    {
        return new DraftPayload(await lifecycle.DeleteRouteAsync(draftId, expectedVersion, routeId, Actor(http),
            Correlation(http),
            ct));
    }

    [Authorize(Policy = ManagementAuth.WritePolicy)]
    [GraphQLIgnore]
    public async Task<DraftPayload> UpsertCluster(Guid draftId, Guid expectedVersion, string clusterJson,
        GatewayLifecycleService lifecycle, IHttpContextAccessor http, CancellationToken ct)
    {
        return new DraftPayload(await lifecycle.UpsertClusterAsync(draftId, expectedVersion, clusterJson, Actor(http),
            Correlation(http), ct));
    }

    [Authorize(Policy = ManagementAuth.WritePolicy)]
    [GraphQLIgnore]
    public async Task<DraftPayload> DeleteCluster(Guid draftId, Guid expectedVersion, string clusterId,
        GatewayLifecycleService lifecycle, IHttpContextAccessor http, CancellationToken ct)
    {
        return new DraftPayload(await lifecycle.DeleteClusterAsync(draftId, expectedVersion, clusterId, Actor(http),
            Correlation(http), ct));
    }

    [Authorize(Policy = ManagementAuth.WritePolicy)]
    [GraphQLIgnore]
    public async Task<DraftPayload> SetPolicies(Guid draftId, Guid expectedVersion, string policiesJson,
        GatewayLifecycleService lifecycle, IHttpContextAccessor http, CancellationToken ct)
    {
        return new DraftPayload(await lifecycle.SetPoliciesAsync(draftId, expectedVersion, policiesJson, Actor(http),
            Correlation(http), ct));
    }

    [Authorize(Policy = ManagementAuth.WritePolicy)]
    [GraphQLIgnore]
    public async Task<bool> DeleteDraft(Guid draftId, Guid expectedVersion, GatewayLifecycleService lifecycle,
        IHttpContextAccessor http, CancellationToken ct)
    {
        await lifecycle.DeleteDraftAsync(draftId, expectedVersion, Actor(http), Correlation(http), ct);
        return true;
    }

    [Authorize(Policy = ManagementAuth.WritePolicy)]
    [GraphQLIgnore]
    public Task<ValidationReport> ValidateDraft(Guid draftId, GatewayLifecycleService lifecycle, CancellationToken ct)
    {
        return lifecycle.ValidateAsync(draftId, ct);
    }

    [Authorize(Policy = ManagementAuth.PublishPolicy)]
    [GraphQLIgnore]
    public async Task<ConfigRevision> PublishDraft(Guid draftId, Guid expectedVersion, string? comment,
        GatewayLifecycleService lifecycle, IHttpContextAccessor http, CancellationToken ct)
    {
        return await lifecycle.PublishAsync(draftId, expectedVersion, comment, Actor(http), Correlation(http), ct);
    }

    [Authorize(Policy = ManagementAuth.PublishPolicy)]
    [GraphQLIgnore]
    public async Task<bool> RollbackEnvironment(Guid environmentId, Guid targetRevisionId,
        GatewayLifecycleService lifecycle, IHttpContextAccessor http, CancellationToken ct)
    {
        await lifecycle.RollbackAsync(environmentId, targetRevisionId, Actor(http), Correlation(http), ct);
        return true;
    }

    [Authorize(Policy = ManagementAuth.CredentialsWritePolicy)]
    public async Task<SecretPayload> CreateManagementApiKey(string name, IReadOnlyList<string> scopes,
        IReadOnlyList<string>? allowedCidrs, DateTimeOffset? expiresAtUtc, ApiKeyService keys,
        IHttpContextAccessor http, CancellationToken ct)
    {
        var key = await keys.CreateManagementAsync(name, scopes, allowedCidrs, expiresAtUtc, Actor(http),
            Correlation(http), ct);
        return new SecretPayload(key.Id, key.Prefix, key.Secret);
    }

    [Authorize(Policy = ManagementAuth.CredentialsWritePolicy)]
    public async Task<SecretPayload> CreateConsumerApiKey(string name, IReadOnlyList<Guid> environmentIds,
        IReadOnlyList<string> routeIds, IReadOnlyDictionary<string, string> claims, IReadOnlyList<string>? allowedCidrs,
        DateTimeOffset? expiresAtUtc, ApiKeyService keys, IHttpContextAccessor http, CancellationToken ct)
    {
        var key = await keys.CreateConsumerAsync(name, environmentIds, routeIds, claims, allowedCidrs, expiresAtUtc,
            Actor(http), Correlation(http), ct);
        return new SecretPayload(key.Id, key.Prefix, key.Secret);
    }

    [Authorize(Policy = ManagementAuth.CredentialsWritePolicy)]
    public Task<bool> RevokeManagementApiKey(Guid id, ApiKeyService keys, IHttpContextAccessor http,
        CancellationToken ct)
    {
        return keys.RevokeManagementAsync(id, Actor(http), Correlation(http), ct);
    }

    [Authorize(Policy = ManagementAuth.CredentialsWritePolicy)]
    public Task<bool> RevokeConsumerApiKey(Guid id, ApiKeyService keys, IHttpContextAccessor http, CancellationToken ct)
    {
        return keys.RevokeConsumerAsync(id, Actor(http), Correlation(http), ct);
    }

    [Authorize(Policy = ManagementAuth.CredentialsWritePolicy)]
    public async Task<SecretPayload> RotateManagementApiKey(Guid id, ApiKeyService keys, IHttpContextAccessor http,
        CancellationToken ct)
    {
        var key = await keys.RotateManagementAsync(id, Actor(http), Correlation(http), ct);
        return new SecretPayload(key.Id, key.Prefix, key.Secret);
    }

    [Authorize(Policy = ManagementAuth.CredentialsWritePolicy)]
    public async Task<SecretPayload> RotateConsumerApiKey(Guid id, ApiKeyService keys, IHttpContextAccessor http,
        CancellationToken ct)
    {
        var key = await keys.RotateConsumerAsync(id, Actor(http), Correlation(http), ct);
        return new SecretPayload(key.Id, key.Prefix, key.Secret);
    }

    [Authorize(Policy = ManagementAuth.CredentialsWritePolicy)]
    public Task<bool> UpdateManagementApiKey(Guid id, string name, IReadOnlyList<string> scopes,
        IReadOnlyList<string>? allowedCidrs, DateTimeOffset? expiresAtUtc, ApiKeyService keys,
        IHttpContextAccessor http, CancellationToken ct)
    {
        return keys.UpdateManagementAsync(id, name, scopes, allowedCidrs, expiresAtUtc, Actor(http), Correlation(http),
            ct);
    }

    [Authorize(Policy = ManagementAuth.CredentialsWritePolicy)]
    public Task<bool> UpdateConsumerApiKey(Guid id, string name, IReadOnlyList<Guid> environmentIds,
        IReadOnlyList<string> routeIds, IReadOnlyDictionary<string, string> claims, IReadOnlyList<string>? allowedCidrs,
        DateTimeOffset? expiresAtUtc, ApiKeyService keys, IHttpContextAccessor http, CancellationToken ct)
    {
        return keys.UpdateConsumerAsync(id, name, environmentIds, routeIds, claims, allowedCidrs, expiresAtUtc,
            Actor(http), Correlation(http), ct);
    }

    [Authorize(Policy = ManagementAuth.AdministratorPolicy)]
    public async Task<bool> DecommissionInstance(Guid id, GatewayDbContext db, IHttpContextAccessor http,
        CancellationToken ct)
    {
        var instance = await db.Instances.SingleOrDefaultAsync(x => x.Id == id, ct) ??
                       throw new KeyNotFoundException("Instance not found.");
        if (instance.StoppedAtUtc is null && instance.LastHeartbeatAtUtc > DateTimeOffset.UtcNow.AddMinutes(-5))
            throw new InvalidOperationException(
                "A running instance cannot be decommissioned. Stop it or wait until its heartbeat is stale.");
        db.Instances.Remove(instance);
        db.AuditEvents.Add(new AuditEvent
        {
            EnvironmentId = instance.EnvironmentId, ActorType = "User", ActorId = Actor(http),
            Action = "GatewayInstanceDecommissioned", TargetType = nameof(GatewayInstance), TargetId = id.ToString(),
            CorrelationId = Correlation(http),
            DetailsJson = JsonSerializer.Serialize(new
                { instance.InstanceId, instance.DisplayName, instance.LastHeartbeatAtUtc })
        });
        await db.SaveChangesAsync(ct);
        return true;
    }

    [Authorize(Policy = ManagementAuth.AdministratorPolicy)]
    public Task<RetentionResult> RunRetentionMaintenance(DateTimeOffset activationBeforeUtc,
        DateTimeOffset auditBeforeUtc, RetentionMaintenanceService retention, IHttpContextAccessor http,
        CancellationToken ct)
    {
        return retention.RunAsync($"manual:{Actor(http)}", activationBeforeUtc, auditBeforeUtc, Actor(http),
            Correlation(http), ct);
    }

    [Authorize(Policy = ManagementAuth.WritePolicy)]
    [GraphQLIgnore]
    public Task<OpenApiPreview> PreviewOpenApiImport(Guid draftId, Guid expectedVersion, string source,
        string clusterId, string? routeIdPrefix, OpenApiImportService importer, CancellationToken ct)
    {
        return importer.PreviewAsync(draftId, expectedVersion, source, clusterId, routeIdPrefix, ct);
    }

    [Authorize(Policy = ManagementAuth.WritePolicy)]
    [GraphQLIgnore]
    public Task<ConfigRevision> ApplyOpenApiImport(string previewToken, Guid expectedVersion,
        IReadOnlyList<OpenApiConflictResolutionInput> resolutions, OpenApiImportService importer,
        IHttpContextAccessor http, CancellationToken ct)
    {
        return importer.ApplyAsync(previewToken, expectedVersion, resolutions, Actor(http), Correlation(http), ct);
    }

    private static string Actor(IHttpContextAccessor http)
    {
        return http.HttpContext?.User.Identity?.Name ?? "development";
    }

    private static string Correlation(IHttpContextAccessor http)
    {
        return http.HttpContext?.TraceIdentifier ?? Guid.NewGuid().ToString("N");
    }
}

public sealed class GatewayErrorFilter : IErrorFilter
{
    public IError OnError(IError error)
    {
        if (error.Code is "AUTH_NOT_AUTHENTICATED") return error.WithCode("UNAUTHENTICATED");
        if (error.Code is "AUTH_NOT_AUTHORIZED") return error.WithCode("FORBIDDEN");
        if (error.Exception is null) return error;
        return error.Exception switch
        {
            ManagedRouteConflictException conflict => error.WithMessage(conflict.Message).WithCode("CONFLICT")
                .SetExtension("currentVersion", conflict.CurrentVersion),
            NamedUpstreamConflictException conflict => error.WithMessage(conflict.Message).WithCode("CONFLICT")
                .SetExtension("currentVersion", conflict.CurrentVersion),
            GatewayConfigurationConflictException conflict => error.WithMessage(conflict.Message).WithCode("CONFLICT")
                .SetExtension("currentVersion", conflict.CurrentVersion),
            GatewayRevertConflictException conflict => error.WithMessage(conflict.Message).WithCode("REVERT_CONFLICT")
                .SetExtension("paths", conflict.Paths),
            GatewayConflictException conflict => error.WithMessage(conflict.Message).WithCode("CONFLICT")
                .SetExtension("currentVersion", conflict.CurrentVersion),
            GatewayValidationException validation => error.WithMessage(validation.Message).WithCode("VALIDATION_FAILED")
                .SetExtension("issues", validation.Report.Issues),
            KeyNotFoundException => error.WithMessage("The requested resource was not found.").WithCode("NOT_FOUND"),
            ArgumentException argument => error.WithMessage(argument.Message).WithCode("VALIDATION_FAILED"),
            InvalidOperationException invalid => error.WithMessage(invalid.Message).WithCode("INVALID_STATE"),
            _ => error.WithMessage("An internal error occurred.").WithCode("INTERNAL")
        };
    }
}
