using System.Net;
using System.Text.Json;
using ApiGateway.Application;
using ApiGateway.Domain;
using Xunit;

namespace ApiGateway.UnitTests;

public sealed class ConfigurationTests
{
    [Fact]
    public void Canonical_hash_is_stable()
    {
        const string first =
            "{\"schemaVersion\":1,\"routes\":[],\"clusters\":[],\"policies\":{\"defaultAuthorizationPolicy\":\"Anonymous\",\"authorization\":{},\"rateLimits\":{},\"timeouts\":{},\"resilience\":{},\"cors\":{}}}";
        Assert.Equal(ConfigDocuments.Hash(first),
            ConfigDocuments.Hash(ConfigDocuments.Serialize(ConfigDocuments.Parse(first))));
    }

    [Fact]
    public void Canonical_hash_ignores_json_object_property_order()
    {
        const string first =
            "{\"schemaVersion\":1,\"routes\":[],\"clusters\":[],\"policies\":{\"defaultAuthorizationPolicy\":\"Anonymous\",\"authorization\":{},\"rateLimits\":{},\"timeouts\":{},\"resilience\":{},\"cors\":{}}}";
        const string reordered =
            "{\"policies\":{\"cors\":{},\"resilience\":{},\"timeouts\":{},\"rateLimits\":{},\"authorization\":{},\"defaultAuthorizationPolicy\":\"Anonymous\"},\"clusters\":[],\"routes\":[],\"schemaVersion\":1}";
        Assert.Equal(ConfigDocuments.Hash(first), ConfigDocuments.Hash(reordered));
    }

    [Fact]
    public void Missing_cluster_is_rejected()
    {
        var document = new GatewayConfigDocument
        {
            Routes =
            [
                new GatewayRoute
                    { Id = "orders", ClusterId = "missing", Match = new RouteMatch { Path = "/orders/{**rest}" } }
            ]
        };
        var report = new GatewayConfigValidator().Validate(document);
        Assert.Contains(report.Issues, x => x.Code == "CLUSTER_REFERENCE");
    }

    [Fact]
    public void OpenApi_json_and_yaml_generate_deterministic_routes()
    {
        const string json =
            "{\"openapi\":\"3.1.0\",\"paths\":{\"/orders/{id}\":{\"get\":{\"operationId\":\"GetOrder\"}}}}";
        const string yaml = "openapi: 3.1.0\npaths:\n  /orders/{id}:\n    get:\n      operationId: GetOrder\n";
        var fromJson = OpenApiRouteGenerator.Generate(json, "orders");
        var fromYaml = OpenApiRouteGenerator.Generate(yaml, "orders");
        Assert.Equal("getorder", Assert.Single(fromJson.Routes).Id);
        Assert.Equal(JsonSerializer.Serialize(fromJson.Routes, GatewayJson.Options),
            JsonSerializer.Serialize(fromYaml.Routes, GatewayJson.Options));
        Assert.Empty(fromJson.Issues);
    }

    [Fact]
    public void Cidr_restrictions_support_ipv4_and_ipv6()
    {
        var configured = CidrMatcher.Normalize(["10.20.0.0/16", "2001:db8::/32"]);
        Assert.True(CidrMatcher.Allows(IPAddress.Parse("10.20.4.8"), configured));
        Assert.True(CidrMatcher.Allows(IPAddress.Parse("2001:db8::42"), configured));
        Assert.False(CidrMatcher.Allows(IPAddress.Parse("10.21.4.8"), configured));
        Assert.Throws<ArgumentException>(() => CidrMatcher.Normalize(["not-a-network"]));
    }

    [Fact]
    public void Invalid_cors_and_policy_references_are_rejected()
    {
        var document = new GatewayConfigDocument
        {
            Routes =
            [
                new GatewayRoute
                {
                    Id = "api", ClusterId = "backend",
                    Match = new RouteMatch
                        { Path = "/api", Headers = [new RouteValueMatch("X-Tenant", "value", "Unknown")] },
                    CorsPolicy = "web"
                }
            ],
            Clusters =
            [
                new GatewayCluster
                {
                    Id = "backend",
                    Destinations = new Dictionary<string, GatewayDestination> { ["one"] = new("https://example.test") }
                }
            ],
            Policies = new GatewayPolicies
            {
                Cors = new Dictionary<string, CorsPolicy>
                    { ["web"] = new(["*"], ["GET"], ["*"], AllowCredentials: true) }
            }
        };
        var issues = new GatewayConfigValidator().Validate(document).Issues;
        Assert.Contains(issues, x => x.Code == "HEADER_MATCH_MODE");
        Assert.Contains(issues, x => x.Code == "CORS_CREDENTIALS_WILDCARD");
    }

    [Fact]
    public void Composite_authorization_cycles_are_rejected()
    {
        var document = new GatewayConfigDocument
        {
            Policies = new GatewayPolicies
            {
                Authorization = new Dictionary<string, AuthorizationPolicy>
                {
                    ["first"] = new("anyOf", Policies: ["second"]), ["second"] = new("allOf", Policies: ["first"])
                }
            }
        };
        Assert.Contains(new GatewayConfigValidator().Validate(document).Issues, x => x.Code == "AUTH_COMPOSITE_CYCLE");
    }

    [Fact]
    public void Jwt_clock_skew_is_bounded()
    {
        var document = new GatewayConfigDocument
        {
            Policies = new GatewayPolicies
            {
                Authorization = new Dictionary<string, AuthorizationPolicy>
                {
                    ["jwt"] = new("jwt", Authority: "https://identity.example/", Issuer: "https://identity.example/",
                        Audiences: ["api"], ClockSkew: TimeSpan.FromMinutes(20))
                }
            }
        };
        Assert.Contains(new GatewayConfigValidator().Validate(document).Issues,
            x => x.Code == "JWT_CLOCK_SKEW" && x.JsonPath.EndsWith("/clockSkew", StringComparison.Ordinal));
    }
}