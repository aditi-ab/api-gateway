using System.Net;
using System.Text;
using ApiGateway.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Xunit;

namespace ApiGateway.UnitTests;

public sealed class GatewayPolicyTests
{
    [Fact]
    public async Task Gateway_hosted_maintenance_response_bypasses_the_normal_pipeline()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.Request.Headers.Accept = "application/json";
        var nextCalled = false;
        var route = new GatewayRoute
        {
            Id = "orders", Match = new RouteMatch { Path = "/orders" }, ClusterId = "orders",
            Operations = new RouteOperationalPolicy(RouteOperationalState.Maintenance,
                new RouteUnavailableResponse(Message: "Deploying", RetryAfter: TimeSpan.FromMinutes(5)))
        };

        await ProxyPolicyMiddleware.ApplyOperationalStateAsync(context, route, _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body, Encoding.UTF8)
            .ReadToEndAsync(TestContext.Current.CancellationToken);

        Assert.False(nextCalled);
        Assert.Equal(503, context.Response.StatusCode);
        Assert.Equal("300", context.Response.Headers.RetryAfter);
        Assert.Contains("ROUTE_MAINTENANCE", body);
    }

    [Fact]
    public void Missing_certificate_reference_rejects_activation()
    {
        var cluster = new GatewayCluster
        {
            Id = "secure",
            Destinations = new Dictionary<string, GatewayDestination> { ["one"] = new("https://localhost/") },
            Tls = new UpstreamTlsPolicy("missing")
        };
        var validator = new SecretReferenceValidator(Options.Create(new UpstreamTlsOptions()));
        var exception = Assert.Throws<InvalidOperationException>(() =>
            validator.Validate(new GatewayConfigDocument { Clusters = [cluster] }));
        Assert.Contains("missing", exception.Message);
    }

    [Fact]
    public void Fixed_window_rate_limit_rejects_excess_requests()
    {
        var limiter = new DynamicRateLimiter();
        var context = new DefaultHttpContext();
        var policy = new RateLimitPolicy("FixedWindow", 1, TimeSpan.FromMinutes(1));
        Assert.True(limiter.TryAcquire("route", "standard", policy, context, out _));
        Assert.False(limiter.TryAcquire("route", "standard", policy, context, out var retry));
        Assert.True(retry > TimeSpan.Zero);
    }

    [Fact]
    public async Task Fixed_window_rate_limit_honors_the_bounded_queue()
    {
        var limiter = new DynamicRateLimiter();
        var context = new DefaultHttpContext();
        var policy = new RateLimitPolicy("fixedWindow", 1, TimeSpan.FromMilliseconds(30), 1);
        Assert.True(limiter.TryAcquire("route", "queued", policy, context, out _));
        var queued = await limiter.AcquireAsync("route", "queued", policy, context);
        Assert.True(queued.Acquired);
        Assert.True(queued.Queued);
        queued.Dispose();
    }

    [Theory]
    [InlineData("slidingWindow")]
    [InlineData("tokenBucket")]
    public void Time_based_rate_limiters_reject_excess_requests(string type)
    {
        var limiter = new DynamicRateLimiter();
        var context = new DefaultHttpContext();
        var policy = new RateLimitPolicy(type, 1, TimeSpan.FromMinutes(1), TokensPerPeriod: 1);
        Assert.True(limiter.TryAcquire("route", type, policy, context, out _));
        Assert.False(limiter.TryAcquire("route", type, policy, context, out _));
    }

    [Fact]
    public async Task Concurrency_limit_holds_the_permit_until_the_request_completes()
    {
        var limiter = new DynamicRateLimiter();
        var context = new DefaultHttpContext();
        var policy = new RateLimitPolicy("concurrency", 1, QueueLimit: 1);
        using var first = await limiter.AcquireAsync("route", "concurrent", policy, context);
        Assert.True(first.Acquired);
        var waiting = limiter.AcquireAsync("route", "concurrent", policy, context);
        await Task.Delay(25, TestContext.Current.CancellationToken);
        Assert.False(waiting.IsCompleted);
        first.Dispose();
        using var second = await waiting;
        Assert.True(second.Acquired);
        Assert.True(second.Queued);
    }

    [Fact]
    public void Yarp_mapping_includes_http_health_and_affinity_settings()
    {
        var cluster = new GatewayCluster
        {
            Id = "upstream",
            Destinations = new Dictionary<string, GatewayDestination> { ["one"] = new("https://example.test/") },
            Health = new HealthPolicy(true, ActivePolicy: "ConsecutiveFailures", PassivePolicy: "TransportFailureRate",
                AvailableDestinationsPolicy: "HealthyOrPanic", Query: "tenant=one"),
            SessionAffinity = new SessionAffinityPolicy(Path: "/api", SecurePolicy: "Always", SameSite: "Strict",
                Expiration: TimeSpan.FromHours(1)),
            HttpClient = new UpstreamHttpPolicy("1.1", "RequestVersionExact", true, true, TimeSpan.FromMinutes(5), 20)
        };
        var mapped = Assert.Single(YarpConfigMapper.Map(new GatewayConfigDocument { Clusters = [cluster] }).Clusters);
        Assert.Equal(HttpVersion.Version11, mapped.HttpRequest?.Version);
        Assert.Equal(HttpVersionPolicy.RequestVersionExact, mapped.HttpRequest?.VersionPolicy);
        Assert.Equal(20, mapped.HttpClient?.MaxConnectionsPerServer);
        Assert.Equal("tenant=one", mapped.HealthCheck?.Active?.Query);
        Assert.Equal("/api", mapped.SessionAffinity?.Cookie?.Path);
    }
}