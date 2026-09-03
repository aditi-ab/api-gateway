using System.Data;
using System.Text.Json;
using ApiGateway.Application;
using ApiGateway.Domain;
using Microsoft.EntityFrameworkCore;

namespace ApiGateway.Persistence;

public sealed record ConfigurationChangeResult(ConfigRevision Revision, ManagedRoute? Route);

public sealed record PendingConfigurationChange(string Kind, string? ResourceId, string Summary);

public sealed record PendingConfigurationInfo(
    Guid RevisionId,
    Guid Version,
    Guid? BaseRevisionId,
    DateTimeOffset CreatedAtUtc,
    string CreatedBy,
    IReadOnlyList<PendingConfigurationChange> Changes,
    ValidationReport Validation);

public sealed record ImportedRouteDefinition(string Id, string Path, IReadOnlyList<string> Methods);

public sealed class GatewayConfigurationService(
    GatewayDbContext db,
    GatewayConfigValidator validator,
    IEnumerable<IConfigurationPublicationValidator>? publicationValidators = null)
{
    private readonly IReadOnlyList<IConfigurationPublicationValidator> publicationValidators =
        publicationValidators?.ToArray() ?? [];

    public async Task<IReadOnlyList<ManagedRoute>> RoutesAsync(Guid environmentId, string? filter,
        CancellationToken ct)
    {
        var (_, document) = await EditableAsync(environmentId, ct);
        return document.Routes
            .Where(x => string.IsNullOrWhiteSpace(filter) ||
                        x.Id.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                        (x.Metadata.DisplayName?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false))
            .Select(x => ManagedRouteCompiler.ToManaged(document, x)).OrderBy(x => x.Name).ToArray();
    }

    public async Task<ManagedRoute?> RouteAsync(Guid environmentId, string routeId, CancellationToken ct)
    {
        var (_, document) = await EditableAsync(environmentId, ct);
        var route = document.Routes.FirstOrDefault(x => x.Id.Equals(routeId, StringComparison.OrdinalIgnoreCase));
        return route is null ? null : ManagedRouteCompiler.ToManaged(document, route);
    }

    public async Task<IReadOnlyList<NamedUpstream>> UpstreamsAsync(Guid environmentId, CancellationToken ct)
    {
        var (_, document) = await EditableAsync(environmentId, ct);
        return NamedUpstreamCompiler.List(document);
    }

    public async Task<NamedUpstream?> UpstreamAsync(Guid environmentId, string upstreamId, CancellationToken ct)
    {
        var (_, document) = await EditableAsync(environmentId, ct);
        return NamedUpstreamCompiler.Find(document, upstreamId);
    }

    public Task<ConfigurationChangeResult> CreateUpstreamAsync(Guid environmentId, SaveNamedUpstreamInput input,
        string actor, string correlationId, CancellationToken ct)
    {
        return ChangeAsync(environmentId, actor, correlationId, "UpstreamCreated", null, document =>
        {
            var changed = NamedUpstreamCompiler.Create(document, input, out var upstream);
            return (changed, null, $"Created upstream {upstream.Name}", upstream.Id);
        }, null, ct, "Upstream");
    }

    public Task<ConfigurationChangeResult> UpdateUpstreamAsync(Guid environmentId, string upstreamId,
        string expectedUpstreamVersion, SaveNamedUpstreamInput input, string actor, string correlationId,
        CancellationToken ct)
    {
        return ChangeAsync(environmentId, actor, correlationId, "UpstreamUpdated", null, document =>
        {
            var changed = NamedUpstreamCompiler.Update(document, upstreamId, expectedUpstreamVersion, input,
                out var upstream);
            return (changed, null, $"Updated upstream {upstream.Name}", upstream.Id);
        }, null, ct, "Upstream");
    }

    public Task<ConfigurationChangeResult> DeleteUpstreamAsync(Guid environmentId, string upstreamId,
        string expectedUpstreamVersion, string actor, string correlationId, CancellationToken ct)
    {
        return ChangeAsync(environmentId, actor, correlationId, "UpstreamDeleted", null, document =>
        {
            var changed = NamedUpstreamCompiler.Delete(document, upstreamId, expectedUpstreamVersion,
                out var upstream);
            return (changed, null, $"Deleted upstream {upstream.Name}", upstream.Id);
        }, null, ct, "Upstream");
    }

    public async Task<IReadOnlyList<RouteUnavailableResponseProfile>> UnavailableResponseProfilesAsync(
        Guid environmentId, CancellationToken ct)
    {
        var (_, document) = await EditableAsync(environmentId, ct);
        return document.UnavailableResponseProfiles.Values.OrderBy(x => x.Name).ToArray();
    }

    public async Task<RouteOperationalDefaults> OperationalDefaultsAsync(Guid environmentId, CancellationToken ct)
    {
        var (_, document) = await EditableAsync(environmentId, ct);
        return document.OperationalDefaults;
    }

    public Task<ConfigurationChangeResult> CreateRouteAsync(Guid environmentId, CreateManagedRouteInput input,
        string actor, string correlationId, CancellationToken ct)
    {
        return ChangeAsync(environmentId, actor, correlationId,
            "RouteCreated", null, document =>
            {
                var changed = ManagedRouteCompiler.Create(document, input, out var route);
                return (changed, route, $"Created route {route.Name}", route.Id);
            }, null, ct);
    }

    public Task<ConfigurationChangeResult> UpdateRouteAsync(Guid environmentId, string routeId,
        string expectedRouteVersion, UpdateManagedRouteInput input, string actor, string correlationId,
        CancellationToken ct)
    {
        return ChangeAsync(environmentId, actor, correlationId, "RouteUpdated", null, document =>
        {
            var changed = ManagedRouteCompiler.Update(document, routeId, expectedRouteVersion, input, out var route);
            return (changed, route, $"Updated route {route.Name}", route.Id);
        }, null, ct);
    }

    public Task<ConfigurationChangeResult> DuplicateRouteAsync(Guid environmentId, string routeId,
        string expectedRouteVersion, string name, string actor, string correlationId, CancellationToken ct)
    {
        return ChangeAsync(environmentId, actor, correlationId, "RouteDuplicated", null, document =>
        {
            var changed = ManagedRouteCompiler.Duplicate(document, routeId, expectedRouteVersion, name,
                out var route);
            return (changed, route, $"Duplicated route as {route.Name}", route.Id);
        }, null, ct);
    }

    public Task<ConfigurationChangeResult> UpdateRouteFeaturesAsync(Guid environmentId, string routeId,
        string expectedRouteVersion, ManagedRouteFeatures features, string actor, string correlationId,
        CancellationToken ct)
    {
        return ChangeAsync(environmentId, actor, correlationId, "RouteFeaturesUpdated", null,
            document =>
            {
                var existing = document.Routes.SingleOrDefault(x => x.Id.Equals(routeId,
                    StringComparison.OrdinalIgnoreCase)) ?? throw new KeyNotFoundException("Route not found.");
                var current = ManagedRouteCompiler.ToManaged(document, existing);
                var input = new UpdateManagedRouteInput(current.Name, current.Enabled, current.Match, current.Upstream,
                    features, current.Order, current.Operations, current.Metadata, current.Inbound);
                var changed =
                    ManagedRouteCompiler.Update(document, routeId, expectedRouteVersion, input, out var route);
                return (changed, route, $"Updated features for route {route.Name}", route.Id);
            }, null, ct);
    }

    public Task<ConfigurationChangeResult> SetRouteEnabledAsync(Guid environmentId, string routeId,
        string expectedRouteVersion, bool enabled, string actor, string correlationId, CancellationToken ct)
    {
        return ImmediateRouteChangeAsync(environmentId, routeId, expectedRouteVersion, actor, correlationId,
            "RouteUpdated", (document, routeVersion) =>
            {
                var existing = document.Routes.SingleOrDefault(x => x.Id.Equals(routeId,
                    StringComparison.OrdinalIgnoreCase)) ?? throw new KeyNotFoundException("Route not found.");
                var current = ManagedRouteCompiler.ToManaged(document, existing);
                var input = new UpdateManagedRouteInput(current.Name, enabled, current.Match, current.Upstream,
                    current.Features, current.Order, current.Operations, current.Metadata, current.Inbound);
                var changed = ManagedRouteCompiler.Update(document, routeId, routeVersion, input, out var route);
                var action = enabled ? "Enabled" : "Disabled";
                return (changed, route, $"{action} route {route.Name}", route.Id);
            }, ct);
    }

    public Task<ConfigurationChangeResult> UpdateRouteBasicsAsync(Guid environmentId, string routeId,
        string expectedRouteVersion, UpdateManagedRouteBasicsInput input, string actor, string correlationId,
        CancellationToken ct)
    {
        return ChangeAsync(environmentId, actor, correlationId, "RouteUpdated", null, document =>
        {
            var existing = document.Routes.SingleOrDefault(x => x.Id.Equals(routeId,
                StringComparison.OrdinalIgnoreCase)) ?? throw new KeyNotFoundException("Route not found.");
            var current = ManagedRouteCompiler.ToManaged(document, existing);
            var namedUpstream = input.UpstreamId is null
                ? null
                : NamedUpstreamCompiler.Find(document, input.UpstreamId) ??
                  throw new KeyNotFoundException("Upstream not found.");
            var destinations = namedUpstream?.Destinations.ToDictionary() ??
                               (input.Destinations ?? current.Upstream.Destinations ??
                                   new Dictionary<string, GatewayDestination>())
                               .ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
            if (namedUpstream is null)
            {
                var primaryKey = destinations.Keys.FirstOrDefault() ?? "primary";
                destinations[primaryKey] = destinations.TryGetValue(primaryKey, out var primary)
                    ? primary with { Address = input.UpstreamUrl! }
                    : new GatewayDestination(input.UpstreamUrl!);
            }
            var match = current.Match with
            {
                Path = input.Path,
                Methods = input.Methods ?? current.Match.Methods,
                Hosts = input.Hosts ?? current.Match.Hosts,
                Headers = input.Headers ?? current.Match.Headers,
                QueryParameters = input.QueryParameters ?? current.Match.QueryParameters
            };
            var upstream = namedUpstream is null
                ? current.Upstream with
                {
                    Url = input.UpstreamUrl!,
                    Destinations = destinations,
                    LoadBalancingPolicy = input.LoadBalancingPolicy ?? current.Upstream.LoadBalancingPolicy,
                    HttpClient = input.HttpClient ?? current.Upstream.HttpClient,
                    UpstreamId = null,
                    UpstreamName = null
                }
                : new ManagedUpstream(namedUpstream.Destinations.Values.First().Address,
                    namedUpstream.Destinations, namedUpstream.LoadBalancingPolicy, namedUpstream.Health,
                    namedUpstream.SessionAffinity, namedUpstream.Traffic, namedUpstream.Tls, namedUpstream.HttpClient,
                    namedUpstream.Id, namedUpstream.Name);
            var features = current.Features;
            if (input.PathHandling is not null)
            {
                var transforms = (features.Transforms ?? [])
                    .Where(x => !x.ContainsKey("PathRemovePrefix"))
                    .Select(x => (IReadOnlyDictionary<string, string>)new Dictionary<string, string>(x))
                    .ToList();
                if (input.PathHandling == UpstreamPathHandling.StripPrefix)
                {
                    if (string.IsNullOrWhiteSpace(input.PathPrefixToRemove) ||
                        !input.PathPrefixToRemove.StartsWith('/'))
                        throw new ArgumentException("The path prefix to remove must begin with '/'.");
                    var prefix = input.PathPrefixToRemove.Trim();
                    if (prefix.Length > 1)
                        prefix = prefix.TrimEnd('/');
                    transforms.Add(new Dictionary<string, string>
                        { ["PathRemovePrefix"] = prefix });
                }

                features = features with { Transforms = transforms };
            }

            if (input.PreserveOriginalHost is not null)
            {
                var transforms = (features.Transforms ?? [])
                    .Where(x => !x.ContainsKey("RequestHeaderOriginalHost"))
                    .Select(x => (IReadOnlyDictionary<string, string>)new Dictionary<string, string>(x))
                    .ToList();
                if (input.PreserveOriginalHost.Value)
                    transforms.Add(new Dictionary<string, string> { ["RequestHeaderOriginalHost"] = "true" });

                features = features with { Transforms = transforms };
            }

            var update = new UpdateManagedRouteInput(input.Name, input.Enabled, match, upstream, features,
                input.Order, current.Operations, current.Metadata, input.Inbound ?? current.Inbound);
            var changed = ManagedRouteCompiler.Update(document, routeId, expectedRouteVersion, update, out var route);
            return (changed, route, $"Updated route {route.Name}", route.Id);
        }, null, ct);
    }

    public Task<ConfigurationChangeResult> SetRouteOperationalStateAsync(Guid environmentId, string routeId,
        string expectedRouteVersion, UpdateRouteOperationalStateInput input, string actor, string correlationId,
        CancellationToken ct)
    {
        return ImmediateRouteChangeAsync(environmentId, routeId, expectedRouteVersion, actor, correlationId,
            "RouteOperationalStateChanged", (document, routeVersion) =>
            {
                var existing = document.Routes.SingleOrDefault(x => x.Id.Equals(routeId,
                    StringComparison.OrdinalIgnoreCase)) ?? throw new KeyNotFoundException("Route not found.");
                var current = ManagedRouteCompiler.ToManaged(document, existing);
                ValidateOperationalState(input);
                if (input.ResponseProfileId is not null &&
                    !document.UnavailableResponseProfiles.ContainsKey(input.ResponseProfileId))
                    throw new ArgumentException($"Response profile '{input.ResponseProfileId}' does not exist.");
                var response = input.State == RouteOperationalState.Online || input.UseEnvironmentDefault ||
                               input.ResponseProfileId is not null
                    ? null
                    : new RouteUnavailableResponse(input.StatusCode, NullIfWhiteSpace(input.Title),
                        NullIfWhiteSpace(input.Message), input.RetryAfter, NullIfWhiteSpace(input.UpstreamUrl));
                var operations = new RouteOperationalPolicy(input.State, response,
                    input.State == RouteOperationalState.Online ? null : NullIfWhiteSpace(input.ResponseProfileId));
                var update = new UpdateManagedRouteInput(current.Name, current.Enabled, current.Match, current.Upstream,
                    current.Features, current.Order, operations, current.Metadata, current.Inbound);
                var changed = ManagedRouteCompiler.Update(document, routeId, routeVersion, update, out var route);
                var summary = input.State switch
                {
                    RouteOperationalState.Online => $"Returned route {route.Name} online",
                    RouteOperationalState.Draining => $"Started draining route {route.Name}",
                    RouteOperationalState.Maintenance => $"Put route {route.Name} under maintenance",
                    _ => $"Took route {route.Name} offline"
                };
                return (changed, route, summary, route.Id);
            }, ct);
    }

    private async Task<ConfigurationChangeResult> ImmediateRouteChangeAsync(Guid environmentId, string routeId,
        string expectedRouteVersion, string actor, string correlationId, string kind,
        Func<GatewayConfigDocument, string,
            (GatewayConfigDocument Document, ManagedRoute? Route, string Summary, string? ResourceId)> mutate,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var environment = await db.Environments.SingleOrDefaultAsync(x => x.Id == environmentId, ct) ??
                          throw new KeyNotFoundException("Environment not found.");
        if (environment.ArchivedAtUtc is not null)
            throw new InvalidOperationException("Archived environments cannot be changed.");
        var active = environment.ActiveRevisionId is null
            ? null
            : await db.Revisions.SingleAsync(x => x.Id == environment.ActiveRevisionId, ct);
        var pending = environment.PendingRevisionId is null
            ? null
            : await db.Revisions.SingleAsync(x => x.Id == environment.PendingRevisionId, ct);
        var activeDocument = active is null ? new GatewayConfigDocument() : ConfigDocuments.Parse(active.ConfigJson);
        var editableDocument = pending is null ? activeDocument : ConfigDocuments.Parse(pending.ConfigJson);
        var editableRoute = editableDocument.Routes.SingleOrDefault(x =>
                                x.Id.Equals(routeId, StringComparison.OrdinalIgnoreCase)) ??
                            throw new KeyNotFoundException("Route not found.");
        var editableVersion = ManagedRouteCompiler.ToManaged(editableDocument, editableRoute).Version;
        if (!editableVersion.Equals(expectedRouteVersion, StringComparison.Ordinal))
            throw new ManagedRouteConflictException(editableVersion);

        var activeRoute = activeDocument.Routes.SingleOrDefault(x =>
            x.Id.Equals(routeId, StringComparison.OrdinalIgnoreCase));
        if (activeRoute is null)
            throw new InvalidOperationException(
                "This route must be published before its operational state can change.");
        var activeVersion = ManagedRouteCompiler.ToManaged(activeDocument, activeRoute).Version;
        var (changedActive, activeResultRoute, summary, resourceId) = mutate(activeDocument, activeVersion);
        var activeReport = await ValidateAsync(changedActive, ct);
        if (!activeReport.IsValid) throw new GatewayValidationException(activeReport);

        var resultRoute = activeResultRoute;
        if (pending is not null)
        {
            var (changedPending, pendingResultRoute, _, _) = mutate(editableDocument, expectedRouteVersion);
            var pendingReport = await ValidateAsync(changedPending, ct);
            if (!pendingReport.IsValid) throw new GatewayValidationException(pendingReport);
            var pendingJson = ConfigDocuments.Serialize(changedPending);
            pending.ConfigJson = pendingJson;
            pending.ContentHash = ConfigDocuments.Hash(pendingJson);
            pending.ConcurrencyVersion = Guid.NewGuid();
            resultRoute = pendingResultRoute;
        }

        var json = ConfigDocuments.Serialize(changedActive);
        var now = DateTimeOffset.UtcNow;
        var revisionNumber = (await db.Revisions.Where(x => x.EnvironmentId == environmentId)
            .MaxAsync(x => (long?)x.Number, ct) ?? 0) + 1;
        var revision = new ConfigRevision
        {
            EnvironmentId = environmentId, Number = revisionNumber, State = RevisionState.Published,
            ConfigJson = json, ContentHash = ConfigDocuments.Hash(json), CreatedBy = actor, CreatedAtUtc = now,
            PublishedBy = actor, PublishedAtUtc = now, ParentRevisionId = active?.Id, ChangeKind = kind,
            ChangeSummary = summary, ChangedResourceType = "Route", ChangedResourceId = resourceId,
            Comment = summary
        };
        db.Revisions.Add(revision);
        environment.ActiveRevisionId = revision.Id;
        environment.UpdatedAtUtc = now;
        environment.ConcurrencyVersion = Guid.NewGuid();
        if (pending is not null) pending.ParentRevisionId = revision.Id;
        db.AuditEvents.Add(new AuditEvent
        {
            EnvironmentId = environmentId, ActorType = "Management", ActorId = actor, Action = kind,
            TargetType = "Route", TargetId = resourceId ?? routeId,
            DetailsJson = JsonSerializer.Serialize(new { revision.Id, revision.Number, summary, immediate = true }),
            CorrelationId = correlationId
        });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new ConfigurationChangeResult(revision, resultRoute);
    }

    public Task<ConfigurationChangeResult> SaveUnavailableResponseProfileAsync(Guid environmentId,
        Guid? expectedConfigurationVersion, SaveRouteUnavailableResponseProfileInput input, string actor,
        string correlationId, CancellationToken ct)
    {
        return ChangeAsync(environmentId, actor, correlationId, "UnavailableResponseProfileSaved", null, document =>
        {
            if (string.IsNullOrWhiteSpace(input.Name))
                throw new ArgumentException("Response profile name is required.");
            ValidateUnavailableResponse(input.StatusCode, input.RetryAfter, input.UpstreamUrl);
            var id = NullIfWhiteSpace(input.Id) ?? UniqueProfileId(document, input.Name);
            if (input.Id is not null && !document.UnavailableResponseProfiles.ContainsKey(id))
                throw new KeyNotFoundException("Response profile not found.");
            var response = new RouteUnavailableResponse(input.StatusCode, NullIfWhiteSpace(input.Title),
                NullIfWhiteSpace(input.Message), input.RetryAfter, NullIfWhiteSpace(input.UpstreamUrl));
            var profile = new RouteUnavailableResponseProfile(id, input.Name.Trim(), response);
            var profiles = document.UnavailableResponseProfiles.ToDictionary();
            profiles[id] = profile;
            var changed = document with { SchemaVersion = 2, UnavailableResponseProfiles = profiles };
            return (changed, null, $"Saved unavailable response {profile.Name}", null);
        }, expectedConfigurationVersion, ct);
    }

    public Task<ConfigurationChangeResult> DeleteUnavailableResponseProfileAsync(Guid environmentId,
        Guid? expectedConfigurationVersion, string profileId, string actor, string correlationId,
        CancellationToken ct)
    {
        return ChangeAsync(environmentId, actor, correlationId, "UnavailableResponseProfileDeleted", null, document =>
        {
            if (!document.UnavailableResponseProfiles.TryGetValue(profileId, out var profile))
                throw new KeyNotFoundException("Response profile not found.");
            if (document.Routes.Any(x => x.Operations.ResponseProfileId == profileId) ||
                document.OperationalDefaults.For(RouteOperationalState.Draining) == profileId ||
                document.OperationalDefaults.For(RouteOperationalState.Maintenance) == profileId ||
                document.OperationalDefaults.For(RouteOperationalState.Offline) == profileId)
                throw new InvalidOperationException(
                    "The response profile is in use by a route or environment default.");
            var profiles = document.UnavailableResponseProfiles.Where(x => x.Key != profileId).ToDictionary();
            return (document with { UnavailableResponseProfiles = profiles }, null,
                $"Deleted unavailable response {profile.Name}", null);
        }, expectedConfigurationVersion, ct);
    }

    public Task<ConfigurationChangeResult> UpdateOperationalDefaultsAsync(Guid environmentId,
        Guid? expectedConfigurationVersion, UpdateRouteOperationalDefaultsInput input, string actor,
        string correlationId, CancellationToken ct)
    {
        return ChangeAsync(environmentId, actor, correlationId, "RouteOperationalDefaultsUpdated", null, document =>
        {
            var defaults = new RouteOperationalDefaults(NullIfWhiteSpace(input.DrainingProfileId),
                NullIfWhiteSpace(input.MaintenanceProfileId), NullIfWhiteSpace(input.OfflineProfileId));
            foreach (var id in new[]
                         { defaults.DrainingProfileId, defaults.MaintenanceProfileId, defaults.OfflineProfileId })
                if (id is not null && !document.UnavailableResponseProfiles.ContainsKey(id))
                    throw new ArgumentException($"Response profile '{id}' does not exist.");
            return (document with { SchemaVersion = 2, OperationalDefaults = defaults }, null,
                "Updated route traffic-state defaults", null);
        }, expectedConfigurationVersion, ct);
    }

    private static string UniqueProfileId(GatewayConfigDocument document, string name)
    {
        var root = string.Concat(name.Trim().ToLowerInvariant().Select(x => char.IsLetterOrDigit(x) ? x : '-'))
            .Trim('-');
        if (string.IsNullOrEmpty(root)) root = "response";
        var id = root;
        for (var suffix = 2; document.UnavailableResponseProfiles.ContainsKey(id); suffix++) id = $"{root}-{suffix}";
        return id;
    }

    private static string? NullIfWhiteSpace(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static void ValidateOperationalState(UpdateRouteOperationalStateInput input)
    {
        if (input.State == RouteOperationalState.Online) return;
        if (input.UseEnvironmentDefault || input.ResponseProfileId is not null) return;
        ValidateUnavailableResponse(input.StatusCode, input.RetryAfter, input.UpstreamUrl);
    }

    private static void ValidateUnavailableResponse(int statusCode, TimeSpan? retryAfter, string? upstreamUrl)
    {
        if (statusCode is < 400 or > 599)
            throw new ArgumentException("The unavailable response status code must be between 400 and 599.");
        if (retryAfter <= TimeSpan.Zero)
            throw new ArgumentException("Retry-After must be positive when configured.");
        if (upstreamUrl is not null &&
            (!Uri.TryCreate(upstreamUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")))
            throw new ArgumentException("The maintenance upstream must be an absolute HTTP or HTTPS URL.");
    }

    public Task<ConfigurationChangeResult> DeleteRouteAsync(Guid environmentId, string routeId,
        string expectedRouteVersion, string actor, string correlationId, CancellationToken ct)
    {
        return ChangeAsync(environmentId, actor, correlationId, "RouteDeleted", null, document =>
        {
            var changed = ManagedRouteCompiler.Delete(document, routeId, expectedRouteVersion, out var route);
            return (changed, null, $"Deleted route {route.Name}", route.Id);
        }, null, ct);
    }

    public async Task<ConfigurationChangeResult> RevertAsync(Guid environmentId, Guid changeId,
        Guid expectedConfigurationVersion, string actor, string correlationId, CancellationToken ct)
    {
        var selected = await db.Revisions.AsNoTracking().SingleOrDefaultAsync(
                           x => x.Id == changeId && x.EnvironmentId == environmentId &&
                                x.State == RevisionState.Published, ct) ??
                       throw new KeyNotFoundException("Configuration change not found.");
        if (string.IsNullOrWhiteSpace(selected.ChangedResourceId))
            throw new InvalidOperationException("This historical entry cannot be reverted as an individual change.");
        var parent = selected.ParentRevisionId is null
            ? null
            : await db.Revisions.AsNoTracking().SingleAsync(x => x.Id == selected.ParentRevisionId, ct);
        var beforeDocument = parent is null ? new GatewayConfigDocument() : ConfigDocuments.Parse(parent.ConfigJson);
        var afterDocument = ConfigDocuments.Parse(selected.ConfigJson);
        var routeId = selected.ChangedResourceId;
        return await ChangeAsync(environmentId, actor, correlationId, "ChangeReverted", selected.Id, current =>
        {
            var beforeRoute = beforeDocument.Routes.FirstOrDefault(x => x.Id == routeId);
            var afterRoute = afterDocument.Routes.FirstOrDefault(x => x.Id == routeId);
            var currentRoute = current.Routes.FirstOrDefault(x => x.Id == routeId);
            if (!Equivalent(afterDocument, afterRoute, current, currentRoute))
                throw new GatewayRevertConflictException([routeId]);
            if (beforeRoute is null && currentRoute is not null)
            {
                var currentManaged = ManagedRouteCompiler.ToManaged(current, currentRoute);
                var changed = ManagedRouteCompiler.Delete(current, routeId, currentManaged.Version, out _);
                return (changed, null, $"Reverted creation of route {routeId}", routeId);
            }

            if (beforeRoute is null)
                throw new GatewayRevertConflictException([routeId]);
            var beforeManaged = ManagedRouteCompiler.ToManaged(beforeDocument, beforeRoute);
            var restoredDocument = ManagedRouteCompiler.Restore(current, beforeManaged, out var restored);
            return (restoredDocument, restored, $"Reverted change to route {restored.Name}", routeId);
        }, expectedConfigurationVersion, ct);
    }

    public Task<ConfigurationChangeResult> RestoreSnapshotAsync(Guid environmentId, Guid revisionId,
        Guid expectedConfigurationVersion, string actor, string correlationId, CancellationToken ct)
    {
        return ChangeAsync(environmentId, actor, correlationId, "SnapshotRestored", revisionId, current =>
        {
            var snapshot = db.Revisions.AsNoTracking().SingleOrDefault(x => x.Id == revisionId &&
                                                                            x.EnvironmentId == environmentId &&
                                                                            x.State == RevisionState.Published);
            var selected = snapshot ?? throw new KeyNotFoundException("Configuration snapshot not found.");
            return (ConfigDocuments.Parse(selected.ConfigJson), null,
                $"Restored configuration snapshot {selected.Number}", null);
        }, expectedConfigurationVersion, ct);
    }

    public async Task<string> ExportActiveAsync(Guid environmentId, CancellationToken ct)
    {
        var (_, document) = await ActiveAsync(environmentId, ct);
        return ConfigDocuments.Serialize(document);
    }

    public Task<ConfigurationChangeResult> ImportAsync(Guid environmentId, Guid expectedConfigurationVersion,
        string json, string actor, string correlationId, CancellationToken ct)
    {
        return ChangeAsync(environmentId, actor,
            correlationId, "ConfigurationImported", null, _ =>
            {
                var document = ConfigDocuments.Parse(json);
                return (document, null, "Imported configuration", null);
            }, expectedConfigurationVersion, ct);
    }

    public async Task<ConfigurationChangeResult> CopyAsync(Guid sourceEnvironmentId, Guid targetEnvironmentId,
        Guid expectedTargetVersion, string actor, string correlationId, CancellationToken ct)
    {
        var (_, source) = await ActiveAsync(sourceEnvironmentId, ct);
        return await ChangeAsync(targetEnvironmentId, actor, correlationId, "ConfigurationCopied", null,
            _ => (source, null, "Copied configuration from another environment", null),
            expectedTargetVersion, ct);
    }

    public Task<ConfigurationChangeResult> ImportRoutesAsync(Guid environmentId, Guid expectedConfigurationVersion,
        IReadOnlyList<ImportedRouteDefinition> routes, string upstreamUrl, string actor, string correlationId,
        CancellationToken ct)
    {
        return ChangeAsync(environmentId, actor, correlationId, "OpenApiRoutesImported", null,
            document =>
            {
                var changed = document;
                foreach (var definition in routes)
                {
                    if (changed.Routes.Any(x => x.Id.Equals(definition.Id, StringComparison.OrdinalIgnoreCase)))
                        throw new InvalidOperationException($"Route '{definition.Id}' already exists.");
                    changed = ManagedRouteCompiler.Create(changed,
                        new CreateManagedRouteInput(definition.Id, definition.Path, upstreamUrl), out var created);
                    if (definition.Methods.Count == 0) continue;
                    var input = new UpdateManagedRouteInput(created.Name, created.Enabled,
                        created.Match with { Methods = definition.Methods }, created.Upstream, created.Features,
                        created.Order, created.Operations, created.Metadata);
                    changed = ManagedRouteCompiler.Update(changed, created.Id, created.Version, input, out _);
                }

                return (changed, null, $"Imported {routes.Count} routes from OpenAPI", null);
            }, expectedConfigurationVersion, ct);
    }

    private async Task<ConfigurationChangeResult> ChangeAsync(Guid environmentId, string actor, string correlationId,
        string kind, Guid? reverts, Func<GatewayConfigDocument,
            (GatewayConfigDocument Document, ManagedRoute? Route, string Summary, string? ResourceId)> mutate,
        Guid? expectedConfigurationVersion, CancellationToken ct, string resourceType = "Route")
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var environment = await db.Environments.SingleOrDefaultAsync(x => x.Id == environmentId, ct) ??
                          throw new KeyNotFoundException("Environment not found.");
        if (environment.ArchivedAtUtc is not null)
            throw new InvalidOperationException("Archived environments cannot be changed.");
        var effectiveRevisionId = environment.PublishingMode == ConfigurationPublishingMode.Staged
            ? environment.PendingRevisionId ?? environment.ActiveRevisionId
            : environment.ActiveRevisionId;
        if (expectedConfigurationVersion is not null && effectiveRevisionId != expectedConfigurationVersion)
            throw new GatewayConfigurationConflictException(effectiveRevisionId);
        var active = environment.ActiveRevisionId is null
            ? null
            : await db.Revisions.SingleAsync(x => x.Id == environment.ActiveRevisionId, ct);
        var pending = environment.PendingRevisionId is null
            ? null
            : await db.Revisions.SingleAsync(x => x.Id == environment.PendingRevisionId, ct);
        var source = environment.PublishingMode == ConfigurationPublishingMode.Staged ? pending ?? active : active;
        var current = source is null ? new GatewayConfigDocument() : ConfigDocuments.Parse(source.ConfigJson);
        var (document, route, summary, resourceId) = mutate(current);
        var report = await ValidateAsync(document, ct);
        if (!report.IsValid) throw new GatewayValidationException(report);
        var json = ConfigDocuments.Serialize(document);
        var now = DateTimeOffset.UtcNow;

        if (environment.PublishingMode == ConfigurationPublishingMode.Staged)
        {
            if (pending is null)
            {
                var number = (await db.Revisions.Where(x => x.EnvironmentId == environmentId)
                    .MaxAsync(x => (long?)x.Number, ct) ?? 0) + 1;
                pending = new ConfigRevision
                {
                    EnvironmentId = environmentId, Number = number, State = RevisionState.Draft, ConfigJson = json,
                    ContentHash = ConfigDocuments.Hash(json), CreatedBy = actor, CreatedAtUtc = now,
                    ParentRevisionId = active?.Id, ChangeKind = "PendingChanges", ChangeSummary = "Pending changes"
                };
                db.Revisions.Add(pending);
                environment.PendingRevisionId = pending.Id;
            }
            else
            {
                pending.ConfigJson = json;
                pending.ContentHash = ConfigDocuments.Hash(json);
                pending.ConcurrencyVersion = Guid.NewGuid();
            }

            environment.UpdatedAtUtc = now;
            environment.ConcurrencyVersion = Guid.NewGuid();
            db.AuditEvents.Add(new AuditEvent
            {
                EnvironmentId = environmentId, ActorType = "Management", ActorId = actor, Action = $"{kind}Staged",
                TargetType = resourceId is null ? "Configuration" : resourceType,
                TargetId = resourceId ?? pending.Id.ToString(),
                DetailsJson = JsonSerializer.Serialize(new { pendingRevisionId = pending.Id, summary }),
                CorrelationId = correlationId
            });
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return new ConfigurationChangeResult(pending, route);
        }

        var revisionNumber = (await db.Revisions.Where(x => x.EnvironmentId == environmentId)
            .MaxAsync(x => (long?)x.Number, ct) ?? 0) + 1;
        var revision = new ConfigRevision
        {
            EnvironmentId = environmentId, Number = revisionNumber, State = RevisionState.Published, ConfigJson = json,
            ContentHash = ConfigDocuments.Hash(json), CreatedBy = actor, CreatedAtUtc = now, PublishedBy = actor,
            PublishedAtUtc = now, ParentRevisionId = active?.Id, ChangeKind = kind, ChangeSummary = summary,
            ChangedResourceType = resourceId is null ? "Configuration" : resourceType, ChangedResourceId = resourceId,
            RevertsRevisionId = reverts, Comment = summary
        };
        db.Revisions.Add(revision);
        environment.ActiveRevisionId = revision.Id;
        environment.UpdatedAtUtc = now;
        environment.ConcurrencyVersion = Guid.NewGuid();
        db.AuditEvents.Add(new AuditEvent
        {
            EnvironmentId = environmentId, ActorType = "Management", ActorId = actor, Action = kind,
            TargetType = revision.ChangedResourceType, TargetId = resourceId ?? revision.Id.ToString(),
            DetailsJson = JsonSerializer.Serialize(new { revision.Id, revision.Number, summary }),
            CorrelationId = correlationId
        });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new ConfigurationChangeResult(revision, route);
    }

    public async Task<PendingConfigurationInfo?> PendingAsync(Guid environmentId, CancellationToken ct)
    {
        var environment = await db.Environments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == environmentId, ct) ??
                          throw new KeyNotFoundException("Environment not found.");
        if (environment.PendingRevisionId is null) return null;
        var pending = await db.Revisions.AsNoTracking().SingleAsync(x => x.Id == environment.PendingRevisionId, ct);
        var activeDocument = environment.ActiveRevisionId is null
            ? new GatewayConfigDocument()
            : ConfigDocuments.Parse((await db.Revisions.AsNoTracking()
                .SingleAsync(x => x.Id == environment.ActiveRevisionId, ct)).ConfigJson);
        var pendingDocument = ConfigDocuments.Parse(pending.ConfigJson);
        return new PendingConfigurationInfo(pending.Id, pending.ConcurrencyVersion, pending.ParentRevisionId,
            pending.CreatedAtUtc, pending.CreatedBy, DescribeChanges(activeDocument, pendingDocument),
            await ValidateAsync(pendingDocument, ct));
    }

    public async Task<ConfigRevision> PublishPendingAsync(Guid environmentId, Guid expectedVersion, string? comment,
        string actor, string correlationId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var environment = await db.Environments.SingleOrDefaultAsync(x => x.Id == environmentId, ct) ??
                          throw new KeyNotFoundException("Environment not found.");
        if (environment.PendingRevisionId is null)
            throw new InvalidOperationException("There are no pending changes to publish.");
        var pending = await db.Revisions.SingleAsync(x => x.Id == environment.PendingRevisionId, ct);
        if (pending.ConcurrencyVersion != expectedVersion)
            throw new GatewayConflictException(pending.ConcurrencyVersion);
        if (pending.ParentRevisionId != environment.ActiveRevisionId)
            throw new GatewayConfigurationConflictException(environment.ActiveRevisionId);
        var document = ConfigDocuments.Parse(pending.ConfigJson);
        var report = await ValidateAsync(document, ct);
        if (!report.IsValid) throw new GatewayValidationException(report);
        var activeDocument = environment.ActiveRevisionId is null
            ? new GatewayConfigDocument()
            : ConfigDocuments.Parse((await db.Revisions.SingleAsync(x => x.Id == environment.ActiveRevisionId, ct))
                .ConfigJson);
        var changes = DescribeChanges(activeDocument, document);
        var now = DateTimeOffset.UtcNow;
        pending.Number = (await db.Revisions.Where(x => x.EnvironmentId == environmentId && x.Id != pending.Id)
            .MaxAsync(x => (long?)x.Number, ct) ?? 0) + 1;
        pending.State = RevisionState.Published;
        pending.PublishedBy = actor;
        pending.PublishedAtUtc = now;
        pending.Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        pending.ChangeKind = "ChangeSetPublished";
        pending.ChangeSummary = changes.Count == 1 ? changes[0].Summary : $"Published {changes.Count} changes";
        pending.ChangedResourceType = "Configuration";
        pending.ChangedResourceId = null;
        pending.ConcurrencyVersion = Guid.NewGuid();
        environment.ActiveRevisionId = pending.Id;
        environment.PendingRevisionId = null;
        environment.UpdatedAtUtc = now;
        environment.ConcurrencyVersion = Guid.NewGuid();
        db.AuditEvents.Add(new AuditEvent
        {
            EnvironmentId = environmentId, ActorType = "Management", ActorId = actor,
            Action = "ConfigurationChangesPublished", TargetType = "Configuration", TargetId = pending.Id.ToString(),
            DetailsJson = JsonSerializer.Serialize(new { pending.Id, pending.Number, changes }),
            CorrelationId = correlationId
        });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return pending;
    }

    public async Task<bool> DiscardPendingAsync(Guid environmentId, Guid expectedVersion, string actor,
        string correlationId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var environment = await db.Environments.SingleOrDefaultAsync(x => x.Id == environmentId, ct) ??
                          throw new KeyNotFoundException("Environment not found.");
        if (environment.PendingRevisionId is null) return false;
        var pending = await db.Revisions.SingleAsync(x => x.Id == environment.PendingRevisionId, ct);
        if (pending.ConcurrencyVersion != expectedVersion)
            throw new GatewayConflictException(pending.ConcurrencyVersion);
        pending.State = RevisionState.Abandoned;
        pending.ConcurrencyVersion = Guid.NewGuid();
        environment.PendingRevisionId = null;
        environment.UpdatedAtUtc = DateTimeOffset.UtcNow;
        environment.ConcurrencyVersion = Guid.NewGuid();
        db.AuditEvents.Add(new AuditEvent
        {
            EnvironmentId = environmentId, ActorType = "Management", ActorId = actor,
            Action = "PendingConfigurationDiscarded", TargetType = "Configuration", TargetId = pending.Id.ToString(),
            CorrelationId = correlationId
        });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return true;
    }

    private async Task<ValidationReport> ValidateAsync(GatewayConfigDocument document, CancellationToken ct)
    {
        var issues = validator.Validate(document).Issues.ToList();
        foreach (var publicationValidator in publicationValidators)
            issues.AddRange(await publicationValidator.ValidateAsync(document, ct));
        return new ValidationReport(issues.OrderBy(x => x.JsonPath).ThenBy(x => x.Code).ToArray());
    }

    private static IReadOnlyList<PendingConfigurationChange> DescribeChanges(GatewayConfigDocument active,
        GatewayConfigDocument pending)
    {
        var changes = new List<PendingConfigurationChange>();
        var activeRoutes = active.Routes.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
        var pendingRoutes = pending.Routes.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var route in pendingRoutes.Values.Where(x => !activeRoutes.ContainsKey(x.Id)))
            changes.Add(new PendingConfigurationChange("RouteAdded", route.Id,
                $"Added route {ManagedRouteCompiler.ToManaged(pending, route).Name}"));
        foreach (var route in activeRoutes.Values.Where(x => !pendingRoutes.ContainsKey(x.Id)))
            changes.Add(new PendingConfigurationChange("RouteDeleted", route.Id,
                $"Deleted route {ManagedRouteCompiler.ToManaged(active, route).Name}"));
        foreach (var route in pendingRoutes.Values.Where(x => activeRoutes.ContainsKey(x.Id)))
        {
            var before = ManagedRouteCompiler.ToManaged(active, activeRoutes[route.Id]);
            var after = ManagedRouteCompiler.ToManaged(pending, route);
            if (before.Version != after.Version)
                changes.Add(new PendingConfigurationChange("RouteUpdated", route.Id, $"Updated route {after.Name}"));
        }

        if (!JsonSerializer.Serialize(active.OperationalDefaults, GatewayJson.Options)
                .Equals(JsonSerializer.Serialize(pending.OperationalDefaults, GatewayJson.Options),
                    StringComparison.Ordinal) ||
            !JsonSerializer.Serialize(active.UnavailableResponseProfiles, GatewayJson.Options)
                .Equals(JsonSerializer.Serialize(pending.UnavailableResponseProfiles, GatewayJson.Options),
                    StringComparison.Ordinal))
            changes.Add(new PendingConfigurationChange("EnvironmentSettingsUpdated", null,
                "Updated route traffic settings"));
        if (changes.Count == 0 && ConfigDocuments.Hash(ConfigDocuments.Serialize(active)) !=
            ConfigDocuments.Hash(ConfigDocuments.Serialize(pending)))
            changes.Add(new PendingConfigurationChange("ConfigurationUpdated", null, "Updated configuration"));
        return changes;
    }

    private async Task<(ConfigRevision? Revision, GatewayConfigDocument Document)> ActiveAsync(Guid environmentId,
        CancellationToken ct)
    {
        var environment = await db.Environments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == environmentId, ct) ??
                          throw new KeyNotFoundException("Environment not found.");
        if (environment.ActiveRevisionId is null) return (null, new GatewayConfigDocument());
        var revision = await db.Revisions.AsNoTracking().SingleAsync(x => x.Id == environment.ActiveRevisionId, ct);
        return (revision, ConfigDocuments.Parse(revision.ConfigJson));
    }

    private async Task<(ConfigRevision? Revision, GatewayConfigDocument Document)> EditableAsync(Guid environmentId,
        CancellationToken ct)
    {
        var environment = await db.Environments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == environmentId, ct) ??
                          throw new KeyNotFoundException("Environment not found.");
        var revisionId = environment.PublishingMode == ConfigurationPublishingMode.Staged
            ? environment.PendingRevisionId ?? environment.ActiveRevisionId
            : environment.ActiveRevisionId;
        if (revisionId is null) return (null, new GatewayConfigDocument());
        var revision = await db.Revisions.AsNoTracking().SingleAsync(x => x.Id == revisionId, ct);
        return (revision, ConfigDocuments.Parse(revision.ConfigJson));
    }

    private static bool Equivalent(GatewayConfigDocument leftDocument, GatewayRoute? left,
        GatewayConfigDocument rightDocument, GatewayRoute? right)
    {
        if (left is null || right is null) return left is null && right is null;
        return string.Equals(ManagedRouteCompiler.ToManaged(leftDocument, left).Version,
            ManagedRouteCompiler.ToManaged(rightDocument, right).Version, StringComparison.Ordinal);
    }
}

public sealed class GatewayConfigurationConflictException(Guid? currentVersion)
    : Exception("The active configuration changed after it was loaded.")
{
    public Guid? CurrentVersion { get; } = currentVersion;
}

public sealed class GatewayRevertConflictException(IReadOnlyList<string> paths)
    : Exception("The change cannot be reverted because the same route changed later.")
{
    public IReadOnlyList<string> Paths { get; } = paths;
}
