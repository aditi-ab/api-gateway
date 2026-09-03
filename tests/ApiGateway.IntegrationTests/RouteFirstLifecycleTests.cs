using ApiGateway.Application;
using ApiGateway.Domain;
using ApiGateway.Management;
using ApiGateway.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ApiGateway.IntegrationTests;

public sealed class RouteFirstLifecycleTests
{
    [Fact]
    public async Task Named_upstream_lifecycle_is_published_and_routes_can_reference_it()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(ct);
        await using var db = new GatewayDbContext(
            new DbContextOptionsBuilder<GatewayDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync(ct);
        var environment = await new GatewayLifecycleService(db, new GatewayConfigValidator())
            .CreateEnvironmentAsync("upstreams", "Upstreams", null, "test", "environment", ct);
        var service = new GatewayConfigurationService(db, new GatewayConfigValidator());
        var input = new SaveNamedUpstreamInput("Orders pool", new Dictionary<string, GatewayDestination>
        {
            ["orders-1"] = new("https://orders-1.example/"),
            ["orders-2"] = new("https://orders-2.example/")
        }, "RoundRobin", new HealthPolicy(ActiveEnabled: true, Path: "/ready"));

        var created = await service.CreateUpstreamAsync(environment.Id, input, "test", "upstream", ct);
        var upstream = Assert.Single(await service.UpstreamsAsync(environment.Id, ct));
        var route = await service.CreateRouteAsync(environment.Id,
            new CreateManagedRouteInput("Orders", "/orders", UpstreamId: upstream.Id), "test", "route", ct);

        Assert.Equal("Upstream", created.Revision.ChangedResourceType);
        Assert.Equal(upstream.Id, route.Route!.Upstream.UpstreamId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteUpstreamAsync(environment.Id,
            upstream.Id, upstream.Version, "test", "delete", ct));
    }

    [Fact]
    public async Task Route_changes_publish_automatically_and_revert_preserves_unrelated_routes()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(ct);
        await using var db = new GatewayDbContext(
            new DbContextOptionsBuilder<GatewayDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync(ct);
        var environment = await new GatewayLifecycleService(db, new GatewayConfigValidator())
            .CreateEnvironmentAsync("routes", "Routes", null, "test", "environment", ct);
        var service = new GatewayConfigurationService(db, new GatewayConfigValidator());

        var first = await service.CreateRouteAsync(environment.Id,
            new CreateManagedRouteInput("Orders", "/orders", "https://orders.example/"), "test", "one", ct);
        var second = await service.CreateRouteAsync(environment.Id,
            new CreateManagedRouteInput("Customers", "/customers", "https://customers.example/"), "test", "two", ct);

        Assert.Equal(RevisionState.Published, first.Revision.State);
        Assert.Equal(2, (await service.RoutesAsync(environment.Id, null, ct)).Count);
        var reverted = await service.RevertAsync(environment.Id, first.Revision.Id, second.Revision.Id, "test",
            "revert", ct);

        Assert.Equal("ChangeReverted", reverted.Revision.ChangeKind);
        Assert.Equal("customers", Assert.Single(await service.RoutesAsync(environment.Id, null, ct)).Id);
        Assert.Equal(3, await db.Revisions.CountAsync(x => x.State == RevisionState.Published, ct));
        Assert.Equal(4, await db.AuditEvents.CountAsync(ct));
    }

    [Fact]
    public async Task Invalid_route_change_does_not_move_the_active_configuration()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(ct);
        await using var db = new GatewayDbContext(
            new DbContextOptionsBuilder<GatewayDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync(ct);
        var environment = await new GatewayLifecycleService(db, new GatewayConfigValidator())
            .CreateEnvironmentAsync("invalid", "Invalid", null, "test", "environment", ct);
        var service = new GatewayConfigurationService(db, new GatewayConfigValidator());
        var created = await service.CreateRouteAsync(environment.Id,
            new CreateManagedRouteInput("Valid", "/valid", "https://valid.example/"), "test", "one", ct);

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateRouteAsync(environment.Id,
            new CreateManagedRouteInput("Broken", "missing-slash", "https://valid.example/"), "test", "two", ct));

        Assert.Equal(created.Revision.Id,
            (await db.Environments.AsNoTracking().SingleAsync(x => x.Id == environment.Id, ct)).ActiveRevisionId);
    }

    [Fact]
    public async Task Duplicated_route_is_published_disabled_and_audited_as_one_change()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(ct);
        await using var db = new GatewayDbContext(
            new DbContextOptionsBuilder<GatewayDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync(ct);
        var environment = await new GatewayLifecycleService(db, new GatewayConfigValidator())
            .CreateEnvironmentAsync("duplicate", "Duplicate", null, "test", "environment", ct);
        var service = new GatewayConfigurationService(db, new GatewayConfigValidator());
        var created = await service.CreateRouteAsync(environment.Id,
            new CreateManagedRouteInput("Orders", "/orders", "https://orders.example/"), "test", "create", ct);

        var result = await service.DuplicateRouteAsync(environment.Id, created.Route!.Id, created.Route.Version,
            "Orders copy", "test", "duplicate", ct);

        Assert.False(result.Route!.Enabled);
        Assert.Equal("orders-copy", result.Route.Id);
        Assert.Equal("RouteDuplicated", result.Revision.ChangeKind);
        Assert.Equal("Duplicated route as Orders copy", result.Revision.ChangeSummary);
        Assert.Equal(2, (await service.RoutesAsync(environment.Id, null, ct)).Count);
    }

    [Fact]
    public async Task Basic_route_update_can_strip_and_restore_the_incoming_path_prefix()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(ct);
        await using var db = new GatewayDbContext(
            new DbContextOptionsBuilder<GatewayDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync(ct);
        var environment = await new GatewayLifecycleService(db, new GatewayConfigValidator())
            .CreateEnvironmentAsync("path-rewrite", "Path rewrite", null, "test", "environment", ct);
        var service = new GatewayConfigurationService(db, new GatewayConfigValidator());
        var created = await service.CreateRouteAsync(environment.Id,
            new CreateManagedRouteInput("Public API", "/sub/{**remainder}", "https://upstream.example/"),
            "test", "create", ct);

        Assert.Contains(created.Route!.Features.Transforms!,
            transform => transform.TryGetValue("RequestHeaderOriginalHost", out var value) && value == "true");

        var stripped = await service.UpdateRouteBasicsAsync(environment.Id, "public-api", created.Route!.Version,
            new UpdateManagedRouteBasicsInput("Public API", "/sub/{**remainder}",
                "https://upstream.example/", PathHandling: UpstreamPathHandling.StripPrefix,
                PathPrefixToRemove: "/sub/"), "test", "strip", ct);

        var stripTransform = Assert.Single(stripped.Route!.Features.Transforms!,
            transform => transform.ContainsKey("PathRemovePrefix"));
        Assert.Equal("/sub", stripTransform["PathRemovePrefix"]);

        var preservedHost = await service.UpdateRouteBasicsAsync(environment.Id, "public-api",
            stripped.Route.Version,
            new UpdateManagedRouteBasicsInput("Public API", "/sub/{**remainder}",
                "https://upstream.example/", PreserveOriginalHost: true), "test", "preserve-host", ct);

        Assert.Contains(preservedHost.Route!.Features.Transforms!,
            transform => transform.TryGetValue("PathRemovePrefix", out var value) && value == "/sub");
        Assert.Contains(preservedHost.Route.Features.Transforms!,
            transform => transform.TryGetValue("RequestHeaderOriginalHost", out var value) && value == "true");

        var preserved = await service.UpdateRouteBasicsAsync(environment.Id, "public-api", preservedHost.Route.Version,
            new UpdateManagedRouteBasicsInput("Public API", "/sub/{**remainder}",
                "https://upstream.example/", PathHandling: UpstreamPathHandling.Preserve,
                PreserveOriginalHost: false),
            "test", "preserve", ct);

        Assert.Empty(preserved.Route!.Features.Transforms!);

        var disabled = await service.SetRouteEnabledAsync(environment.Id, "public-api", preserved.Route.Version,
            false, "test", "disable", ct);

        Assert.False(disabled.Route!.Enabled);
        Assert.Equal("Disabled route Public API", disabled.Revision.ChangeSummary);
    }

    [Fact]
    public async Task Operational_state_change_is_one_publication_and_preserves_route_configuration()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(ct);
        await using var db = new GatewayDbContext(
            new DbContextOptionsBuilder<GatewayDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync(ct);
        var environment = await new GatewayLifecycleService(db, new GatewayConfigValidator())
            .CreateEnvironmentAsync("operations", "Operations", null, "test", "environment", ct);
        var service = new GatewayConfigurationService(db, new GatewayConfigValidator());
        var created = await service.CreateRouteAsync(environment.Id,
            new CreateManagedRouteInput("Orders", "/orders", "https://orders.example/"), "test", "create", ct);

        var changed = await service.SetRouteOperationalStateAsync(environment.Id, "orders", created.Route!.Version,
            new UpdateRouteOperationalStateInput(RouteOperationalState.Draining, Message: "Deploying"), "test",
            "drain", ct);

        Assert.Equal(RouteOperationalState.Draining, changed.Route!.Operations!.State);
        Assert.Equal("Started draining route Orders", changed.Revision.ChangeSummary);
        Assert.Equal("https://orders.example/", changed.Route.Upstream.Url);
        Assert.Equal(2, await db.Revisions.CountAsync(x => x.State == RevisionState.Published, ct));
        Assert.Equal(3, await db.AuditEvents.CountAsync(ct));
    }

    [Fact]
    public async Task Shared_unavailable_response_and_defaults_publish_atomically_and_can_be_linked_to_a_route()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(ct);
        await using var db = new GatewayDbContext(
            new DbContextOptionsBuilder<GatewayDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync(ct);
        var environment = await new GatewayLifecycleService(db, new GatewayConfigValidator())
            .CreateEnvironmentAsync("shared-responses", "Shared responses", null, "test", "environment", ct);
        var service = new GatewayConfigurationService(db, new GatewayConfigValidator());
        var created = await service.CreateRouteAsync(environment.Id,
            new CreateManagedRouteInput("Orders", "/orders", "https://orders.example/"), "test", "create", ct);

        var profile = await service.SaveUnavailableResponseProfileAsync(environment.Id, created.Revision.Id,
            new SaveRouteUnavailableResponseProfileInput(null, "Planned maintenance", Message: "Back soon"),
            "test", "profile", ct);
        var saved = Assert.Single(await service.UnavailableResponseProfilesAsync(environment.Id, ct));
        var defaults = await service.UpdateOperationalDefaultsAsync(environment.Id, profile.Revision.Id,
            new UpdateRouteOperationalDefaultsInput(MaintenanceProfileId: saved.Id), "test", "defaults", ct);
        var changed = await service.SetRouteOperationalStateAsync(environment.Id, "orders", created.Route!.Version,
            new UpdateRouteOperationalStateInput(RouteOperationalState.Maintenance, saved.Id), "test", "state", ct);

        Assert.Equal(saved.Id, changed.Route!.Operations.ResponseProfileId);
        Assert.Equal(saved.Id, (await service.OperationalDefaultsAsync(environment.Id, ct)).MaintenanceProfileId);
        Assert.Equal(4, await db.Revisions.CountAsync(x => x.State == RevisionState.Published, ct));
        Assert.Equal(defaults.Revision.Id, changed.Revision.ParentRevisionId);
    }

    [Fact]
    public async Task Runtime_status_filters_sqlite_heartbeats_after_loading_the_environment_instances()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(ct);
        await using var db = new GatewayDbContext(
            new DbContextOptionsBuilder<GatewayDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync(ct);
        var environment = await new GatewayLifecycleService(db, new GatewayConfigValidator())
            .CreateEnvironmentAsync("runtime", "Runtime", null, "test", "environment", ct);
        var service = new GatewayConfigurationService(db, new GatewayConfigValidator());
        await service.CreateRouteAsync(environment.Id,
            new CreateManagedRouteInput("Orders", "/orders", "https://orders.example/"), "test", "create", ct);
        db.Instances.AddRange(
            new GatewayInstance
            {
                EnvironmentId = environment.Id, InstanceId = "live", DisplayName = "Live",
                StartedAtUtc = DateTimeOffset.UtcNow, LastHeartbeatAtUtc = DateTimeOffset.UtcNow,
                RuntimeVersion = "test", ActiveRouteRequestsJson = "{\"orders\":2}"
            },
            new GatewayInstance
            {
                EnvironmentId = environment.Id, InstanceId = "stale", DisplayName = "Stale",
                StartedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
                LastHeartbeatAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5), RuntimeVersion = "test",
                ActiveRouteRequestsJson = "{\"orders\":7}"
            });
        await db.SaveChangesAsync(ct);

        var status = Assert.Single(await new Query().GetRouteRuntimeStatuses(environment.Id, db, ct));

        Assert.Equal("orders", status.RouteId);
        Assert.Equal(2, status.ActiveRequests);
        Assert.Equal(1, status.ReportingInstances);
    }

    [Fact]
    public async Task Staged_changes_remain_editable_until_the_change_set_is_published()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(ct);
        await using var db = new GatewayDbContext(
            new DbContextOptionsBuilder<GatewayDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync(ct);
        var lifecycle = new GatewayLifecycleService(db, new GatewayConfigValidator());
        var environment = await lifecycle.CreateEnvironmentAsync("staged", "Staged", null, "test", "create", ct);
        await lifecycle.SetPublishingModeAsync(environment.Id, environment.ConcurrencyVersion,
            ConfigurationPublishingMode.Staged, "test", "mode", ct);
        var service = new GatewayConfigurationService(db, new GatewayConfigValidator());

        var created = await service.CreateRouteAsync(environment.Id,
            new CreateManagedRouteInput("Orders", "/orders", "https://orders.example/"), "test", "route", ct);
        var storedEnvironment = await db.Environments.AsNoTracking().SingleAsync(x => x.Id == environment.Id, ct);

        Assert.Equal(RevisionState.Draft, created.Revision.State);
        Assert.Null(storedEnvironment.ActiveRevisionId);
        Assert.Equal(created.Revision.Id, storedEnvironment.PendingRevisionId);
        Assert.Single(await service.RoutesAsync(environment.Id, null, ct));
        var pending = Assert.IsType<PendingConfigurationInfo>(await service.PendingAsync(environment.Id, ct));
        Assert.Equal("RouteAdded", Assert.Single(pending.Changes).Kind);

        var published = await service.PublishPendingAsync(environment.Id, pending.Version, "Initial routes", "test",
            "publish", ct);
        storedEnvironment = await db.Environments.AsNoTracking().SingleAsync(x => x.Id == environment.Id, ct);
        Assert.Equal(RevisionState.Published, published.State);
        Assert.Equal(published.Id, storedEnvironment.ActiveRevisionId);
        Assert.Null(storedEnvironment.PendingRevisionId);
    }

    [Fact]
    public async Task Discard_restores_active_configuration_and_operational_changes_stay_immediate()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(ct);
        await using var db = new GatewayDbContext(
            new DbContextOptionsBuilder<GatewayDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync(ct);
        var lifecycle = new GatewayLifecycleService(db, new GatewayConfigValidator());
        var environment =
            await lifecycle.CreateEnvironmentAsync("staged-live", "Staged live", null, "test", "create", ct);
        var service = new GatewayConfigurationService(db, new GatewayConfigValidator());
        var created = await service.CreateRouteAsync(environment.Id,
            new CreateManagedRouteInput("Orders", "/orders", "https://orders.example/"), "test", "route", ct);
        var currentEnvironment = await db.Environments.SingleAsync(x => x.Id == environment.Id, ct);
        await lifecycle.SetPublishingModeAsync(environment.Id, currentEnvironment.ConcurrencyVersion,
            ConfigurationPublishingMode.Staged, "test", "mode", ct);

        var updated = await service.UpdateRouteBasicsAsync(environment.Id, "orders", created.Route!.Version,
            new UpdateManagedRouteBasicsInput("Orders v2", "/orders", "https://orders-v2.example/"), "test",
            "edit", ct);
        var disabled = await service.SetRouteEnabledAsync(environment.Id, "orders", updated.Route!.Version, false,
            "test", "disable", ct);
        currentEnvironment = await db.Environments.AsNoTracking().SingleAsync(x => x.Id == environment.Id, ct);
        var active = await db.Revisions.AsNoTracking()
            .SingleAsync(x => x.Id == currentEnvironment.ActiveRevisionId, ct);
        var activeRoute = ManagedRouteCompiler.ToManaged(ConfigDocuments.Parse(active.ConfigJson),
            Assert.Single(ConfigDocuments.Parse(active.ConfigJson).Routes));

        Assert.False(activeRoute.Enabled);
        Assert.Equal("Orders", activeRoute.Name);
        Assert.Equal("Orders v2", disabled.Route!.Name);
        var pending = Assert.IsType<PendingConfigurationInfo>(await service.PendingAsync(environment.Id, ct));
        Assert.True(await service.DiscardPendingAsync(environment.Id, pending.Version, "test", "discard", ct));
        var restored = Assert.Single(await service.RoutesAsync(environment.Id, null, ct));
        Assert.Equal("Orders", restored.Name);
        Assert.False(restored.Enabled);
    }
}
