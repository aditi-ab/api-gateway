using System.Text;
using System.Text.Json;
using ApiGateway.Application;
using ApiGateway.Domain;
using ApiGateway.Management;
using ApiGateway.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.LoadBalancing;
using Yarp.ReverseProxy.Transforms.Builder;
using RouteMatch = ApiGateway.Domain.RouteMatch;

namespace ApiGateway.IntegrationTests;

public sealed class LifecycleTests
{
    [Fact]
    public async Task Publish_and_rollback_preserve_immutable_revisions()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(ct);
        var options = new DbContextOptionsBuilder<GatewayDbContext>().UseSqlite(connection).Options;
        await using var db = new GatewayDbContext(options);
        await db.Database.EnsureCreatedAsync(ct);
        var service = new GatewayLifecycleService(db, new GatewayConfigValidator());
        var environment = await service.CreateEnvironmentAsync("development", "Development", null, "test", "one", ct);
        var draft = await service.CreateDraftAsync(environment.Id, null, "test", "two", ct);
        var published = await service.PublishAsync(draft.Id, draft.ConcurrencyVersion, "initial", "test", "three", ct);
        Assert.Equal(RevisionState.Published, published.State);
        Assert.Equal(published.Id, (await db.Environments.SingleAsync(ct)).ActiveRevisionId);
        var second = await service.CreateDraftAsync(environment.Id, published.Id, "test", "four", ct);
        var secondPublished =
            await service.PublishAsync(second.Id, second.ConcurrencyVersion, null, "test", "five", ct);
        await service.RollbackAsync(environment.Id, published.Id, "test", "six", ct);
        Assert.Equal(published.Id, (await db.Environments.SingleAsync(ct)).ActiveRevisionId);
        Assert.All(await db.Revisions.AsNoTracking().ToListAsync(ct),
            x => Assert.Equal(RevisionState.Published, x.State));
        Assert.Equal(2, secondPublished.Number);
    }

    [Fact]
    public async Task Stale_draft_version_is_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(ct);
        var options = new DbContextOptionsBuilder<GatewayDbContext>().UseSqlite(connection).Options;
        await using var db = new GatewayDbContext(options);
        await db.Database.EnsureCreatedAsync(ct);
        var service = new GatewayLifecycleService(db, new GatewayConfigValidator());
        var environment = await service.CreateEnvironmentAsync("testing", "Testing", null, "test", "one", ct);
        var draft = await service.CreateDraftAsync(environment.Id, null, "test", "two", ct);
        var stale = draft.ConcurrencyVersion;
        _ = await service.SetDraftContentAsync(draft.Id, stale, draft.ConfigJson, "test", "three", ct);
        await Assert.ThrowsAsync<GatewayConflictException>(() =>
            service.SetDraftContentAsync(draft.Id, stale, draft.ConfigJson, "test", "four", ct));
    }

    [Fact]
    public async Task Environment_edits_archive_and_draft_deletion_are_concurrency_safe()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(ct);
        var options = new DbContextOptionsBuilder<GatewayDbContext>().UseSqlite(connection).Options;
        await using var db = new GatewayDbContext(options);
        await db.Database.EnsureCreatedAsync(ct);
        var service = new GatewayLifecycleService(db, new GatewayConfigValidator());
        var environment = await service.CreateEnvironmentAsync("staging", "Staging", null, "test", "one", ct);
        var stale = environment.ConcurrencyVersion;
        environment = await service.UpdateEnvironmentAsync(environment.Id, stale, "Pre-production",
            "Release validation", "test", "two", ct);
        await Assert.ThrowsAsync<GatewayConflictException>(() =>
            service.UpdateEnvironmentAsync(environment.Id, stale, "Wrong", null, "test", "three", ct));
        environment = await service.SetEnvironmentArchivedAsync(environment.Id, environment.ConcurrencyVersion, true,
            "test", "four", ct);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateDraftAsync(environment.Id, null, "test", "five", ct));
        environment = await service.SetEnvironmentArchivedAsync(environment.Id, environment.ConcurrencyVersion, false,
            "test", "six", ct);
        var draft = await service.CreateDraftAsync(environment.Id, null, "test", "seven", ct);
        await service.DeleteDraftAsync(draft.Id, draft.ConcurrencyVersion, "test", "eight", ct);
        Assert.Empty(await db.Revisions.AsNoTracking().ToListAsync(ct));
        Assert.Contains(await db.AuditEvents.AsNoTracking().ToListAsync(ct), x => x.Action == "DraftDeleted");
    }

    [Fact]
    public async Task Api_key_lifecycle_is_audited_without_storing_the_secret()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(ct);
        var options = new DbContextOptionsBuilder<GatewayDbContext>().UseSqlite(connection).Options;
        await using var db = new GatewayDbContext(options);
        await db.Database.EnsureCreatedAsync(ct);
        var service = new ApiKeyService(db);
        var created =
            await service.CreateManagementAsync("deployment", ["config:read"], null, null, "test", "create", ct);
        await service.RevokeManagementAsync(created.Id, "test", "revoke", ct);
        var stored = await db.ManagementApiKeys.AsNoTracking().SingleAsync(ct);
        Assert.NotEqual(Encoding.UTF8.GetBytes(created.Secret), stored.KeyHash);
        var events = await db.AuditEvents.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct);
        Assert.Equal(["ManagementApiKeyCreated", "ManagementApiKeyRevoked"], events.Select(x => x.Action));
        Assert.DoesNotContain(events, x => x.DetailsJson.Contains(created.Secret, StringComparison.Ordinal));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateManagementAsync("invalid", ["Reader"], null, null, "test", "invalid", ct));
    }

    [Fact]
    public async Task Promotion_creates_an_unpublished_draft_in_the_target_environment()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(ct);
        var options = new DbContextOptionsBuilder<GatewayDbContext>().UseSqlite(connection).Options;
        await using var db = new GatewayDbContext(options);
        await db.Database.EnsureCreatedAsync(ct);
        var service = new GatewayLifecycleService(db, new GatewayConfigValidator());
        var source = await service.CreateEnvironmentAsync("source", "Source", null, "test", "one", ct);
        var target = await service.CreateEnvironmentAsync("target", "Target", null, "test", "two", ct);
        var draft = await service.CreateDraftAsync(source.Id, null, "test", "three", ct);
        var published = await service.PublishAsync(draft.Id, draft.ConcurrencyVersion, null, "test", "four", ct);
        var promoted = await service.PromoteAsync(published.Id, target.Id, "test", "five", ct);
        Assert.Equal(target.Id, promoted.EnvironmentId);
        Assert.Equal(RevisionState.Draft, promoted.State);
        Assert.Equal(published.ContentHash, promoted.ContentHash);
        Assert.Null((await db.Environments.AsNoTracking().SingleAsync(x => x.Id == target.Id, ct)).ActiveRevisionId);
    }

    [Fact]
    public async Task Retention_uses_a_lease_and_records_deleted_counts()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(ct);
        var options = new DbContextOptionsBuilder<GatewayDbContext>().UseSqlite(connection).Options;
        await using var db = new GatewayDbContext(options);
        await db.Database.EnsureCreatedAsync(ct);
        db.ActivationEvents.Add(new GatewayActivationEvent
        {
            EnvironmentId = Guid.NewGuid(), InstanceId = "old", Outcome = ActivationOutcome.Succeeded,
            StartedAtUtc = DateTimeOffset.UtcNow.AddDays(-40), CompletedAtUtc = DateTimeOffset.UtcNow.AddDays(-40)
        });
        db.AuditEvents.Add(new AuditEvent
        {
            ActorType = "User", ActorId = "old", Action = "Old", TargetType = "Test", TargetId = "old",
            CorrelationId = "old", OccurredAtUtc = DateTimeOffset.UtcNow.AddDays(-400)
        });
        await db.SaveChangesAsync(ct);
        var result = await new RetentionMaintenanceService(db).RunAsync("instance-one",
            DateTimeOffset.UtcNow.AddDays(-30), DateTimeOffset.UtcNow.AddDays(-365), "test", "retention", ct);
        Assert.True(result.LeaseAcquired);
        Assert.Equal(1, result.ActivationEventsDeleted);
        Assert.Equal(1, result.AuditEventsDeleted);
        Assert.Contains(await db.AuditEvents.AsNoTracking().ToListAsync(ct),
            x => x.Action == "RetentionMaintenanceCompleted");
    }

    [Fact]
    public async Task Import_export_and_granular_draft_edits_preserve_concurrency()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(ct);
        await using var db =
            new GatewayDbContext(new DbContextOptionsBuilder<GatewayDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync(ct);
        var service = new GatewayLifecycleService(db, new GatewayConfigValidator());
        var environment = await service.CreateEnvironmentAsync("contracts", "Contracts", null, "test", "one", ct);
        var draft = await service.CreateDraftAsync(environment.Id, null, "test", "two", ct);
        var cluster = new GatewayCluster
        {
            Id = "backend",
            Destinations = new Dictionary<string, GatewayDestination> { ["one"] = new("https://example.test/") }
        };
        draft = await service.UpsertClusterAsync(draft.Id, draft.ConcurrencyVersion,
            JsonSerializer.Serialize(cluster, GatewayJson.Options), "test", "three", ct);
        var route = new GatewayRoute
        {
            Id = "orders", ClusterId = "backend", Match = new RouteMatch { Path = "/orders/{**rest}" },
            AuthorizationPolicy = "Anonymous"
        };
        draft = await service.UpsertRouteAsync(draft.Id, draft.ConcurrencyVersion,
            JsonSerializer.Serialize(route, GatewayJson.Options), "test", "four", ct);
        var exported = await service.ExportRevisionAsync(draft.Id, ct);
        Assert.Contains("orders", exported, StringComparison.Ordinal);
        var stale = draft.ConcurrencyVersion;
        draft = await service.ImportDraftAsync(draft.Id, draft.ConcurrencyVersion, exported, "test", "five", ct);
        await Assert.ThrowsAsync<GatewayConflictException>(() =>
            service.ImportDraftAsync(draft.Id, stale, exported, "test", "six", ct));
    }

    [Fact]
    public async Task OpenApi_preview_requires_explicit_conflict_resolution()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(ct);
        await using var db =
            new GatewayDbContext(new DbContextOptionsBuilder<GatewayDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync(ct);
        var lifecycle = new GatewayLifecycleService(db, new GatewayConfigValidator());
        var environment = await lifecycle.CreateEnvironmentAsync("openapi", "OpenAPI", null, "test", "one", ct);
        var draft = await lifecycle.CreateDraftAsync(environment.Id, null, "test", "two", ct);
        var document = new GatewayConfigDocument
        {
            Routes =
            [
                new GatewayRoute
                {
                    Id = "listpets", ClusterId = "backend", Match = new RouteMatch { Path = "/old" },
                    AuthorizationPolicy = "Anonymous"
                }
            ],
            Clusters =
            [
                new GatewayCluster
                {
                    Id = "backend",
                    Destinations = new Dictionary<string, GatewayDestination> { ["one"] = new("https://example.test/") }
                }
            ]
        };
        draft = await lifecycle.SetDraftContentAsync(draft.Id, draft.ConcurrencyVersion,
            ConfigDocuments.Serialize(document), "test", "three", ct);
        var importer = new OpenApiImportService(new EphemeralDataProtectionProvider(), db, lifecycle,
            new GatewayConfigurationService(db, new GatewayConfigValidator()));
        var preview = await importer.PreviewAsync(draft.Id, draft.ConcurrencyVersion,
            "{\"openapi\":\"3.0.0\",\"paths\":{\"/pets\":{\"get\":{\"operationId\":\"listPets\"}}}}", "backend", null,
            ct);
        Assert.Single(preview.Conflicts);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            importer.ApplyAsync(preview.Token, draft.ConcurrencyVersion, [], "test", "four", ct));
        var applied = await importer.ApplyAsync(preview.Token, draft.ConcurrencyVersion,
            [new OpenApiConflictResolutionInput("listpets", "replace")], "test", "five", ct);
        Assert.Contains("/pets", applied.ConfigJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Management_validation_accepts_the_custom_weighted_pool_policy()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddReverseProxy();
        services.AddSingleton<ILoadBalancingPolicy, WeightedPoolValidationPolicy>();
        await using var provider = services.BuildServiceProvider();
        var validator = new YarpPublicationValidator(provider.GetRequiredService<IConfigValidator>(),
            provider.GetRequiredService<ITransformBuilder>());
        var cluster = new GatewayCluster
        {
            Id = "canary",
            Destinations = new Dictionary<string, GatewayDestination>
            {
                ["stable"] = new("https://stable.example/", Pool: "stable"),
                ["canary"] = new("https://canary.example/", Pool: "canary")
            },
            Traffic = new TrafficPolicy(new Dictionary<string, int> { ["stable"] = 90, ["canary"] = 10 })
        };
        var issues = await validator.ValidateAsync(new GatewayConfigDocument { Clusters = [cluster] },
            TestContext.Current.CancellationToken);
        Assert.DoesNotContain(issues, issue => issue.Code == "YARP_CLUSTER");
    }

    [Fact]
    public async Task Management_validation_accepts_preserving_the_original_host()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddReverseProxy();
        await using var provider = services.BuildServiceProvider();
        var validator = new YarpPublicationValidator(provider.GetRequiredService<IConfigValidator>(),
            provider.GetRequiredService<ITransformBuilder>());
        var document = new GatewayConfigDocument
        {
            Routes =
            [
                new GatewayRoute
                {
                    Id = "iis", ClusterId = "iis", Match = new RouteMatch { Path = "/{**remainder}" },
                    Transforms = [new Dictionary<string, string> { ["RequestHeaderOriginalHost"] = "true" }]
                }
            ],
            Clusters =
            [
                new GatewayCluster
                {
                    Id = "iis",
                    Destinations = new Dictionary<string, GatewayDestination> { ["primary"] = new("http://192.0.2.1/") }
                }
            ]
        };

        var issues = await validator.ValidateAsync(document, TestContext.Current.CancellationToken);

        Assert.DoesNotContain(issues, issue => issue.Code == "YARP_TRANSFORM");
    }

    [Fact]
    public async Task Management_validation_accepts_canonical_punycode_route_hosts()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddReverseProxy();
        await using var provider = services.BuildServiceProvider();
        var validator = new YarpPublicationValidator(provider.GetRequiredService<IConfigValidator>(),
            provider.GetRequiredService<ITransformBuilder>());
        var document = new GatewayConfigDocument
        {
            Routes =
            [
                new GatewayRoute
                {
                    Id = "swedish-site", ClusterId = "swedish-site",
                    Match = new RouteMatch
                    {
                        Path = "/{**remainder}", Hosts = ["xn--sjgrsstigen-o8a5u.se"]
                    }
                }
            ]
        };

        var issues = await validator.ValidateAsync(document, TestContext.Current.CancellationToken);

        Assert.DoesNotContain(issues, issue => issue.Code == "YARP_ROUTE");
    }
}