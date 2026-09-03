using ApiGateway.Application;
using ApiGateway.Domain;
using Xunit;

namespace ApiGateway.UnitTests;

public sealed class RouteFirstTests
{
    [Fact]
    public void Named_upstream_can_be_shared_by_multiple_routes()
    {
        var document = NamedUpstreamCompiler.Create(new GatewayConfigDocument(),
            new SaveNamedUpstreamInput("Orders pool", new Dictionary<string, GatewayDestination>
            {
                ["orders-1"] = new("https://orders-1.example/"),
                ["orders-2"] = new("https://orders-2.example/")
            }, "RoundRobin", new HealthPolicy(ActiveEnabled: true, Path: "/ready")), out var upstream);
        document = ManagedRouteCompiler.Create(document,
            new CreateManagedRouteInput("Orders", "/orders", UpstreamId: upstream.Id), out var first);
        document = ManagedRouteCompiler.Create(document,
            new CreateManagedRouteInput("Order admin", "/admin/orders", UpstreamId: upstream.Id), out var second);

        Assert.Equal(upstream.Id, document.Routes.Single(x => x.Id == first.Id).ClusterId);
        Assert.Equal(upstream.Id, document.Routes.Single(x => x.Id == second.Id).ClusterId);
        Assert.Single(document.Clusters);
        var mapped = YarpConfigMapper.Map(document);
        Assert.Equal(2, mapped.Routes.Count);
        Assert.Single(mapped.Clusters);
        Assert.Equal(2, ManagedRouteCompiler.ToManaged(document, document.Routes[0]).Upstream.Destinations!.Count);
        Assert.Throws<InvalidOperationException>(() =>
            NamedUpstreamCompiler.Delete(document, upstream.Id, upstream.Version, out _));

        document = ManagedRouteCompiler.Delete(document, first.Id, first.Version, out _);
        document = ManagedRouteCompiler.Delete(document, second.Id, second.Version, out _);
        document = NamedUpstreamCompiler.Delete(document, upstream.Id, upstream.Version, out _);
        Assert.Empty(document.Clusters);
    }

    [Fact]
    public void Three_fields_compile_to_an_owned_route_and_upstream()
    {
        var document = ManagedRouteCompiler.Create(new GatewayConfigDocument(),
            new CreateManagedRouteInput("Orders API", "/orders/{**remainder}", "https://orders.example/"),
            out var route);

        Assert.Equal("orders-api", route.Id);
        Assert.Equal("Orders API", route.Name);
        Assert.Empty(route.Match.Methods);
        Assert.Equal("https://orders.example/", route.Upstream.Url);
        Assert.Equal("__route_orders-api_upstream", document.Routes.Single().ClusterId);
        Assert.Equal("orders-api", document.Clusters.Single().Metadata.ManagedByRouteId);
        Assert.Equal(3, document.SchemaVersion);
        var report = new GatewayConfigValidator().Validate(document);
        Assert.True(report.IsValid, string.Join(Environment.NewLine,
            report.Issues.Select(x => $"{x.Code}: {x.Message}")));
    }

    [Fact]
    public void Feature_compilation_is_typed_and_stale_route_versions_are_rejected()
    {
        var document = ManagedRouteCompiler.Create(new GatewayConfigDocument(),
            new CreateManagedRouteInput("Orders", "/orders", "https://orders.example/"), out var route);
        var input = new UpdateManagedRouteInput(route.Name, route.Enabled, route.Match, route.Upstream,
            route.Features with
            {
                RateLimit = new RateLimitPolicy("fixedWindow", 100, TimeSpan.FromMinutes(1)),
                Access = new RouteAccessPolicy(["10.0.0.0/8"], MaximumRequestBodyBytes: 1_000_000),
                ResponseCache = new ResponseCachePolicy(TimeSpan.FromMinutes(1))
            });

        document = ManagedRouteCompiler.Update(document, route.Id, route.Version, input, out var updated);

        Assert.NotEqual(route.Version, updated.Version);
        Assert.NotNull(document.Policies.RateLimits.Single().Value);
        Assert.Equal(1_000_000, document.Routes.Single().Access?.MaximumRequestBodyBytes);
        Assert.Throws<ManagedRouteConflictException>(() =>
            ManagedRouteCompiler.Update(document, route.Id, route.Version, input, out _));
    }

    [Fact]
    public void Disabled_features_keep_their_configuration_but_are_omitted_from_the_runtime_snapshot()
    {
        var document = ManagedRouteCompiler.Create(new GatewayConfigDocument(),
            new CreateManagedRouteInput("Orders", "/orders", "https://orders.example/"), out var route);
        var input = new UpdateManagedRouteInput(route.Name, route.Enabled, route.Match, route.Upstream,
            route.Features with
            {
                Authorization = new AuthorizationPolicy("apiKey"),
                RateLimit = new RateLimitPolicy("fixedWindow", 100, TimeSpan.FromMinutes(1)),
                Access = new RouteAccessPolicy(["10.0.0.0/8"], MaximumRequestBodyBytes: 1_000_000),
                DisabledFeatures = ["authorization", "rate-limit", "ip-restrictions"]
            });

        document = ManagedRouteCompiler.Update(document, route.Id, route.Version, input, out var updated);
        var effective = GatewayFeatureSwitches.Apply(document);
        var effectiveRoute = Assert.Single(effective.Routes);

        Assert.NotNull(updated.Features.Authorization);
        Assert.NotNull(updated.Features.RateLimit);
        Assert.Equal(["authorization", "ip-restrictions", "rate-limit"], updated.Features.DisabledFeatures);
        Assert.Equal("Anonymous", effectiveRoute.AuthorizationPolicy);
        Assert.Null(effectiveRoute.RateLimitPolicy);
        Assert.Null(effectiveRoute.Access!.AllowedCidrs);
        Assert.Equal(1_000_000, effectiveRoute.Access.MaximumRequestBodyBytes);

        var mapped = Assert.Single(YarpConfigMapper.Map(document).Routes);
        Assert.Equal("Anonymous", mapped.Metadata!["ApiGateway.Authorization"]);
        Assert.Equal(string.Empty, mapped.Metadata["ApiGateway.RateLimit"]);
    }

    [Fact]
    public void Advanced_matching_compiles_hosts_headers_query_parameters_and_precedence()
    {
        var document = ManagedRouteCompiler.Create(new GatewayConfigDocument(),
            new CreateManagedRouteInput("Website", "/{**remainder}", "https://another-example.com/"), out var route);
        var match = route.Match with
        {
            Hosts = ["example.com", "*.example.com"],
            Methods = ["GET", "HEAD"],
            Headers = [new RouteValueMatch("X-Tenant", "north", "Exact", true)],
            QueryParameters = [new RouteValueMatch("preview", "", "Exists")]
        };

        document = ManagedRouteCompiler.Update(document, route.Id, route.Version,
            new UpdateManagedRouteInput(route.Name, route.Enabled, match, route.Upstream, route.Features, 10),
            out var updated);

        Assert.Equal(["example.com", "*.example.com"], updated.Match.Hosts);
        Assert.Equal("X-Tenant", updated.Match.Headers.Single().Name);
        Assert.Equal("preview", updated.Match.QueryParameters.Single().Name);
        Assert.Equal(10, updated.Order);
        Assert.True(new GatewayConfigValidator().Validate(document).IsValid);
    }

    [Fact]
    public void Internationalized_route_hosts_are_normalized_and_deduplicated()
    {
        var document = ManagedRouteCompiler.Create(new GatewayConfigDocument(),
            new CreateManagedRouteInput("Swedish website", "/{**remainder}", "https://another-example.com/"),
            out var route);
        var match = route.Match with
        {
            Hosts = ["sjögrässtigen.se", "xn--sjgrsstigen-o8a5u.se", "*.sjögrässtigen.se"]
        };

        ManagedRouteCompiler.Update(document, route.Id, route.Version,
            new UpdateManagedRouteInput(route.Name, route.Enabled, match, route.Upstream, route.Features),
            out var updated);

        Assert.Equal(["xn--sjgrsstigen-o8a5u.se", "*.xn--sjgrsstigen-o8a5u.se"], updated.Match.Hosts);
    }

    [Fact]
    public void Internationalized_route_hosts_are_mapped_to_unicode_for_yarp()
    {
        var document = ManagedRouteCompiler.Create(new GatewayConfigDocument(),
            new CreateManagedRouteInput("Swedish website", "/{**remainder}", "https://another-example.com/"),
            out var route);
        var match = route.Match with { Hosts = ["sjögrässtigen.se", "*.sjögrässtigen.se"] };
        document = ManagedRouteCompiler.Update(document, route.Id, route.Version,
            new UpdateManagedRouteInput(route.Name, route.Enabled, match, route.Upstream, route.Features),
            out var updated);

        var mapped = Assert.Single(YarpConfigMapper.Map(document).Routes);

        Assert.Equal(["xn--sjgrsstigen-o8a5u.se", "*.xn--sjgrsstigen-o8a5u.se"], updated.Match.Hosts);
        Assert.Equal(["sjögrässtigen.se", "*.sjögrässtigen.se"], mapped.Match.Hosts);
    }

    [Fact]
    public void Duplicate_copies_the_complete_route_with_a_new_identity_and_disabled_state()
    {
        var document = ManagedRouteCompiler.Create(new GatewayConfigDocument(),
            new CreateManagedRouteInput("Orders", "/orders", "https://orders.example/"), out var route);
        var configured = new UpdateManagedRouteInput(route.Name, true,
            route.Match with { Methods = ["POST"], Hosts = ["api.example.com"] },
            route.Upstream with
            {
                Destinations = new Dictionary<string, GatewayDestination>
                {
                    ["primary"] = new("https://orders.example/"),
                    ["secondary"] = new("https://orders-2.example/")
                }
            },
            route.Features with { RateLimit = new RateLimitPolicy("fixedWindow", 100, TimeSpan.FromMinutes(1)) },
            10, new RouteOperationalPolicy(RouteOperationalState.Maintenance),
            Inbound: new InboundRoutePolicy(InboundScheme.HttpsRedirect, Guid.NewGuid()));
        document = ManagedRouteCompiler.Update(document, route.Id, route.Version, configured, out var updated);

        document = ManagedRouteCompiler.Duplicate(document, updated.Id, updated.Version, "Orders copy",
            out var duplicate);

        Assert.Equal("orders-copy", duplicate.Id);
        Assert.Equal("Orders copy", duplicate.Name);
        Assert.False(duplicate.Enabled);
        Assert.Equal(updated.Match, duplicate.Match);
        Assert.Equal(updated.Upstream, duplicate.Upstream);
        Assert.Equal(updated.Features, duplicate.Features);
        Assert.Equal(updated.Operations, duplicate.Operations);
        Assert.Equal(updated.Inbound, duplicate.Inbound);
        Assert.Equal(2, document.Routes.Count);
    }

    [Fact]
    public void Operational_states_are_versioned_and_compile_a_dedicated_unavailable_upstream()
    {
        var document = ManagedRouteCompiler.Create(new GatewayConfigDocument(),
            new CreateManagedRouteInput("Orders", "/orders", "https://orders.example/"), out var route);
        var operations = new RouteOperationalPolicy(RouteOperationalState.Maintenance,
            new RouteUnavailableResponse(503, "Maintenance", "Try again later", TimeSpan.FromMinutes(5),
                "https://maintenance.example/"));

        document = ManagedRouteCompiler.Update(document, route.Id, route.Version,
            new UpdateManagedRouteInput(route.Name, route.Enabled, route.Match, route.Upstream, route.Features,
                Operations: operations), out var updated);
        var mapped = YarpConfigMapper.Map(document);

        Assert.NotEqual(route.Version, updated.Version);
        Assert.Equal(RouteOperationalState.Maintenance, updated.Operations!.State);
        Assert.Equal("__route_orders_unavailable", Assert.Single(mapped.Routes).ClusterId);
        Assert.Contains(mapped.Clusters, x => x.ClusterId == "__route_orders_unavailable" &&
                                              x.Destinations!["unavailable"].Address ==
                                              "https://maintenance.example/");
        Assert.Contains("\"state\":\"maintenance\"", ConfigDocuments.Serialize(document));
        Assert.True(new GatewayConfigValidator().Validate(document).IsValid);
    }

    [Fact]
    public void Operational_states_resolve_shared_route_overrides_and_environment_defaults()
    {
        var document = ManagedRouteCompiler.Create(new GatewayConfigDocument(),
            new CreateManagedRouteInput("Orders", "/orders", "https://orders.example/"), out var route);
        document = document with
        {
            UnavailableResponseProfiles = new Dictionary<string, RouteUnavailableResponseProfile>
            {
                ["default-maintenance"] = new("default-maintenance", "Default maintenance",
                    new RouteUnavailableResponse(UpstreamUrl: "https://default.example/")),
                ["orders-maintenance"] = new("orders-maintenance", "Orders maintenance",
                    new RouteUnavailableResponse(UpstreamUrl: "https://orders-maintenance.example/"))
            },
            OperationalDefaults = new RouteOperationalDefaults(MaintenanceProfileId: "default-maintenance")
        };
        document = ManagedRouteCompiler.Update(document, route.Id, route.Version,
            new UpdateManagedRouteInput(route.Name, route.Enabled, route.Match, route.Upstream, route.Features,
                Operations: new RouteOperationalPolicy(RouteOperationalState.Maintenance,
                    ResponseProfileId: "orders-maintenance")), out var updated);

        var mapped = YarpConfigMapper.Map(document);
        Assert.Equal("orders-maintenance", updated.Operations.ResponseProfileId);
        Assert.Equal("https://orders-maintenance.example/",
            mapped.Clusters.Single(x => x.ClusterId == "__route_orders_unavailable")
                .Destinations!["unavailable"].Address);
        Assert.True(new GatewayConfigValidator().Validate(document).IsValid);

        var inherited = document with
        {
            Routes =
            [
                document.Routes.Single() with
                {
                    Operations = new RouteOperationalPolicy(RouteOperationalState.Maintenance)
                }
            ]
        };
        mapped = YarpConfigMapper.Map(inherited);
        Assert.Equal("https://default.example/",
            mapped.Clusters.Single(x => x.ClusterId == "__route_orders_unavailable")
                .Destinations!["unavailable"].Address);
    }

    [Fact]
    public void Active_route_request_tracking_is_released_when_a_request_finishes()
    {
        var tracker = new RouteRequestTracker();
        using (tracker.Enter("orders"))
        {
            Assert.Equal(1, tracker.Snapshot()["orders"]);
        }

        Assert.Empty(tracker.Snapshot());
    }
}
