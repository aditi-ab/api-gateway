using System.Text.Json;
using System.Text.Json.Serialization;

namespace ApiGateway.Domain;

public sealed record GatewayConfigDocument
{
    public int SchemaVersion { get; init; } = 2;
    public IReadOnlyList<GatewayRoute> Routes { get; init; } = [];
    public IReadOnlyList<GatewayCluster> Clusters { get; init; } = [];
    public GatewayPolicies Policies { get; init; } = new();

    public IReadOnlyDictionary<string, RouteUnavailableResponseProfile> UnavailableResponseProfiles { get; init; } =
        new Dictionary<string, RouteUnavailableResponseProfile>();

    public RouteOperationalDefaults OperationalDefaults { get; init; } = new();
}

public sealed record GatewayRoute
{
    public required string Id { get; init; }
    public bool Enabled { get; init; } = true;
    public int? Order { get; init; }
    public required RouteMatch Match { get; init; }
    public required string ClusterId { get; init; }
    public IReadOnlyList<IReadOnlyDictionary<string, string>> Transforms { get; init; } = [];
    public string? AuthorizationPolicy { get; init; }
    public string? RateLimitPolicy { get; init; }
    public string? TimeoutPolicy { get; init; }
    public string? CorsPolicy { get; init; }
    public MirrorPolicy? Mirror { get; init; }
    public RouteAccessPolicy? Access { get; init; }
    public RequestValidationPolicy? RequestValidation { get; init; }
    public ResponseCachePolicy? ResponseCache { get; init; }
    public IReadOnlyList<string> DisabledFeatures { get; init; } = [];
    public RouteOperationalPolicy Operations { get; init; } = new();
    public InboundRoutePolicy Inbound { get; init; } = new();
    public GatewayMetadata Metadata { get; init; } = new();
}

public enum InboundScheme
{
    Any,
    HttpOnly,
    HttpsRedirect
}

public sealed record InboundRoutePolicy(
    InboundScheme Scheme = InboundScheme.Any,
    Guid? CertificateId = null,
    bool WebSocketsAllowed = true);

public enum RouteOperationalState
{
    Online,
    Draining,
    Maintenance,
    Offline
}

public sealed record RouteOperationalPolicy(
    RouteOperationalState State = RouteOperationalState.Online,
    RouteUnavailableResponse? Response = null,
    string? ResponseProfileId = null);

public sealed record RouteUnavailableResponseProfile(
    string Id,
    string Name,
    RouteUnavailableResponse Response);

public sealed record RouteOperationalDefaults(
    string? DrainingProfileId = null,
    string? MaintenanceProfileId = null,
    string? OfflineProfileId = null)
{
    public string? For(RouteOperationalState state)
    {
        return state switch
        {
            RouteOperationalState.Draining => DrainingProfileId,
            RouteOperationalState.Maintenance => MaintenanceProfileId,
            RouteOperationalState.Offline => OfflineProfileId,
            _ => null
        };
    }
}

public sealed record RouteUnavailableResponse(
    int StatusCode = 503,
    string? Title = null,
    string? Message = null,
    TimeSpan? RetryAfter = null,
    string? UpstreamUrl = null);

public sealed record RouteMatch
{
    public required string Path { get; init; }
    public IReadOnlyList<string> Hosts { get; init; } = [];
    public IReadOnlyList<string> Methods { get; init; } = [];
    public IReadOnlyList<RouteValueMatch> Headers { get; init; } = [];
    public IReadOnlyList<RouteValueMatch> QueryParameters { get; init; } = [];
}

public sealed record RouteValueMatch(string Name, string Pattern, string Mode = "Exact", bool IsCaseSensitive = false);

public sealed record MirrorPolicy(
    string ClusterId,
    double Percentage = 100,
    IReadOnlyList<string>? AllowedMethods = null,
    long MaximumBufferedBodyBytes = 0,
    TimeSpan? Timeout = null,
    IReadOnlyList<string>? RemoveHeaders = null);

public sealed record GatewayCluster
{
    public required string Id { get; init; }
    public string LoadBalancingPolicy { get; init; } = "PowerOfTwoChoices";
    public required IReadOnlyDictionary<string, GatewayDestination> Destinations { get; init; }
    public HealthPolicy Health { get; init; } = new();
    public SessionAffinityPolicy? SessionAffinity { get; init; }
    public string? ResiliencePolicy { get; init; }
    public TrafficPolicy? Traffic { get; init; }
    public UpstreamTlsPolicy? Tls { get; init; }
    public UpstreamHttpPolicy HttpClient { get; init; } = new();
    public GatewayMetadata Metadata { get; init; } = new();
}

public sealed record GatewayDestination(
    string Address,
    string? HealthAddress = null,
    string Pool = "default",
    IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record HealthPolicy(
    bool ActiveEnabled = false,
    string Path = "/healthz",
    TimeSpan? Interval = null,
    TimeSpan? Timeout = null,
    bool PassiveEnabled = false,
    TimeSpan? ReactivationPeriod = null,
    string ActivePolicy = "ConsecutiveFailures",
    string PassivePolicy = "TransportFailureRate",
    string AvailableDestinationsPolicy = "HealthyOrPanic",
    string? Query = null);

public sealed record SessionAffinityPolicy(
    bool Enabled = true,
    string Policy = "Cookie",
    string FailurePolicy = "Redistribute",
    string CookieName = "ApiGateway.Affinity",
    string? Path = null,
    string? Domain = null,
    string SecurePolicy = "SameAsRequest",
    string SameSite = "Lax",
    TimeSpan? Expiration = null);

public sealed record TrafficPolicy(
    IReadOnlyDictionary<string, int> Allocations,
    string Mode = "random",
    string? Key = null,
    bool FallbackToHealthyPool = false,
    string? KeySource = null);

public sealed record UpstreamTlsPolicy(string? ClientCertificateRef = null, string? TrustBundleRef = null);

public sealed record UpstreamHttpPolicy(
    string Version = "2.0",
    string VersionPolicy = "RequestVersionOrLower",
    bool AutomaticDecompression = false,
    bool AllowAutoRedirect = false,
    TimeSpan? PooledConnectionLifetime = null,
    int? MaxConnectionsPerServer = null,
    bool EnableMultipleHttp2Connections = false);

public sealed record GatewayMetadata(
    string? Owner = null,
    IReadOnlyList<string>? Tags = null,
    string? Description = null,
    string? Criticality = null,
    DateTimeOffset? SunsetAt = null,
    string? DeprecationMessage = null,
    string? DisplayName = null,
    string? ManagedByRouteId = null);

public sealed record RouteAccessPolicy(
    IReadOnlyList<string>? AllowedCidrs = null,
    IReadOnlyList<string>? DeniedCidrs = null,
    long? MaximumRequestBodyBytes = null);

public sealed record RequestValidationPolicy(string JsonSchema, long MaximumBodyBytes = 1_048_576);

public sealed record ResponseCachePolicy(
    TimeSpan TimeToLive,
    long MaximumBodyBytes = 1_048_576,
    IReadOnlyList<string>? VaryByHeaders = null);

public sealed record GatewayPolicies
{
    public string? DefaultAuthorizationPolicy { get; init; } = "Anonymous";

    public IReadOnlyDictionary<string, AuthorizationPolicy> Authorization { get; init; } =
        new Dictionary<string, AuthorizationPolicy>();

    public IReadOnlyDictionary<string, RateLimitPolicy> RateLimits { get; init; } =
        new Dictionary<string, RateLimitPolicy>();

    public IReadOnlyDictionary<string, TimeoutPolicy> Timeouts { get; init; } = new Dictionary<string, TimeoutPolicy>();

    public IReadOnlyDictionary<string, ResiliencePolicy> Resilience { get; init; } =
        new Dictionary<string, ResiliencePolicy>();

    public IReadOnlyDictionary<string, CorsPolicy> Cors { get; init; } = new Dictionary<string, CorsPolicy>();
}

public sealed record AuthorizationPolicy(
    string Type,
    IReadOnlyList<string>? RequiredScopes = null,
    string? Authority = null,
    string? Issuer = null,
    IReadOnlyList<string>? Audiences = null,
    IReadOnlyDictionary<string, string>? RequiredClaims = null,
    IReadOnlyList<string>? Policies = null,
    TimeSpan? ClockSkew = null);

public sealed record RateLimitPolicy(
    string Type,
    int PermitLimit,
    TimeSpan? Window = null,
    int QueueLimit = 0,
    string PartitionBy = "global",
    string? PartitionName = null,
    int SegmentsPerWindow = 4,
    int? TokensPerPeriod = null,
    string QueueOrder = "oldestFirst");

public sealed record TimeoutPolicy(TimeSpan Total);

public sealed record ResiliencePolicy(
    int RetryCount = 0,
    TimeSpan? AttemptTimeout = null,
    IReadOnlyList<int>? StatusCodes = null,
    IReadOnlyList<string>? AllowedMethods = null,
    long MaximumBufferedRequestBytes = 0,
    double? FailureRatio = null,
    TimeSpan? SamplingDuration = null,
    int? MinimumThroughput = null,
    TimeSpan? BreakDuration = null,
    bool RetryTransportFailures = true,
    TimeSpan? Backoff = null,
    bool Jitter = true);

public sealed record CorsPolicy(
    IReadOnlyList<string> Origins,
    IReadOnlyList<string> Methods,
    IReadOnlyList<string> Headers,
    IReadOnlyList<string>? ExposedHeaders = null,
    bool AllowCredentials = false,
    TimeSpan? PreflightMaxAge = null);

public static class GatewayJson
{
    public static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            WriteIndented = false
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}