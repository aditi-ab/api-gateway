using ApiGateway.Application;
using ApiGateway.Domain;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;
using Yarp.ReverseProxy.LoadBalancing;
using Yarp.ReverseProxy.Model;
using Yarp.ReverseProxy.Transforms.Builder;
using RouteMatch = Yarp.ReverseProxy.Configuration.RouteMatch;

namespace ApiGateway.Management;

public sealed class YarpPublicationValidator(IConfigValidator validator, ITransformBuilder transformBuilder)
    : IConfigurationPublicationValidator
{
    public async Task<IReadOnlyList<ValidationIssue>> ValidateAsync(GatewayConfigDocument document,
        CancellationToken ct)
    {
        var issues = new List<ValidationIssue>();
        foreach (var route in document.Routes.Where(x => x.Enabled))
        {
            RouteConfig mapped;
            try
            {
                mapped = Map(route);
            }
            catch (Exception ex)
            {
                issues.Add(Issue("YARP_ROUTE_MAPPING", $"/routes/{route.Id}", ex, route.Id));
                continue;
            }

            foreach (var error in await validator.ValidateRouteAsync(mapped))
                issues.Add(Issue("YARP_ROUTE", $"/routes/{route.Id}", error, route.Id));
            foreach (var error in transformBuilder.ValidateRoute(mapped))
                issues.Add(Issue("YARP_TRANSFORM", $"/routes/{route.Id}/transforms", error, route.Id));
        }

        foreach (var cluster in document.Clusters)
        {
            ClusterConfig mapped;
            try
            {
                mapped = Map(cluster);
            }
            catch (Exception ex)
            {
                issues.Add(Issue("YARP_CLUSTER_MAPPING", $"/clusters/{cluster.Id}", ex, clusterId: cluster.Id));
                continue;
            }

            foreach (var error in await validator.ValidateClusterAsync(mapped))
                issues.Add(Issue("YARP_CLUSTER", $"/clusters/{cluster.Id}", error, clusterId: cluster.Id));
            foreach (var error in transformBuilder.ValidateCluster(mapped))
                issues.Add(Issue("YARP_CLUSTER_TRANSFORM", $"/clusters/{cluster.Id}", error, clusterId: cluster.Id));
        }

        return issues;
    }

    private static RouteConfig Map(GatewayRoute route)
    {
        return new RouteConfig
        {
            RouteId = route.Id,
            ClusterId = route.ClusterId,
            Order = route.Order,
            Match = new RouteMatch
            {
                Path = route.Match.Path,
                Hosts = route.Match.Hosts.Select(DnsHostPattern.ToUnicode).ToArray(),
                Methods = route.Match.Methods,
                Headers = route.Match.Headers.Select(x => new RouteHeader
                {
                    Name = x.Name, Values = [x.Pattern], Mode = HeaderMode(x.Mode), IsCaseSensitive = x.IsCaseSensitive
                }).ToArray(),
                QueryParameters = route.Match.QueryParameters.Select(x => new RouteQueryParameter
                {
                    Name = x.Name, Values = [x.Pattern], Mode = QueryMode(x.Mode), IsCaseSensitive = x.IsCaseSensitive
                }).ToArray()
            },
            Transforms = route.Transforms
        };
    }

    private static ClusterConfig Map(GatewayCluster cluster)
    {
        return new ClusterConfig
        {
            ClusterId = cluster.Id,
            LoadBalancingPolicy = cluster.Traffic is null ? cluster.LoadBalancingPolicy : "ApiGatewayWeightedPools",
            Destinations = cluster.Destinations.ToDictionary(x => x.Key,
                x => new DestinationConfig { Address = x.Value.Address, Health = x.Value.HealthAddress },
                StringComparer.OrdinalIgnoreCase),
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
            }
        };
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

    private static ValidationIssue Issue(string code, string path, Exception error, string? routeId = null,
        string? clusterId = null)
    {
        return new ValidationIssue(ValidationSeverity.Error, code, path, error.Message, routeId, clusterId);
    }
}

public sealed class WeightedPoolValidationPolicy : ILoadBalancingPolicy
{
    public string Name => "ApiGatewayWeightedPools";

    public DestinationState? PickDestination(HttpContext context, ClusterState cluster,
        IReadOnlyList<DestinationState> availableDestinations)
    {
        return availableDestinations.FirstOrDefault();
    }
}