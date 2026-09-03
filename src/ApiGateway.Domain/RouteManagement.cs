namespace ApiGateway.Domain;

public sealed record ManagedRoute(
    string Id,
    string Name,
    string Version,
    bool Enabled,
    RouteMatch Match,
    ManagedUpstream Upstream,
    ManagedRouteFeatures Features,
    RouteOperationalPolicy Operations,
    int? Order = null,
    GatewayMetadata? Metadata = null,
    InboundRoutePolicy? Inbound = null);

public sealed record ManagedUpstream(
    string Url,
    IReadOnlyDictionary<string, GatewayDestination>? Destinations = null,
    string LoadBalancingPolicy = "PowerOfTwoChoices",
    HealthPolicy? Health = null,
    SessionAffinityPolicy? SessionAffinity = null,
    TrafficPolicy? Traffic = null,
    UpstreamTlsPolicy? Tls = null,
    UpstreamHttpPolicy? HttpClient = null,
    string? UpstreamId = null,
    string? UpstreamName = null);

public sealed record NamedUpstream(
    string Id,
    string Name,
    string Version,
    IReadOnlyDictionary<string, GatewayDestination> Destinations,
    string LoadBalancingPolicy = "PowerOfTwoChoices",
    HealthPolicy? Health = null,
    SessionAffinityPolicy? SessionAffinity = null,
    TrafficPolicy? Traffic = null,
    UpstreamTlsPolicy? Tls = null,
    UpstreamHttpPolicy? HttpClient = null);

public sealed record SaveNamedUpstreamInput(
    string Name,
    IReadOnlyDictionary<string, GatewayDestination> Destinations,
    string LoadBalancingPolicy = "PowerOfTwoChoices",
    HealthPolicy? Health = null,
    SessionAffinityPolicy? SessionAffinity = null,
    TrafficPolicy? Traffic = null,
    UpstreamTlsPolicy? Tls = null,
    UpstreamHttpPolicy? HttpClient = null);

public sealed record ManagedRouteFeatures(
    AuthorizationPolicy? Authorization = null,
    RateLimitPolicy? RateLimit = null,
    TimeoutPolicy? Timeout = null,
    ResiliencePolicy? Resilience = null,
    CorsPolicy? Cors = null,
    IReadOnlyList<IReadOnlyDictionary<string, string>>? Transforms = null,
    MirrorPolicy? Mirror = null,
    RouteAccessPolicy? Access = null,
    RequestValidationPolicy? RequestValidation = null,
    ResponseCachePolicy? ResponseCache = null,
    IReadOnlyList<string>? DisabledFeatures = null);

public sealed record CreateManagedRouteInput(string Name, string Path, string? UpstreamUrl = null,
    string? UpstreamId = null);

public enum UpstreamPathHandling
{
    Preserve,
    StripPrefix
}

public sealed record UpdateManagedRouteBasicsInput(
    string Name,
    string Path,
    string? UpstreamUrl,
    bool Enabled = true,
    IReadOnlyList<string>? Methods = null,
    IReadOnlyList<string>? Hosts = null,
    IReadOnlyList<RouteValueMatch>? Headers = null,
    IReadOnlyList<RouteValueMatch>? QueryParameters = null,
    int? Order = null,
    IReadOnlyDictionary<string, GatewayDestination>? Destinations = null,
    string? LoadBalancingPolicy = null,
    UpstreamPathHandling? PathHandling = null,
    string? PathPrefixToRemove = null,
    InboundRoutePolicy? Inbound = null,
    UpstreamHttpPolicy? HttpClient = null,
    bool? PreserveOriginalHost = null,
    string? UpstreamId = null);

public sealed record UpdateManagedRouteInput(
    string Name,
    bool Enabled,
    RouteMatch Match,
    ManagedUpstream Upstream,
    ManagedRouteFeatures Features,
    int? Order = null,
    RouteOperationalPolicy? Operations = null,
    GatewayMetadata? Metadata = null,
    InboundRoutePolicy? Inbound = null);

public sealed record UpdateRouteOperationalStateInput(
    RouteOperationalState State,
    string? ResponseProfileId = null,
    bool UseEnvironmentDefault = true,
    int StatusCode = 503,
    string? Title = null,
    string? Message = null,
    TimeSpan? RetryAfter = null,
    string? UpstreamUrl = null);

public sealed record SaveRouteUnavailableResponseProfileInput(
    string? Id,
    string Name,
    int StatusCode = 503,
    string? Title = null,
    string? Message = null,
    TimeSpan? RetryAfter = null,
    string? UpstreamUrl = null);

public sealed record UpdateRouteOperationalDefaultsInput(
    string? DrainingProfileId = null,
    string? MaintenanceProfileId = null,
    string? OfflineProfileId = null);

public sealed record RouteFeatureDescriptor(string Id, string Category, string DisplayName, string Description);
