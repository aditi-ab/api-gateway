using System.Data;
using System.Text.Json;
using ApiGateway.Application;
using ApiGateway.Domain;
using Microsoft.EntityFrameworkCore;

namespace ApiGateway.Persistence;

public sealed class GatewayConflictException(Guid currentVersion) : Exception("The draft changed after it was loaded.")
{
    public Guid CurrentVersion { get; } = currentVersion;
}

public sealed class GatewayValidationException(ValidationReport report) : Exception("The configuration is invalid.")
{
    public ValidationReport Report { get; } = report;
}

public sealed class GatewayLifecycleService(
    GatewayDbContext db,
    GatewayConfigValidator validator,
    IEnumerable<IConfigurationPublicationValidator>? publicationValidators = null)
{
    private readonly IReadOnlyList<IConfigurationPublicationValidator> publicationValidators =
        publicationValidators?.ToArray() ?? [];

    public async Task<GatewayEnvironment> CreateEnvironmentAsync(string slug, string displayName, string? description,
        string actor, string correlationId, CancellationToken ct)
    {
        var normalized = slug.Trim().ToLowerInvariant();
        if (normalized.Length is < 2 or > 64 || normalized.Any(c => !(char.IsAsciiLetterOrDigit(c) || c == '-')))
            throw new ArgumentException("The environment slug is invalid.", nameof(slug));
        var environment = new GatewayEnvironment
            { Slug = normalized, DisplayName = displayName.Trim(), Description = description?.Trim() };
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        db.Environments.Add(environment);
        Audit("EnvironmentCreated", nameof(GatewayEnvironment), environment.Id, actor, correlationId, environment.Id);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return environment;
    }

    public async Task<GatewayEnvironment> UpdateEnvironmentAsync(Guid id, Guid expectedVersion, string displayName,
        string? description, string actor, string correlationId, CancellationToken ct)
    {
        var environment = await db.Environments.SingleOrDefaultAsync(x => x.Id == id, ct) ??
                          throw new KeyNotFoundException("Environment not found.");
        if (environment.ConcurrencyVersion != expectedVersion)
            throw new GatewayConflictException(environment.ConcurrencyVersion);
        var normalizedName = displayName.Trim();
        if (normalizedName.Length is < 1 or > 128)
            throw new ArgumentException("The display name must contain 1 to 128 characters.", nameof(displayName));
        environment.DisplayName = normalizedName;
        environment.Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        environment.UpdatedAtUtc = DateTimeOffset.UtcNow;
        environment.ConcurrencyVersion = Guid.NewGuid();
        Audit("EnvironmentUpdated", nameof(GatewayEnvironment), id, actor, correlationId, id);
        await db.SaveChangesAsync(ct);
        return environment;
    }

    public async Task<GatewayEnvironment> SetEnvironmentArchivedAsync(Guid id, Guid expectedVersion, bool archived,
        string actor, string correlationId, CancellationToken ct)
    {
        var environment = await db.Environments.SingleOrDefaultAsync(x => x.Id == id, ct) ??
                          throw new KeyNotFoundException("Environment not found.");
        if (environment.ConcurrencyVersion != expectedVersion)
            throw new GatewayConflictException(environment.ConcurrencyVersion);
        environment.ArchivedAtUtc = archived ? DateTimeOffset.UtcNow : null;
        environment.UpdatedAtUtc = DateTimeOffset.UtcNow;
        environment.ConcurrencyVersion = Guid.NewGuid();
        Audit(archived ? "EnvironmentArchived" : "EnvironmentRestored", nameof(GatewayEnvironment), id, actor,
            correlationId, id);
        await db.SaveChangesAsync(ct);
        return environment;
    }

    public async Task<GatewayEnvironment> SetPublishingModeAsync(Guid id, Guid expectedVersion,
        ConfigurationPublishingMode mode, string actor, string correlationId, CancellationToken ct)
    {
        var environment = await db.Environments.SingleOrDefaultAsync(x => x.Id == id, ct) ??
                          throw new KeyNotFoundException("Environment not found.");
        if (environment.ConcurrencyVersion != expectedVersion)
            throw new GatewayConflictException(environment.ConcurrencyVersion);
        if (environment.PendingRevisionId is not null)
            throw new InvalidOperationException(
                "Publish or discard the pending changes before changing publishing mode.");
        environment.PublishingMode = mode;
        environment.UpdatedAtUtc = DateTimeOffset.UtcNow;
        environment.ConcurrencyVersion = Guid.NewGuid();
        Audit("EnvironmentPublishingModeChanged", nameof(GatewayEnvironment), id, actor, correlationId, id,
            new { Mode = mode.ToString() });
        await db.SaveChangesAsync(ct);
        return environment;
    }

    public async Task DeleteEnvironmentAsync(Guid id, Guid expectedVersion, CancellationToken ct)
    {
        var environment = await db.Environments.SingleOrDefaultAsync(x => x.Id == id, ct) ??
                          throw new KeyNotFoundException("Environment not found.");
        if (environment.ConcurrencyVersion != expectedVersion)
            throw new GatewayConflictException(environment.ConcurrencyVersion);
        if (await db.Revisions.AnyAsync(x => x.EnvironmentId == id, ct) ||
            await db.AuditEvents.AnyAsync(x => x.EnvironmentId == id, ct))
            throw new InvalidOperationException(
                "Environments with revision or audit history cannot be deleted. Archive the environment instead.");
        db.Environments.Remove(environment);
        await db.SaveChangesAsync(ct);
    }

    public async Task<ConfigRevision> CreateDraftAsync(Guid environmentId, Guid? baseRevisionId, string actor,
        string correlationId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var environment = await db.Environments.SingleOrDefaultAsync(x => x.Id == environmentId, ct) ??
                          throw new KeyNotFoundException("Environment not found.");
        if (environment.ArchivedAtUtc is not null)
            throw new InvalidOperationException("Archived environments cannot accept drafts.");
        var sourceId = baseRevisionId ?? environment.ActiveRevisionId;
        var content = sourceId is null
            ? ConfigDocuments.Serialize(new GatewayConfigDocument())
            : (await db.Revisions.SingleAsync(x => x.Id == sourceId && x.EnvironmentId == environmentId, ct))
            .ConfigJson;
        var number =
            (await db.Revisions.Where(x => x.EnvironmentId == environmentId).MaxAsync(x => (long?)x.Number, ct) ?? 0) +
            1;
        var draft = new ConfigRevision
        {
            EnvironmentId = environmentId, Number = number, State = RevisionState.Draft, ConfigJson = content,
            ContentHash = ConfigDocuments.Hash(content), CreatedBy = actor
        };
        db.Revisions.Add(draft);
        Audit("DraftCreated", nameof(ConfigRevision), draft.Id, actor, correlationId, environmentId);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return draft;
    }

    public async Task<ConfigRevision> PromoteAsync(Guid sourceRevisionId, Guid targetEnvironmentId, string actor,
        string correlationId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var source =
            await db.Revisions.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == sourceRevisionId && x.State == RevisionState.Published, ct) ??
            throw new InvalidOperationException("Only published revisions can be promoted.");
        var target = await db.Environments.SingleOrDefaultAsync(x => x.Id == targetEnvironmentId, ct) ??
                     throw new KeyNotFoundException("Target environment not found.");
        if (target.ArchivedAtUtc is not null)
            throw new InvalidOperationException("Archived environments cannot accept promoted drafts.");
        var number = (await db.Revisions.Where(x => x.EnvironmentId == targetEnvironmentId)
            .MaxAsync(x => (long?)x.Number, ct) ?? 0) + 1;
        var draft = new ConfigRevision
        {
            EnvironmentId = targetEnvironmentId, Number = number, State = RevisionState.Draft,
            ConfigJson = source.ConfigJson, ContentHash = source.ContentHash, CreatedBy = actor,
            Comment = $"Promoted from revision {source.Number}."
        };
        db.Revisions.Add(draft);
        Audit("RevisionPromoted", nameof(ConfigRevision), draft.Id, actor, correlationId, targetEnvironmentId,
            new { sourceRevisionId, source.EnvironmentId });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return draft;
    }

    public async Task<ConfigRevision> SetDraftContentAsync(Guid id, Guid expectedVersion, string json, string actor,
        string correlationId, CancellationToken ct)
    {
        var draft = await DraftAsync(id, ct);
        CheckVersion(draft, expectedVersion);
        var document = ConfigDocuments.Parse(json);
        var canonical = ConfigDocuments.Serialize(document);
        draft.ConfigJson = canonical;
        draft.ContentHash = ConfigDocuments.Hash(canonical);
        draft.ConcurrencyVersion = Guid.NewGuid();
        Audit("DraftContentUpdated", nameof(ConfigRevision), id, actor, correlationId, draft.EnvironmentId);
        await db.SaveChangesAsync(ct);
        return draft;
    }

    public async Task<string> ExportRevisionAsync(Guid id, CancellationToken ct)
    {
        return (await db.Revisions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) ??
                throw new KeyNotFoundException("Revision not found.")).ConfigJson;
    }

    public Task<ConfigRevision> ImportDraftAsync(Guid id, Guid expectedVersion, string json, string actor,
        string correlationId, CancellationToken ct)
    {
        return SetDraftContentAsync(id, expectedVersion, json, actor, correlationId, ct);
    }

    public async Task<ConfigRevision> UpsertRouteAsync(Guid id, Guid expectedVersion, string routeJson, string actor,
        string correlationId, CancellationToken ct)
    {
        var draft = await DraftAsync(id, ct);
        CheckVersion(draft, expectedVersion);
        var document = ConfigDocuments.Parse(draft.ConfigJson);
        var route = JsonSerializer.Deserialize<GatewayRoute>(routeJson, GatewayJson.Options) ??
                    throw new ArgumentException("Route JSON is required.");
        var routes = document.Routes.Where(x => !x.Id.Equals(route.Id, StringComparison.OrdinalIgnoreCase))
            .Append(route).ToArray();
        return await SetDraftContentAsync(id, expectedVersion,
            ConfigDocuments.Serialize(document with { Routes = routes }), actor, correlationId, ct);
    }

    public async Task<ConfigRevision> DeleteRouteAsync(Guid id, Guid expectedVersion, string routeId, string actor,
        string correlationId, CancellationToken ct)
    {
        var draft = await DraftAsync(id, ct);
        CheckVersion(draft, expectedVersion);
        var document = ConfigDocuments.Parse(draft.ConfigJson);
        var routes = document.Routes.Where(x => !x.Id.Equals(routeId, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (routes.Length == document.Routes.Count) throw new KeyNotFoundException("Route not found.");
        return await SetDraftContentAsync(id, expectedVersion,
            ConfigDocuments.Serialize(document with { Routes = routes }), actor, correlationId, ct);
    }

    public async Task<ConfigRevision> UpsertClusterAsync(Guid id, Guid expectedVersion, string clusterJson,
        string actor, string correlationId, CancellationToken ct)
    {
        var draft = await DraftAsync(id, ct);
        CheckVersion(draft, expectedVersion);
        var document = ConfigDocuments.Parse(draft.ConfigJson);
        var cluster = JsonSerializer.Deserialize<GatewayCluster>(clusterJson, GatewayJson.Options) ??
                      throw new ArgumentException("Cluster JSON is required.");
        var clusters = document.Clusters.Where(x => !x.Id.Equals(cluster.Id, StringComparison.OrdinalIgnoreCase))
            .Append(cluster).ToArray();
        return await SetDraftContentAsync(id, expectedVersion,
            ConfigDocuments.Serialize(document with { Clusters = clusters }), actor, correlationId, ct);
    }

    public async Task<ConfigRevision> DeleteClusterAsync(Guid id, Guid expectedVersion, string clusterId, string actor,
        string correlationId, CancellationToken ct)
    {
        var draft = await DraftAsync(id, ct);
        CheckVersion(draft, expectedVersion);
        var document = ConfigDocuments.Parse(draft.ConfigJson);
        var clusters = document.Clusters.Where(x => !x.Id.Equals(clusterId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (clusters.Length == document.Clusters.Count) throw new KeyNotFoundException("Cluster not found.");
        return await SetDraftContentAsync(id, expectedVersion,
            ConfigDocuments.Serialize(document with { Clusters = clusters }), actor, correlationId, ct);
    }

    public async Task<ConfigRevision> SetPoliciesAsync(Guid id, Guid expectedVersion, string policiesJson, string actor,
        string correlationId, CancellationToken ct)
    {
        var draft = await DraftAsync(id, ct);
        CheckVersion(draft, expectedVersion);
        var document = ConfigDocuments.Parse(draft.ConfigJson);
        var policies = JsonSerializer.Deserialize<GatewayPolicies>(policiesJson, GatewayJson.Options) ??
                       throw new ArgumentException("Policies JSON is required.");
        return await SetDraftContentAsync(id, expectedVersion,
            ConfigDocuments.Serialize(document with { Policies = policies }), actor, correlationId, ct);
    }

    public async Task DeleteDraftAsync(Guid id, Guid expectedVersion, string actor, string correlationId,
        CancellationToken ct)
    {
        var draft = await DraftAsync(id, ct);
        CheckVersion(draft, expectedVersion);
        db.Revisions.Remove(draft);
        Audit("DraftDeleted", nameof(ConfigRevision), id, actor, correlationId, draft.EnvironmentId,
            new { draft.Number });
        await db.SaveChangesAsync(ct);
    }

    public async Task<ValidationReport> ValidateAsync(Guid id, CancellationToken ct)
    {
        return await ValidateDocumentAsync(
            ConfigDocuments.Parse((await db.Revisions.AsNoTracking().SingleAsync(x => x.Id == id, ct)).ConfigJson), ct);
    }

    public async Task<ConfigRevision> PublishAsync(Guid id, Guid expectedVersion, string? comment, string actor,
        string correlationId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var draft = await DraftAsync(id, ct);
        CheckVersion(draft, expectedVersion);
        var report = await ValidateDocumentAsync(ConfigDocuments.Parse(draft.ConfigJson), ct);
        if (!report.IsValid) throw new GatewayValidationException(report);
        var environment = await db.Environments.SingleAsync(x => x.Id == draft.EnvironmentId, ct);
        if (environment.ArchivedAtUtc is not null)
            throw new InvalidOperationException("Archived environments cannot publish.");
        draft.State = RevisionState.Published;
        draft.Comment = comment?.Trim();
        draft.PublishedBy = actor;
        draft.PublishedAtUtc = DateTimeOffset.UtcNow;
        environment.ActiveRevisionId = draft.Id;
        environment.UpdatedAtUtc = DateTimeOffset.UtcNow;
        environment.ConcurrencyVersion = Guid.NewGuid();
        Audit("RevisionPublished", nameof(ConfigRevision), id, actor, correlationId, draft.EnvironmentId);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return draft;
    }

    public async Task RollbackAsync(Guid environmentId, Guid targetRevisionId, string actor, string correlationId,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var environment = await db.Environments.SingleAsync(x => x.Id == environmentId, ct);
        _ = await db.Revisions.SingleOrDefaultAsync(
            x => x.Id == targetRevisionId && x.EnvironmentId == environmentId && x.State == RevisionState.Published,
            ct) ?? throw new InvalidOperationException(
            "Rollback target must be a published revision in the same environment.");
        environment.ActiveRevisionId = targetRevisionId;
        environment.UpdatedAtUtc = DateTimeOffset.UtcNow;
        environment.ConcurrencyVersion = Guid.NewGuid();
        Audit("EnvironmentRolledBack", nameof(GatewayEnvironment), environmentId, actor, correlationId, environmentId,
            new { targetRevisionId });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    private async Task<ConfigRevision> DraftAsync(Guid id, CancellationToken ct)
    {
        return await db.Revisions.SingleOrDefaultAsync(x => x.Id == id && x.State == RevisionState.Draft, ct) ??
               throw new InvalidOperationException("Draft not found or no longer editable.");
    }

    private async Task<ValidationReport> ValidateDocumentAsync(GatewayConfigDocument document, CancellationToken ct)
    {
        var issues = validator.Validate(document).Issues.ToList();
        foreach (var publicationValidator in publicationValidators)
            issues.AddRange(await publicationValidator.ValidateAsync(document, ct));
        return new ValidationReport(issues.OrderBy(x => x.JsonPath, StringComparer.Ordinal)
            .ThenBy(x => x.Code, StringComparer.Ordinal).ToArray());
    }

    private static void CheckVersion(ConfigRevision revision, Guid expected)
    {
        if (revision.ConcurrencyVersion != expected) throw new GatewayConflictException(revision.ConcurrencyVersion);
    }

    private void Audit(string action, string targetType, Guid targetId, string actor, string correlationId,
        Guid? environmentId, object? details = null)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            EnvironmentId = environmentId, ActorType = "User", ActorId = actor, Action = action,
            TargetType = targetType, TargetId = targetId.ToString(), CorrelationId = correlationId,
            DetailsJson = JsonSerializer.Serialize(details ?? new { })
        });
    }
}