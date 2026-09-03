using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ApiGateway.Domain;

namespace ApiGateway.Application;

public enum ValidationSeverity
{
    Warning,
    Error
}

public sealed record ValidationIssue(
    ValidationSeverity Severity,
    string Code,
    string JsonPath,
    string Message,
    string? RouteId = null,
    string? ClusterId = null);

public sealed record ValidationReport(IReadOnlyList<ValidationIssue> Issues)
{
    public bool IsValid => Issues.All(x => x.Severity != ValidationSeverity.Error);
}

public interface IConfigurationPublicationValidator
{
    Task<IReadOnlyList<ValidationIssue>> ValidateAsync(GatewayConfigDocument document, CancellationToken ct);
}

public static class ConfigDocuments
{
    public static GatewayConfigDocument Parse(string json)
    {
        return JsonSerializer.Deserialize<GatewayConfigDocument>(json, GatewayJson.Options) ??
               throw new JsonException("Configuration document is null.");
    }

    public static string Serialize(GatewayConfigDocument document)
    {
        using var parsed = JsonDocument.Parse(JsonSerializer.Serialize(document, GatewayJson.Options));
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);
        WriteCanonical(writer, parsed.RootElement);
        writer.Flush();
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static string Hash(string json)
    {
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Serialize(Parse(json)))));
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray()) WriteCanonical(writer, item);
                writer.WriteEndArray();
                break;
            default: element.WriteTo(writer); break;
        }
    }
}

public sealed class GatewayConfigValidator
{
    private static readonly HashSet<string> LoadBalancers = new(StringComparer.OrdinalIgnoreCase)
        { "PowerOfTwoChoices", "RoundRobin", "Random", "LeastRequests" };

    private static readonly HashSet<string> HeaderModes = new(StringComparer.OrdinalIgnoreCase)
        { "Exact", "ExactHeader", "Prefix", "HeaderPrefix", "Contains", "NotContains", "Exists", "NotExists" };

    private static readonly HashSet<string> QueryModes = new(StringComparer.OrdinalIgnoreCase)
        { "Exact", "Prefix", "Contains", "NotContains", "Exists" };

    public ValidationReport Validate(GatewayConfigDocument document)
    {
        var issues = new List<ValidationIssue>();
        if (document.SchemaVersion is not (1 or 2 or 3))
            issues.Add(Error("SCHEMA_VERSION", "/schemaVersion", "Only schemaVersion 1 and 2 are supported."));
        foreach (var (id, profile) in document.UnavailableResponseProfiles)
        {
            var path = $"/unavailableResponseProfiles/{id}";
            if (string.IsNullOrWhiteSpace(id) || !id.Equals(profile.Id, StringComparison.OrdinalIgnoreCase))
                issues.Add(Error("RESPONSE_PROFILE_ID", path, "The response profile key must match its non-empty ID."));
            if (string.IsNullOrWhiteSpace(profile.Name))
                issues.Add(Error("RESPONSE_PROFILE_NAME", path + "/name", "A response profile requires a name."));
            ValidateUnavailableResponse(profile.Response, path + "/response", issues);
        }

        foreach (var (state, profileId) in new[]
                 {
                     ("draining", document.OperationalDefaults.DrainingProfileId),
                     ("maintenance", document.OperationalDefaults.MaintenanceProfileId),
                     ("offline", document.OperationalDefaults.OfflineProfileId)
                 })
            if (profileId is not null && !document.UnavailableResponseProfiles.ContainsKey(profileId))
                issues.Add(Error("OPERATIONAL_DEFAULT_REFERENCE", $"/operationalDefaults/{state}ProfileId",
                    $"Response profile '{profileId}' does not exist."));
        var clusters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var cluster in document.Clusters)
        {
            var path = $"/clusters/{cluster.Id}";
            if (string.IsNullOrWhiteSpace(cluster.Id) || !clusters.Add(cluster.Id))
                issues.Add(
                    Error("CLUSTER_ID", path, "Cluster IDs must be non-empty and unique.", clusterId: cluster.Id));
            if (cluster.Destinations.Count == 0)
                issues.Add(Error("DESTINATIONS_REQUIRED", path + "/destinations",
                    "A cluster requires at least one destination.", clusterId: cluster.Id));
            if (!LoadBalancers.Contains(cluster.LoadBalancingPolicy))
                issues.Add(Error("LOAD_BALANCER", path + "/loadBalancingPolicy",
                    "The load-balancing policy is unsupported.", clusterId: cluster.Id));
            foreach (var (id, destination) in cluster.Destinations)
                if (!Uri.TryCreate(destination.Address, UriKind.Absolute, out var uri) ||
                    uri.Scheme is not ("http" or "https"))
                    issues.Add(Error("DESTINATION_ADDRESS", path + $"/destinations/{id}/address",
                        "Destination addresses must be absolute HTTP or HTTPS URLs.", clusterId: cluster.Id));
            if (cluster.ResiliencePolicy is not null &&
                !document.Policies.Resilience.ContainsKey(cluster.ResiliencePolicy))
                issues.Add(Error("RESILIENCE_REFERENCE", path + "/resiliencePolicy",
                    "The resilience policy does not exist.", clusterId: cluster.Id));
            if (cluster.HttpClient.Version is not ("1.1" or "2.0" or "3.0"))
                issues.Add(Error("HTTP_VERSION", path + "/httpClient/version", "HTTP version must be 1.1, 2.0, or 3.0.",
                    clusterId: cluster.Id));
            if (cluster.HttpClient.VersionPolicy is not ("RequestVersionOrLower" or "RequestVersionOrHigher"
                or "RequestVersionExact"))
                issues.Add(Error("HTTP_VERSION_POLICY", path + "/httpClient/versionPolicy",
                    "The HTTP version policy is invalid.", clusterId: cluster.Id));
            if (cluster.HttpClient.PooledConnectionLifetime is { } connectionLifetime &&
                connectionLifetime <= TimeSpan.Zero)
                issues.Add(Error("HTTP_CONNECTION_LIFETIME", path + "/httpClient/pooledConnectionLifetime",
                    "Connection lifetime must be positive when configured.", clusterId: cluster.Id));
            if (cluster.HttpClient.MaxConnectionsPerServer is <= 0)
                issues.Add(Error("HTTP_MAX_CONNECTIONS", path + "/httpClient/maxConnectionsPerServer",
                    "Maximum connections must be positive when configured.", clusterId: cluster.Id));
            if (cluster.Traffic is not null)
            {
                if (cluster.Traffic.Allocations.Values.Sum() != 100 ||
                    cluster.Traffic.Allocations.Values.Any(x => x < 0))
                    issues.Add(Error("TRAFFIC_TOTAL", path + "/traffic/allocations",
                        "Traffic allocations must be non-negative and total 100.", clusterId: cluster.Id));
                var pools = cluster.Destinations.Values.Select(x => x.Pool).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var pool in cluster.Traffic.Allocations.Keys.Where(x => !pools.Contains(x)))
                    issues.Add(Error("TRAFFIC_POOL", path + $"/traffic/allocations/{pool}",
                        "The traffic pool has no destination.", clusterId: cluster.Id));
                if (!cluster.Traffic.Mode.Equals("random", StringComparison.OrdinalIgnoreCase) &&
                    !cluster.Traffic.Mode.Equals("stable", StringComparison.OrdinalIgnoreCase))
                    issues.Add(Error("TRAFFIC_MODE", path + "/traffic/mode", "Traffic mode must be random or stable.",
                        clusterId: cluster.Id));
                if (cluster.Traffic.Mode.Equals("stable", StringComparison.OrdinalIgnoreCase))
                {
                    var sources = new[] { "header", "cookie", "claim", "consumerKey" };
                    if (!sources.Contains(cluster.Traffic.KeySource, StringComparer.OrdinalIgnoreCase))
                        issues.Add(Error("TRAFFIC_KEY_SOURCE", path + "/traffic/keySource",
                            "Stable traffic keySource must be header, cookie, claim, or consumerKey.",
                            clusterId: cluster.Id));
                    if (!cluster.Traffic.KeySource?.Equals("consumerKey", StringComparison.OrdinalIgnoreCase) == true &&
                        string.IsNullOrWhiteSpace(cluster.Traffic.Key))
                        issues.Add(Error("TRAFFIC_KEY", path + "/traffic/key",
                            "The selected stable key source requires a key name.", clusterId: cluster.Id));
                }
            }
        }

        var routes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var route in document.Routes)
        {
            if (route.Inbound.Scheme == InboundScheme.HttpsRedirect && route.Inbound.CertificateId is null)
                issues.Add(new ValidationIssue(ValidationSeverity.Error, "INBOUND_CERTIFICATE_REQUIRED",
                    $"$.routes[{route.Id}].inbound.certificateId",
                    "HTTPS redirect routes require an inbound certificate."));
            if (route.Inbound.Scheme == InboundScheme.HttpOnly && route.Inbound.CertificateId is not null)
                issues.Add(new ValidationIssue(ValidationSeverity.Error, "INBOUND_CERTIFICATE_NOT_ALLOWED",
                    $"$.routes[{route.Id}].inbound.certificateId",
                    "HTTP-only routes cannot select an inbound certificate."));
            if (route.Inbound.CertificateId is not null && route.Match.Hosts.Count == 0)
                issues.Add(new ValidationIssue(ValidationSeverity.Error, "INBOUND_HOST_REQUIRED",
                    $"$.routes[{route.Id}].match.hosts",
                    "Certificate-backed routes require at least one incoming hostname."));
            var path = $"/routes/{route.Id}";
            if (string.IsNullOrWhiteSpace(route.Id) || !routes.Add(route.Id))
                issues.Add(Error("ROUTE_ID", path, "Route IDs must be non-empty and unique.", route.Id));
            if (string.IsNullOrWhiteSpace(route.Match.Path) || !route.Match.Path.StartsWith('/'))
                issues.Add(Error("ROUTE_PATH", path + "/match/path", "Route paths must begin with '/'.", route.Id));
            if (route.Enabled && !clusters.Contains(route.ClusterId))
                issues.Add(Error("CLUSTER_REFERENCE", path + "/clusterId", "The referenced cluster does not exist.",
                    route.Id));
            foreach (var match in route.Match.Headers)
            {
                if (string.IsNullOrWhiteSpace(match.Name))
                    issues.Add(Error("HEADER_MATCH_NAME", path + "/match/headers",
                        "Header match names must not be empty.", route.Id));
                if (!HeaderModes.Contains(match.Mode))
                    issues.Add(Error("HEADER_MATCH_MODE", path + "/match/headers",
                        $"Header match mode '{match.Mode}' is unsupported.", route.Id));
            }

            foreach (var match in route.Match.QueryParameters)
            {
                if (string.IsNullOrWhiteSpace(match.Name))
                    issues.Add(Error("QUERY_MATCH_NAME", path + "/match/queryParameters",
                        "Query parameter match names must not be empty.", route.Id));
                if (!QueryModes.Contains(match.Mode))
                    issues.Add(Error("QUERY_MATCH_MODE", path + "/match/queryParameters",
                        $"Query match mode '{match.Mode}' is unsupported.", route.Id));
            }

            if (route.Transforms.Any(x => x.Count == 0 || x.Any(pair => string.IsNullOrWhiteSpace(pair.Key))))
                issues.Add(Error("TRANSFORM", path + "/transforms",
                    "Each transform requires at least one non-empty key.", route.Id));
            var auth = route.AuthorizationPolicy ?? document.Policies.DefaultAuthorizationPolicy;
            if (auth is null)
                issues.Add(Error("AUTH_REQUIRED", path + "/authorizationPolicy",
                    "A route or default authorization policy is required.", route.Id));
            else if (auth != "Anonymous" && !document.Policies.Authorization.ContainsKey(auth))
                issues.Add(Error("AUTH_REFERENCE", path + "/authorizationPolicy",
                    "The authorization policy does not exist.", route.Id));
            if (route.RateLimitPolicy is not null && !document.Policies.RateLimits.ContainsKey(route.RateLimitPolicy))
                issues.Add(Error("RATE_LIMIT_REFERENCE", path + "/rateLimitPolicy",
                    "The rate-limit policy does not exist.", route.Id));
            if (route.TimeoutPolicy is not null && !document.Policies.Timeouts.ContainsKey(route.TimeoutPolicy))
                issues.Add(Error("TIMEOUT_REFERENCE", path + "/timeoutPolicy", "The timeout policy does not exist.",
                    route.Id));
            if (route.CorsPolicy is not null && !document.Policies.Cors.ContainsKey(route.CorsPolicy))
                issues.Add(Error("CORS_REFERENCE", path + "/corsPolicy", "The CORS policy does not exist.", route.Id));
            if (route.Mirror is { Percentage: < 0 or > 100 })
                issues.Add(Error("MIRROR_PERCENTAGE", path + "/mirror/percentage",
                    "Mirror sampling must be between 0 and 100 percent.", route.Id));
            if (route.Mirror is { } mirror && !clusters.Contains(mirror.ClusterId))
                issues.Add(Error("MIRROR_CLUSTER_REFERENCE", path + "/mirror/clusterId",
                    "The mirror cluster does not exist.", route.Id));
            if (route.Access?.MaximumRequestBodyBytes is <= 0)
                issues.Add(Error("REQUEST_SIZE", path + "/access/maximumRequestBodyBytes",
                    "Maximum request body bytes must be positive.", route.Id));
            try
            {
                CidrMatcher.Normalize(route.Access?.AllowedCidrs);
                CidrMatcher.Normalize(route.Access?.DeniedCidrs);
            }
            catch (ArgumentException exception)
            {
                issues.Add(Error("CIDR", path + "/access", exception.Message, route.Id));
            }

            if (route.RequestValidation is { } requestValidation &&
                (requestValidation.MaximumBodyBytes <= 0 || string.IsNullOrWhiteSpace(requestValidation.JsonSchema)))
                issues.Add(Error("REQUEST_VALIDATION", path + "/requestValidation",
                    "Request validation requires a JSON schema and a positive body limit.", route.Id));
            if ((route.ResponseCache is { TimeToLive: var ttl } && ttl <= TimeSpan.Zero) ||
                route.ResponseCache is { MaximumBodyBytes: <= 0 })
                issues.Add(Error("RESPONSE_CACHE", path + "/responseCache",
                    "Response caching requires a positive lifetime and body limit.", route.Id));
            if (route.Operations.State != RouteOperationalState.Online)
            {
                if (route.Operations.ResponseProfileId is { } profileId &&
                    !document.UnavailableResponseProfiles.ContainsKey(profileId))
                    issues.Add(Error("OPERATIONAL_PROFILE_REFERENCE", path + "/operations/responseProfileId",
                        $"Response profile '{profileId}' does not exist.", route.Id));
                if (route.Operations.Response is { } response)
                    ValidateUnavailableResponse(response, path + "/operations/response", issues, route.Id);
            }
        }

        foreach (var (id, policy) in document.Policies.Authorization)
        {
            var path = $"/policies/authorization/{id}";
            if (policy.Type.Equals("jwt", StringComparison.OrdinalIgnoreCase))
            {
                if (!Uri.TryCreate(policy.Authority, UriKind.Absolute, out var authority) ||
                    authority.Scheme != "https")
                    issues.Add(Error("JWT_AUTHORITY", path + "/authority",
                        "JWT authority must be an absolute HTTPS URL."));
                if (string.IsNullOrWhiteSpace(policy.Issuer))
                    issues.Add(Error("JWT_ISSUER", path + "/issuer", "JWT issuer is required."));
                if (policy.Audiences is null || policy.Audiences.Count == 0)
                    issues.Add(Error("JWT_AUDIENCE", path + "/audiences", "At least one JWT audience is required."));
                if (policy.ClockSkew is { } skew && (skew < TimeSpan.Zero || skew > TimeSpan.FromMinutes(15)))
                    issues.Add(Error("JWT_CLOCK_SKEW", path + "/clockSkew",
                        "JWT clock skew must be between zero and 15 minutes."));
            }
            else if (policy.Type.Equals("anyOf", StringComparison.OrdinalIgnoreCase) ||
                     policy.Type.Equals("allOf", StringComparison.OrdinalIgnoreCase))
            {
                if (policy.Policies is null || policy.Policies.Count == 0)
                    issues.Add(Error("AUTH_COMPOSITE_EMPTY", path + "/policies",
                        "Composite authorization requires at least one child policy."));
                else
                    foreach (var child in policy.Policies.Where(x => !document.Policies.Authorization.ContainsKey(x)))
                        issues.Add(Error("AUTH_COMPOSITE_REFERENCE", path + "/policies",
                            $"Authorization policy '{child}' does not exist."));
            }
            else if (!policy.Type.Equals("apiKey", StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(Error("AUTH_TYPE", path + "/type",
                    "Authorization type must be apiKey, jwt, anyOf, or allOf."));
            }
        }

        foreach (var id in document.Policies.Authorization.Keys)
            DetectAuthorizationCycle(id, document.Policies.Authorization, [], [], issues);
        foreach (var (id, policy) in document.Policies.RateLimits)
        {
            var path = $"/policies/rateLimits/{id}";
            var type = policy.Type.ToLowerInvariant();
            if (type is not ("fixedwindow" or "slidingwindow" or "tokenbucket" or "concurrency"))
                issues.Add(Error("RATE_LIMIT_TYPE", path + "/type",
                    "Rate-limit type must be fixedWindow, slidingWindow, tokenBucket, or concurrency."));
            if (policy.PermitLimit <= 0 || policy.QueueLimit < 0)
                issues.Add(Error("RATE_LIMIT_BOUNDS", path,
                    "Permit limit must be positive and queue limit cannot be negative."));
            if (type != "concurrency" && (policy.Window is null || policy.Window <= TimeSpan.Zero))
                issues.Add(Error("RATE_LIMIT_WINDOW", path + "/window",
                    "Window must be positive for time-based rate limits."));
            if (type == "slidingwindow" && policy.SegmentsPerWindow < 2)
                issues.Add(Error("RATE_LIMIT_SEGMENTS", path + "/segmentsPerWindow",
                    "Sliding-window policies require at least two segments."));
            if (type == "tokenbucket" && policy.TokensPerPeriod is <= 0)
                issues.Add(Error("RATE_LIMIT_TOKENS", path + "/tokensPerPeriod",
                    "Token-bucket tokens per period must be positive when specified."));
            if (policy.QueueOrder is not ("oldestFirst" or "newestFirst"))
                issues.Add(Error("RATE_LIMIT_QUEUE_ORDER", path + "/queueOrder",
                    "Queue order must be oldestFirst or newestFirst."));
            if (policy.PartitionBy.Equals("header", StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrWhiteSpace(policy.PartitionName))
                issues.Add(Error("RATE_LIMIT_PARTITION", path + "/partitionName",
                    "Header partitioning requires a header name."));
        }

        foreach (var (id, policy) in document.Policies.Timeouts)
            if (policy.Total <= TimeSpan.Zero)
                issues.Add(Error("TIMEOUT_BOUNDS", $"/policies/timeouts/{id}/total", "Timeout must be positive."));
        foreach (var (id, policy) in document.Policies.Resilience)
        {
            var path = $"/policies/resilience/{id}";
            if (policy.RetryCount is < 0 or > 10)
                issues.Add(Error("RETRY_COUNT", path + "/retryCount", "Retry count must be between 0 and 10."));
            if (policy.MaximumBufferedRequestBytes < 0)
                issues.Add(Error("RETRY_BUFFER", path + "/maximumBufferedRequestBytes",
                    "Retry buffering cannot be negative."));
            if (policy.FailureRatio is < 0 or > 1)
                issues.Add(Error("CIRCUIT_RATIO", path + "/failureRatio",
                    "Circuit failure ratio must be between 0 and 1."));
            if (policy.Backoff is { } backoff && backoff < TimeSpan.Zero)
                issues.Add(Error("RETRY_BACKOFF", path + "/backoff", "Retry backoff cannot be negative."));
        }

        foreach (var (id, policy) in document.Policies.Cors)
        {
            var path = $"/policies/cors/{id}";
            if (policy.Origins.Count == 0 || policy.Methods.Count == 0)
                issues.Add(Error("CORS_REQUIRED", path, "CORS origins and methods are required."));
            if (policy.AllowCredentials && policy.Origins.Contains("*", StringComparer.Ordinal))
                issues.Add(Error("CORS_CREDENTIALS_WILDCARD", path + "/origins",
                    "Credentialed CORS cannot use a wildcard origin."));
        }

        return new ValidationReport(issues.OrderBy(x => x.JsonPath, StringComparer.Ordinal)
            .ThenBy(x => x.Code, StringComparer.Ordinal).ToArray());
    }

    private static void ValidateUnavailableResponse(RouteUnavailableResponse response, string path,
        List<ValidationIssue> issues, string? routeId = null)
    {
        if (response.StatusCode is < 400 or > 599)
            issues.Add(Error("OPERATIONAL_STATUS", path + "/statusCode",
                "The unavailable response status code must be between 400 and 599.", routeId));
        if (response.RetryAfter <= TimeSpan.Zero)
            issues.Add(Error("OPERATIONAL_RETRY_AFTER", path + "/retryAfter",
                "Retry-After must be positive when configured.", routeId));
        if (response.UpstreamUrl is not null &&
            (!Uri.TryCreate(response.UpstreamUrl, UriKind.Absolute, out var uri) ||
             uri.Scheme is not ("http" or "https")))
            issues.Add(Error("OPERATIONAL_UPSTREAM", path + "/upstreamUrl",
                "The maintenance upstream must be an absolute HTTP or HTTPS URL.", routeId));
    }

    private static void DetectAuthorizationCycle(string id, IReadOnlyDictionary<string, AuthorizationPolicy> policies,
        HashSet<string> visiting, HashSet<string> visited, List<ValidationIssue> issues)
    {
        if (visited.Contains(id) || !policies.TryGetValue(id, out var policy)) return;
        if (!visiting.Add(id))
        {
            issues.Add(Error("AUTH_COMPOSITE_CYCLE", $"/policies/authorization/{id}/policies",
                "Composite authorization policies cannot contain cycles."));
            return;
        }

        foreach (var child in policy.Policies ?? [])
            DetectAuthorizationCycle(child, policies, visiting, visited, issues);
        visiting.Remove(id);
        visited.Add(id);
    }

    private static ValidationIssue Error(string code, string path, string message, string? routeId = null,
        string? clusterId = null)
    {
        return new ValidationIssue(ValidationSeverity.Error, code, path, message, routeId, clusterId);
    }
}