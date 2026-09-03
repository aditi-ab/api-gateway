using System.Text.Json;
using ApiGateway.Application;
using ApiGateway.Domain;
using ApiGateway.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ApiGateway;

public sealed class ConfigurationPoller(
    IServiceScopeFactory scopes,
    DynamicProxyConfigProvider provider,
    GatewayRuntimeState runtime,
    GatewayConfigValidator validator,
    SecretReferenceValidator secrets,
    GatewayPolicyStore policies,
    RouteRequestTracker requests,
    ConsumerCredentialStore credentials,
    LastKnownGoodStore lastKnownGood,
    IOptions<GatewayOptions> options,
    ILogger<ConfigurationPoller> logger) : BackgroundService
{
    private readonly GatewayOptions settings = options.Value;
    private TimeSpan activationBackoff = TimeSpan.FromSeconds(5);
    private bool databaseSchemaVerified;
    private DateTimeOffset lastHeartbeatUtc = DateTimeOffset.MinValue;
    private DateTimeOffset nextActivationAttemptUtc = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await TryRestoreAsync(stoppingToken);
        using var timer = new PeriodicTimer(settings.PollInterval);
        do
        {
            await PollAsync(stoppingToken);
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task PollAsync(CancellationToken ct)
    {
        Guid? environmentId = null;
        Guid? desiredRevisionId = null;
        string? desiredContentHash = null;
        var started = DateTimeOffset.UtcNow;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();
            if (!databaseSchemaVerified)
            {
                var pendingMigrations = (await db.Database.GetPendingMigrationsAsync(ct)).ToArray();
                if (pendingMigrations.Length > 0)
                    throw new InvalidOperationException(
                        "DATABASE_SCHEMA_INCOMPATIBLE: the gateway database has pending migrations.");
                databaseSchemaVerified = true;
            }

            var environment = await db.Environments.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Slug == settings.Environment && x.ArchivedAtUtc == null, ct);
            environmentId = environment?.Id;
            desiredRevisionId = environment?.ActiveRevisionId;
            if (environment is not null) await credentials.RefreshAsync(db, environment.Id, ct);
            if (environment is not null && DateTimeOffset.UtcNow - lastHeartbeatUtc >= settings.HeartbeatInterval)
                await WriteHeartbeatAsync(db, environment.Id, runtime.RevisionId, runtime.ContentHash, ct);
            if (environment?.ActiveRevisionId is null || environment.ActiveRevisionId == runtime.RevisionId) return;
            if (DateTimeOffset.UtcNow < nextActivationAttemptUtc) return;
            var revision = await db.Revisions.AsNoTracking()
                .SingleAsync(x => x.Id == environment.ActiveRevisionId && x.State == RevisionState.Published, ct);
            desiredContentHash = revision.ContentHash;
            var document = ConfigDocuments.Parse(revision.ConfigJson);
            var report = validator.Validate(document);
            if (!report.IsValid) throw new InvalidOperationException("The desired revision failed validation.");
            secrets.Validate(document);
            var mapped = YarpConfigMapper.Map(document);
            policies.Set(document);
            provider.Set(mapped.Routes, mapped.Clusters);
            runtime.Activated(revision.Id, revision.ContentHash);
            await lastKnownGood.SaveAsync(revision.Id, revision.ContentHash, revision.ConfigJson, credentials.Snapshot,
                ct);
            db.ActivationEvents.Add(new GatewayActivationEvent
            {
                EnvironmentId = environment.Id, InstanceId = settings.InstanceId, DesiredRevisionId = revision.Id,
                ContentHash = revision.ContentHash, Outcome = ActivationOutcome.Succeeded, StartedAtUtc = started,
                CompletedAtUtc = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync(ct);
            await WriteHeartbeatAsync(db, environment.Id, revision.Id, revision.ContentHash, ct, true);
            logger.LogInformation("Activated revision {RevisionNumber} ({ContentHash})", revision.Number,
                revision.ContentHash);
            GatewayTelemetry.ActivationSuccesses.Add(1);
            GatewayTelemetry.ActivationLag.Record((DateTimeOffset.UtcNow - started).TotalMilliseconds);
            activationBackoff = settings.PollInterval;
            nextActivationAttemptUtc = DateTimeOffset.MinValue;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            var code = ex.Message.Contains("DATABASE_SCHEMA_INCOMPATIBLE", StringComparison.Ordinal)
                ? "DATABASE_SCHEMA_INCOMPATIBLE"
                : ex.Message.Contains("reference", StringComparison.OrdinalIgnoreCase) &&
                  ex.Message.Contains("unavailable", StringComparison.OrdinalIgnoreCase)
                    ? "SECRET_REFERENCE_UNAVAILABLE"
                    : "ACTIVATION_FAILED";
            runtime.Failed(code);
            GatewayTelemetry.ActivationFailures.Add(1);
            GatewayTelemetry.PollFailures.Add(1);
            logger.LogWarning(ex, "Configuration polling or activation failed; the previous snapshot remains active.");
            if (desiredRevisionId is not null && desiredRevisionId != runtime.RevisionId)
            {
                nextActivationAttemptUtc = DateTimeOffset.UtcNow + activationBackoff;
                activationBackoff = TimeSpan.FromSeconds(Math.Min(60,
                    Math.Max(settings.PollInterval.TotalSeconds, activationBackoff.TotalSeconds * 2)));
            }

            if (environmentId is not null && desiredRevisionId != runtime.RevisionId)
                await TryRecordFailureAsync(environmentId.Value, desiredRevisionId, desiredContentHash, code, started,
                    ct);
        }
    }

    private async Task TryRecordFailureAsync(Guid environmentId, Guid? revisionId, string? contentHash, string code,
        DateTimeOffset started, CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();
            db.ActivationEvents.Add(new GatewayActivationEvent
            {
                EnvironmentId = environmentId, InstanceId = settings.InstanceId, DesiredRevisionId = revisionId,
                ContentHash = contentHash, Outcome = ActivationOutcome.Failed, ErrorCode = code,
                ErrorMessage = "The desired revision could not be activated. See the gateway logs for details.",
                StartedAtUtc = started, CompletedAtUtc = DateTimeOffset.UtcNow
            });
            if (await db.Instances.SingleOrDefaultAsync(
                    x => x.EnvironmentId == environmentId && x.InstanceId == settings.InstanceId, ct) is { } instance)
            {
                instance.LastActivationStatus = "Failed";
                instance.LastActivationAtUtc = DateTimeOffset.UtcNow;
                instance.LastActivationErrorCode = code;
            }

            await db.SaveChangesAsync(ct);
        }
        catch (Exception persistenceError)
        {
            logger.LogDebug(persistenceError, "Unable to persist the activation failure diagnostic.");
        }
    }

    private async Task TryRestoreAsync(CancellationToken ct)
    {
        var bundle = await lastKnownGood.LoadAsync(ct);
        if (bundle is null) return;
        try
        {
            var document = ConfigDocuments.Parse(bundle.ConfigJson);
            if (!validator.Validate(document).IsValid) return;
            secrets.Validate(document);
            var mapped = YarpConfigMapper.Map(document);
            credentials.Set(bundle.Credentials ?? []);
            policies.Set(document);
            provider.Set(mapped.Routes, mapped.Clusters);
            runtime.Activated(bundle.RevisionId, bundle.ContentHash);
            logger.LogInformation("Restored last-known-good revision {RevisionId}", bundle.RevisionId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "The last-known-good bundle could not be activated.");
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();
            var environment =
                await db.Environments.SingleOrDefaultAsync(x => x.Slug == settings.Environment, cancellationToken);
            if (environment is not null &&
                await db.Instances.SingleOrDefaultAsync(
                        x => x.EnvironmentId == environment.Id && x.InstanceId == settings.InstanceId,
                        cancellationToken) is
                    { } instance)
            {
                instance.StoppedAtUtc = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unable to record graceful gateway shutdown.");
        }

        await base.StopAsync(cancellationToken);
    }

    private async Task WriteHeartbeatAsync(GatewayDbContext db, Guid environmentId, Guid? revisionId,
        string? contentHash, CancellationToken ct, bool activationSucceeded = false)
    {
        var instance =
            await db.Instances.SingleOrDefaultAsync(
                x => x.EnvironmentId == environmentId && x.InstanceId == settings.InstanceId, ct);
        if (instance is null)
        {
            instance = new GatewayInstance
            {
                EnvironmentId = environmentId, InstanceId = settings.InstanceId, DisplayName = settings.DisplayName,
                StartedAtUtc = DateTimeOffset.UtcNow, LastHeartbeatAtUtc = DateTimeOffset.UtcNow,
                RuntimeVersion = typeof(Program).Assembly.GetName().Version?.ToString() ?? "development"
            };
            db.Instances.Add(instance);
        }

        instance.LastHeartbeatAtUtc = DateTimeOffset.UtcNow;
        instance.ActivatedRevisionId = revisionId;
        instance.ActivatedContentHash = contentHash;
        instance.ActiveRouteRequestsJson = JsonSerializer.Serialize(requests.Snapshot());
        if (activationSucceeded)
        {
            instance.LastActivationStatus = "Succeeded";
            instance.LastActivationAtUtc = DateTimeOffset.UtcNow;
            instance.LastActivationErrorCode = null;
        }

        instance.StoppedAtUtc = null;
        await db.SaveChangesAsync(ct);
        lastHeartbeatUtc = DateTimeOffset.UtcNow;
    }
}