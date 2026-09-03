using System.Text.Json;
using ApiGateway.Domain;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;
using RouteMatch = Yarp.ReverseProxy.Configuration.RouteMatch;

namespace ApiGateway;

public static class YarpConfigMapper
{
    public static (IReadOnlyList<RouteConfig> Routes, IReadOnlyList<ClusterConfig> Clusters) Map(
        GatewayConfigDocument document)
    {
        document = GatewayFeatureSwitches.Apply(document);
        var routes = document.Routes.Where(x => x.Enabled).Select(route =>
        {
            var unavailableResponse = ResolveUnavailableResponse(document, route);
            return new RouteConfig
            {
                RouteId = route.Id,
                ClusterId = OperationalClusterId(route, unavailableResponse),
                Order = route.Order,
                Match = new RouteMatch
                {
                    Path = route.Match.Path,
                    Hosts = route.Match.Hosts.Select(DnsHostPattern.ToUnicode).ToArray(),
                    Methods = MatchMethods(route),
                    Headers = route.Match.Headers.Select(x => new RouteHeader
                    {
                        Name = x.Name, Values = [x.Pattern], Mode = HeaderMode(x.Mode),
                        IsCaseSensitive = x.IsCaseSensitive
                    }).ToArray(),
                    QueryParameters = route.Match.QueryParameters.Select(x => new RouteQueryParameter
                    {
                        Name = x.Name, Values = [x.Pattern], Mode = QueryMode(x.Mode),
                        IsCaseSensitive = x.IsCaseSensitive
                    }).ToArray()
                },
                Transforms = route.Transforms,
                AuthorizationPolicy = "Anonymous",
                Timeout = route.TimeoutPolicy is not null &&
                          document.Policies.Timeouts.TryGetValue(route.TimeoutPolicy, out var timeout)
                    ? timeout.Total
                    : null,
                Metadata = new Dictionary<string, string>
                {
                    ["ApiGateway.Authorization"] = route.AuthorizationPolicy ??
                                                   document.Policies.DefaultAuthorizationPolicy ?? "Anonymous",
                    ["ApiGateway.RateLimit"] = route.RateLimitPolicy ?? string.Empty,
                    ["ApiGateway.Cors"] = route.CorsPolicy ?? string.Empty,
                    ["ApiGateway.OperationalState"] = route.Operations.State.ToString(),
                    ["ApiGateway.UnavailableResponse"] = JsonSerializer.Serialize(unavailableResponse,
                        GatewayJson.Options),
                    ["ApiGateway.InboundScheme"] = route.Inbound.Scheme.ToString(),
                    ["ApiGateway.WebSocketsAllowed"] = route.Inbound.WebSocketsAllowed.ToString()
                }
            };
        }).ToArray();
        var clusters = document.Clusters.Select(cluster => new ClusterConfig
        {
            ClusterId = cluster.Id,
            LoadBalancingPolicy = cluster.Traffic is null ? cluster.LoadBalancingPolicy : "ApiGatewayWeightedPools",
            Destinations = cluster.Destinations.ToDictionary(x => x.Key,
                x => new DestinationConfig
                {
                    Address = x.Value.Address, Health = x.Value.HealthAddress,
                    Metadata = new Dictionary<string, string> { ["ApiGateway.Pool"] = x.Value.Pool }
                }, StringComparer.OrdinalIgnoreCase),
            HealthCheck = new HealthCheckConfig
            {
                AvailableDestinationsPolicy = cluster.Health.AvailableDestinationsPolicy,
                Active = new ActiveHealthCheckConfig
                {
                    Enabled = cluster.Health.ActiveEnabled, Path = cluster.Health.Path, Query = cluster.Health.Query,
                    Policy = cluster.Health.ActivePolicy,
                    Interval = cluster.Health.Interval ?? TimeSpan.FromSeconds(15),
                    Timeout = cluster.Health.Timeout ?? TimeSpan.FromSeconds(3)
                },
                Passive = new PassiveHealthCheckConfig
                {
                    Enabled = cluster.Health.PassiveEnabled, Policy = cluster.Health.PassivePolicy,
                    ReactivationPeriod = cluster.Health.ReactivationPeriod ?? TimeSpan.FromMinutes(1)
                }
            },
            SessionAffinity = cluster.SessionAffinity is null
                ? null
                : new SessionAffinityConfig
                {
                    Enabled = cluster.SessionAffinity.Enabled, Policy = cluster.SessionAffinity.Policy,
                    FailurePolicy = cluster.SessionAffinity.FailurePolicy,
                    AffinityKeyName = cluster.SessionAffinity.CookieName,
                    Cookie = new SessionAffinityCookieConfig
                    {
                        Path = cluster.SessionAffinity.Path, Domain = cluster.SessionAffinity.Domain,
                        SecurePolicy = Enum.Parse<CookieSecurePolicy>(cluster.SessionAffinity.SecurePolicy, true),
                        SameSite = Enum.Parse<SameSiteMode>(cluster.SessionAffinity.SameSite, true),
                        Expiration = cluster.SessionAffinity.Expiration
                    }
                },
            HttpClient = new HttpClientConfig
            {
                MaxConnectionsPerServer = cluster.HttpClient.MaxConnectionsPerServer,
                EnableMultipleHttp2Connections = cluster.HttpClient.EnableMultipleHttp2Connections
            },
            HttpRequest = new ForwarderRequestConfig
            {
                Version = Version.Parse(cluster.HttpClient.Version),
                VersionPolicy = Enum.Parse<HttpVersionPolicy>(cluster.HttpClient.VersionPolicy, true)
            },
            Metadata = ClusterMetadata(cluster, document)
        }).Concat(document.Routes.Where(route => route.Enabled &&
                                                 route.Operations.State != RouteOperationalState.Online)
            .Select(route => (Route: route, Response: ResolveUnavailableResponse(document, route)))
            .Where(x => x.Response.UpstreamUrl is not null)
            .Select(x => new ClusterConfig
            {
                ClusterId = UnavailableClusterId(x.Route),
                Destinations = new Dictionary<string, DestinationConfig>
                    { ["unavailable"] = new() { Address = x.Response.UpstreamUrl! } },
                HttpRequest = new ForwarderRequestConfig { ActivityTimeout = TimeSpan.FromSeconds(10) }
            })).ToArray();
        return (routes, clusters);
    }

    private static string OperationalClusterId(GatewayRoute route, RouteUnavailableResponse response)
    {
        return route.Operations.State != RouteOperationalState.Online && response.UpstreamUrl is not null
            ? UnavailableClusterId(route)
            : route.ClusterId;
    }

    private static RouteUnavailableResponse ResolveUnavailableResponse(GatewayConfigDocument document,
        GatewayRoute route)
    {
        if (route.Operations.ResponseProfileId is { } routeProfileId &&
            document.UnavailableResponseProfiles.TryGetValue(routeProfileId, out var profile))
            return profile.Response;
        if (route.Operations.Response is { } inlineResponse) return inlineResponse;
        var defaultProfileId = document.OperationalDefaults.For(route.Operations.State);
        return defaultProfileId is not null &&
               document.UnavailableResponseProfiles.TryGetValue(defaultProfileId, out var defaultProfile)
            ? defaultProfile.Response
            : new RouteUnavailableResponse();
    }

    private static string UnavailableClusterId(GatewayRoute route)
    {
        return $"__route_{route.Id}_unavailable";
    }

    private static IReadOnlyList<string> MatchMethods(GatewayRoute route)
    {
        return route.CorsPolicy is not null && route.Match.Methods.Count > 0 &&
               !route.Match.Methods.Contains("OPTIONS", StringComparer.OrdinalIgnoreCase)
            ? route.Match.Methods.Append("OPTIONS").ToArray()
            : route.Match.Methods;
    }

    private static HeaderMatchMode HeaderMode(string mode)
    {
        return mode.ToLowerInvariant() switch
        {
            "exact" or "exactheader" => HeaderMatchMode.ExactHeader,
            "prefix" or "headerprefix" => HeaderMatchMode.HeaderPrefix, "contains" => HeaderMatchMode.Contains,
            "notcontains" => HeaderMatchMode.NotContains, "exists" => HeaderMatchMode.Exists,
            "notexists" => HeaderMatchMode.NotExists,
            _ => throw new InvalidOperationException($"Unsupported header match mode '{mode}'.")
        };
    }

    private static QueryParameterMatchMode QueryMode(string mode)
    {
        return mode.ToLowerInvariant() switch
        {
            "exact" => QueryParameterMatchMode.Exact, "prefix" => QueryParameterMatchMode.Prefix,
            "contains" => QueryParameterMatchMode.Contains, "notcontains" => QueryParameterMatchMode.NotContains,
            "exists" => QueryParameterMatchMode.Exists,
            _ => throw new InvalidOperationException($"Unsupported query match mode '{mode}'.")
        };
    }

    private static IReadOnlyDictionary<string, string>? ClusterMetadata(GatewayCluster cluster,
        GatewayConfigDocument document)
    {
        var metadata = new Dictionary<string, string>
            { ["ApiGateway.HttpClient"] = JsonSerializer.Serialize(cluster.HttpClient, GatewayJson.Options) };
        if (cluster.Traffic is not null)
            metadata["ApiGateway.Traffic"] = JsonSerializer.Serialize(cluster.Traffic, GatewayJson.Options);
        if (cluster.ResiliencePolicy is not null &&
            document.Policies.Resilience.TryGetValue(cluster.ResiliencePolicy, out var resilience))
            metadata["ApiGateway.Resilience"] = JsonSerializer.Serialize(resilience, GatewayJson.Options);
        if (cluster.Tls?.ClientCertificateRef is { } certificate)
            metadata["ApiGateway.ClientCertificateRef"] = certificate;
        if (cluster.Tls?.TrustBundleRef is { } trust) metadata["ApiGateway.TrustBundleRef"] = trust;
        return metadata;
    }
}