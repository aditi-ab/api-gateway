using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ApiGateway.Domain;

namespace ApiGateway.Application;

public static partial class ManagedRouteCompiler
{
    private const string Prefix = "__route_";

    public static IReadOnlyList<RouteFeatureDescriptor> FeatureCatalog { get; } =
    [
        new("authorization", "Security", "Authentication", "Require an API key or validate a JWT access token."),
        new("ip-restrictions", "Security", "IP restrictions", "Allow or deny client CIDR ranges."),
        new("rate-limit", "Traffic control", "Rate limiting", "Limit requests globally or by client identity."),
        new("request-size", "Traffic control", "Request size", "Reject request bodies above a configured limit."),
        new("headers", "Transformation", "Header manipulation", "Add, set, or remove request and response headers."),
        new("transforms", "Transformation", "Path and query transforms", "Rewrite paths and query parameters."),
        new("timeout", "Reliability", "Timeout", "Limit total request duration."),
        new("resilience", "Reliability", "Retries and circuit breaker",
            "Retry safe requests and isolate failing upstreams."),
        new("cors", "Security", "CORS", "Control browser origins, methods, and headers."),
        new("mirror", "Traffic control", "Traffic mirroring",
            "Send a bounded copy of selected requests to another upstream."),
        new("request-validation", "Validation", "JSON request validation",
            "Validate JSON request bodies against a schema."),
        new("response-cache", "Reliability", "Response caching", "Cache safe anonymous GET and HEAD responses.")
    ];

    public static ManagedRoute ToManaged(GatewayConfigDocument document, GatewayRoute route)
    {
        var cluster = document.Clusters.First(x => x.Id.Equals(route.ClusterId, StringComparison.OrdinalIgnoreCase));
        var destination = cluster.Destinations.Values.First();
        var features = new ManagedRouteFeatures(
            Policy(document.Policies.Authorization, route.AuthorizationPolicy),
            Policy(document.Policies.RateLimits, route.RateLimitPolicy),
            Policy(document.Policies.Timeouts, route.TimeoutPolicy),
            Policy(document.Policies.Resilience, cluster.ResiliencePolicy),
            Policy(document.Policies.Cors, route.CorsPolicy), route.Transforms,
            route.Mirror is { } mirror && mirror.ClusterId.StartsWith(Prefix, StringComparison.Ordinal)
                ? mirror with { ClusterId = mirror.ClusterId[Prefix.Length..].Replace("_upstream", string.Empty) }
                : route.Mirror, route.Access,
            route.RequestValidation, route.ResponseCache, route.DisabledFeatures);
        var named = NamedUpstreamCompiler.IsNamed(cluster);
        var upstream = new ManagedUpstream(destination.Address, cluster.Destinations, cluster.LoadBalancingPolicy,
            cluster.Health, cluster.SessionAffinity, cluster.Traffic, cluster.Tls, cluster.HttpClient,
            named ? cluster.Id : null, named ? cluster.Metadata.DisplayName : null);
        var name = route.Metadata.DisplayName ?? Humanize(route.Id);
        var unversioned = new
        {
            route.Id, Name = name, route.Enabled, route.Match, Upstream = upstream, Features = features,
            route.Order, route.Operations, route.Metadata
        };
        var version = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(unversioned, GatewayJson.Options))));
        return new ManagedRoute(route.Id, name, version, route.Enabled, route.Match, upstream, features,
            route.Operations, route.Order, route.Metadata, route.Inbound);
    }

    public static GatewayConfigDocument Create(GatewayConfigDocument source, CreateManagedRouteInput input,
        out ManagedRoute created)
    {
        ValidateBasics(input.Name, input.Path, input.UpstreamUrl, input.UpstreamId, source);
        var root = Slug(input.Name);
        var id = root;
        for (var suffix = 2; source.Routes.Any(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase)); suffix++)
            id = $"{root}-{suffix}";
        var update = new UpdateManagedRouteInput(input.Name.Trim(), true,
            new RouteMatch { Path = input.Path.Trim() }, CreateUpstream(input.UpstreamUrl, input.UpstreamId, source),
            new ManagedRouteFeatures(Transforms:
            [
                new Dictionary<string, string> { ["RequestHeaderOriginalHost"] = "true" }
            ]));
        var result = Upsert(source, id, update);
        created = ToManaged(result, result.Routes.Single(x => x.Id == id));
        return result;
    }

    public static GatewayConfigDocument Update(GatewayConfigDocument source, string id, string expectedVersion,
        UpdateManagedRouteInput input, out ManagedRoute updated)
    {
        var existing = source.Routes.SingleOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) ??
                       throw new KeyNotFoundException("Route not found.");
        var current = ToManaged(source, existing);
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(current.Version),
                Encoding.ASCII.GetBytes(expectedVersion)))
            throw new ManagedRouteConflictException(current.Version);
        ValidateBasics(input.Name, input.Match.Path, input.Upstream.Url, input.Upstream.UpstreamId, source);
        var result = Upsert(source, existing.Id, input);
        updated = ToManaged(result, result.Routes.Single(x => x.Id == existing.Id));
        return result;
    }

    public static GatewayConfigDocument Duplicate(GatewayConfigDocument source, string id, string expectedVersion,
        string name, out ManagedRoute duplicated)
    {
        var existing = source.Routes.SingleOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) ??
                       throw new KeyNotFoundException("Route not found.");
        var current = ToManaged(source, existing);
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(current.Version),
                Encoding.ASCII.GetBytes(expectedVersion)))
            throw new ManagedRouteConflictException(current.Version);
        ValidateBasics(name, current.Match.Path, current.Upstream.Url);
        var root = Slug(name);
        var duplicateId = root;
        for (var suffix = 2;
             source.Routes.Any(x => x.Id.Equals(duplicateId, StringComparison.OrdinalIgnoreCase));
             suffix++)
            duplicateId = $"{root}-{suffix}";
        var input = new UpdateManagedRouteInput(name.Trim(), false, current.Match, current.Upstream,
            current.Features, current.Order, current.Operations, current.Metadata, current.Inbound);
        var result = Upsert(source, duplicateId, input);
        duplicated = ToManaged(result, result.Routes.Single(x => x.Id == duplicateId));
        return result;
    }

    public static GatewayConfigDocument Restore(GatewayConfigDocument source, ManagedRoute route,
        out ManagedRoute restored)
    {
        var input = new UpdateManagedRouteInput(route.Name, route.Enabled, route.Match, route.Upstream, route.Features,
            route.Order, route.Operations, route.Metadata, route.Inbound);
        var result = Upsert(source, route.Id, input);
        restored = ToManaged(result, result.Routes.Single(x => x.Id == route.Id));
        return result;
    }

    public static GatewayConfigDocument Delete(GatewayConfigDocument source, string id, string expectedVersion,
        out ManagedRoute deleted)
    {
        var existing = source.Routes.SingleOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) ??
                       throw new KeyNotFoundException("Route not found.");
        deleted = ToManaged(source, existing);
        if (!string.Equals(deleted.Version, expectedVersion, StringComparison.Ordinal))
            throw new ManagedRouteConflictException(deleted.Version);
        var clusterId = existing.ClusterId;
        var routes = source.Routes.Where(x => x.Id != existing.Id).ToArray();
        var clusters = source.Clusters.Where(x => x.Id != clusterId ||
                                                   NamedUpstreamCompiler.IsNamed(x) ||
                                                   routes.Any(route =>
                                                      route.ClusterId.Equals(clusterId,
                                                          StringComparison.OrdinalIgnoreCase))).ToArray();
        return With(source, routes, clusters, RemovePolicies(source.Policies, existing, clusterId));
    }

    private static GatewayConfigDocument Upsert(GatewayConfigDocument source, string id, UpdateManagedRouteInput input)
    {
        var old = source.Routes.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        var ownedClusterId = $"{Prefix}{id}_upstream";
        var namedCluster = input.Upstream.UpstreamId is null
            ? null
            : source.Clusters.SingleOrDefault(x => NamedUpstreamCompiler.IsNamed(x) &&
                                                   x.Id.Equals(input.Upstream.UpstreamId,
                                                       StringComparison.OrdinalIgnoreCase)) ??
              throw new KeyNotFoundException("Upstream not found.");
        var clusterId = namedCluster?.Id ?? ownedClusterId;
        var authId = $"{Prefix}{id}_authorization";
        var rateId = $"{Prefix}{id}_rate_limit";
        var timeoutId = $"{Prefix}{id}_timeout";
        var resilienceId = $"{Prefix}{id}_resilience";
        var corsId = $"{Prefix}{id}_cors";
        var destinations = input.Upstream.Destinations?.Count > 0
            ? input.Upstream.Destinations
            : new Dictionary<string, GatewayDestination> { ["primary"] = new(input.Upstream.Url.Trim()) };
        var cluster = namedCluster ?? new GatewayCluster
        {
            Id = clusterId, Destinations = destinations, LoadBalancingPolicy = input.Upstream.LoadBalancingPolicy,
            Health = input.Upstream.Health ?? new HealthPolicy(), SessionAffinity = input.Upstream.SessionAffinity,
            Traffic = input.Upstream.Traffic, Tls = input.Upstream.Tls,
            HttpClient = input.Upstream.HttpClient ?? new UpstreamHttpPolicy(),
            ResiliencePolicy = input.Features.Resilience is null ? null : resilienceId,
            Metadata = new GatewayMetadata(DisplayName: input.Name.Trim(), ManagedByRouteId: id)
        };
        var metadata = (input.Metadata ?? new GatewayMetadata()) with
        {
            DisplayName = input.Name.Trim(), ManagedByRouteId = id
        };
        var normalizedHosts = input.Match.Hosts.Select(DnsHostPattern.Normalize)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var match = input.Match with
        {
            Hosts = input.Match.Hosts.SequenceEqual(normalizedHosts, StringComparer.Ordinal)
                ? input.Match.Hosts
                : normalizedHosts
        };
        var route = new GatewayRoute
        {
            Id = id, Enabled = input.Enabled, Order = input.Order, Match = match, ClusterId = clusterId,
            Transforms = input.Features.Transforms ?? [],
            AuthorizationPolicy = input.Features.Authorization is null ? "Anonymous" : authId,
            RateLimitPolicy = input.Features.RateLimit is null ? null : rateId,
            TimeoutPolicy = input.Features.Timeout is null ? null : timeoutId,
            CorsPolicy = input.Features.Cors is null ? null : corsId,
            Mirror = input.Features.Mirror is { } mirror &&
                     !mirror.ClusterId.StartsWith(Prefix, StringComparison.Ordinal)
                ? mirror with { ClusterId = $"{Prefix}{mirror.ClusterId}_upstream" }
                : input.Features.Mirror,
            Access = input.Features.Access, RequestValidation = input.Features.RequestValidation,
            ResponseCache = input.Features.ResponseCache,
            DisabledFeatures = (input.Features.DisabledFeatures ?? []).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
            Operations = input.Operations ?? old?.Operations ?? new RouteOperationalPolicy(),
            Inbound = input.Inbound ?? old?.Inbound ?? new InboundRoutePolicy(),
            Metadata = metadata
        };
        var routes = source.Routes.Where(x => x.Id != old?.Id).Append(route).OrderBy(x => x.Id).ToArray();
        var clusters = source.Clusters.Where(x =>
                x.Id != old?.ClusterId || NamedUpstreamCompiler.IsNamed(x))
            .Where(x => namedCluster is not null || x.Id != ownedClusterId)
            .Concat(namedCluster is null ? [cluster] : [])
            .OrderBy(x => x.Id).ToArray();
        var policies = RemovePolicies(source.Policies, old, old?.ClusterId);
        policies = new GatewayPolicies
        {
            DefaultAuthorizationPolicy = policies.DefaultAuthorizationPolicy,
            Authorization = Set(policies.Authorization, authId, input.Features.Authorization),
            RateLimits = Set(policies.RateLimits, rateId, input.Features.RateLimit),
            Timeouts = Set(policies.Timeouts, timeoutId, input.Features.Timeout),
            Resilience = Set(policies.Resilience, resilienceId, input.Features.Resilience),
            Cors = Set(policies.Cors, corsId, input.Features.Cors)
        };
        return With(source, routes, clusters, policies);
    }

    private static GatewayPolicies RemovePolicies(GatewayPolicies policies, GatewayRoute? route, string? clusterId)
    {
        return new GatewayPolicies
        {
            DefaultAuthorizationPolicy = policies.DefaultAuthorizationPolicy,
            Authorization = Remove(policies.Authorization, route?.AuthorizationPolicy),
            RateLimits = Remove(policies.RateLimits, route?.RateLimitPolicy),
            Timeouts = Remove(policies.Timeouts, route?.TimeoutPolicy),
            Resilience = Remove(policies.Resilience, clusterId is null ? null : $"{Prefix}{route?.Id}_resilience"),
            Cors = Remove(policies.Cors, route?.CorsPolicy)
        };
    }

    private static GatewayConfigDocument With(GatewayConfigDocument source, IReadOnlyList<GatewayRoute> routes,
        IReadOnlyList<GatewayCluster> clusters, GatewayPolicies policies)
    {
        return new GatewayConfigDocument
        {
            SchemaVersion = 3, Routes = routes, Clusters = clusters, Policies = policies,
            UnavailableResponseProfiles = source.UnavailableResponseProfiles,
            OperationalDefaults = source.OperationalDefaults
        };
    }

    private static IReadOnlyDictionary<string, T> Set<T>(IReadOnlyDictionary<string, T> source, string id, T? value)
        where T : class
    {
        var result = source.Where(x => x.Key != id).ToDictionary();
        if (value is not null) result[id] = value;
        return result;
    }

    private static IReadOnlyDictionary<string, T> Remove<T>(IReadOnlyDictionary<string, T> source, string? id)
    {
        return id is null || !id.StartsWith(Prefix, StringComparison.Ordinal)
            ? source
            : source.Where(x => x.Key != id).ToDictionary();
    }

    private static T? Policy<T>(IReadOnlyDictionary<string, T> source, string? id) where T : class
    {
        return id is not null && source.TryGetValue(id, out var policy) ? policy : null;
    }

    private static ManagedUpstream CreateUpstream(string? upstreamUrl, string? upstreamId,
        GatewayConfigDocument document)
    {
        if (upstreamId is not null)
        {
            var named = NamedUpstreamCompiler.Find(document, upstreamId) ??
                        throw new KeyNotFoundException("Upstream not found.");
            var first = named.Destinations.Values.First();
            return new ManagedUpstream(first.Address, named.Destinations, named.LoadBalancingPolicy, named.Health,
                named.SessionAffinity, named.Traffic, named.Tls, named.HttpClient, named.Id, named.Name);
        }

        return new ManagedUpstream(upstreamUrl!.Trim());
    }

    private static void ValidateBasics(string name, string path, string? upstreamUrl, string? upstreamId = null,
        GatewayConfigDocument? document = null)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 128)
            throw new ArgumentException("Route name must contain 1 to 128 characters.");
        if (string.IsNullOrWhiteSpace(path) || !path.Trim().StartsWith('/'))
            throw new ArgumentException("Route path must begin with '/'.");
        if (upstreamId is not null)
        {
            if (document is null || NamedUpstreamCompiler.Find(document, upstreamId) is null)
                throw new ArgumentException("The selected upstream does not exist.");
            return;
        }

        if (!Uri.TryCreate(upstreamUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new ArgumentException("Upstream URL must be an absolute HTTP or HTTPS URL.");
    }

    private static string Humanize(string id)
    {
        return string.Join(' ', id.Split('-', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => char.ToUpperInvariant(x[0]) + x[1..]));
    }

    private static string Slug(string name)
    {
        var value = NonSlug().Replace(name.Trim().ToLowerInvariant(), "-").Trim('-');
        return string.IsNullOrEmpty(value) ? "route" : value[..Math.Min(value.Length, 64)];
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonSlug();
}

public sealed class ManagedRouteConflictException(string currentVersion)
    : Exception("The route changed after it was loaded.")
{
    public string CurrentVersion { get; } = currentVersion;
}
