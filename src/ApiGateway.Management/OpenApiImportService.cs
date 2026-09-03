using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ApiGateway.Application;
using ApiGateway.Domain;
using ApiGateway.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using KeyNotFoundException = System.Collections.Generic.KeyNotFoundException;

namespace ApiGateway.Management;

public sealed record OpenApiRouteConflict(string RouteId, string ExistingPath, string ProposedPath);

public sealed record OpenApiConflictResolutionInput(string RouteId, string Action);

public sealed record OpenApiPreview(
    string Token,
    IReadOnlyList<GatewayRoute> Routes,
    IReadOnlyList<OpenApiRouteConflict> Conflicts,
    IReadOnlyList<ValidationIssue> Issues);

public sealed record ManagedOpenApiRoutePreview(
    string Id,
    string Path,
    IReadOnlyList<string> Methods,
    bool Conflicts);

public sealed record ManagedOpenApiPreview(
    string Token,
    IReadOnlyList<ManagedOpenApiRoutePreview> Routes,
    IReadOnlyList<ValidationIssue> Issues);

public sealed class OpenApiImportService(
    IDataProtectionProvider protection,
    GatewayDbContext db,
    GatewayLifecycleService lifecycle,
    GatewayConfigurationService configuration)
{
    private readonly IDataProtector managedProtector = protection.CreateProtector("ApiGateway.OpenApiPreview.v2");
    private readonly IDataProtector protector = protection.CreateProtector("ApiGateway.OpenApiPreview.v1");

    public async Task<ManagedOpenApiPreview> PreviewManagedAsync(Guid environmentId,
        Guid expectedConfigurationVersion, string source, string upstreamUrl, string? prefix, CancellationToken ct)
    {
        var environment = await db.Environments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == environmentId, ct) ??
                          throw new KeyNotFoundException("Environment not found.");
        if (environment.ActiveRevisionId != expectedConfigurationVersion)
            throw new GatewayConfigurationConflictException(environment.ActiveRevisionId);
        if (!Uri.TryCreate(upstreamUrl, UriKind.Absolute, out var upstream) ||
            upstream.Scheme is not ("http" or "https"))
            throw new ArgumentException("Upstream URL must be an absolute HTTP or HTTPS URL.");
        var current = environment.ActiveRevisionId is null
            ? new GatewayConfigDocument()
            : ConfigDocuments.Parse((await db.Revisions.AsNoTracking()
                .SingleAsync(x => x.Id == environment.ActiveRevisionId, ct)).ConfigJson);
        var generated = OpenApiRouteGenerator.Generate(source, "preview", prefix);
        var routes = generated.Routes.Select(x => new ManagedOpenApiRoutePreview(x.Id, x.Match.Path, x.Match.Methods,
            current.Routes.Any(existing => existing.Id.Equals(x.Id, StringComparison.OrdinalIgnoreCase)))).ToArray();
        var payload = new ManagedPreviewPayload(environmentId, expectedConfigurationVersion, upstreamUrl,
            DateTimeOffset.UtcNow.AddMinutes(10), routes);
        return new ManagedOpenApiPreview(managedProtector.Protect(
            JsonSerializer.Serialize(payload, GatewayJson.Options)), routes, generated.Issues);
    }

    public async Task<ConfigurationChangeResult> ApplyManagedAsync(string token, IReadOnlyList<string> routeIds,
        string actor, string correlationId, CancellationToken ct)
    {
        ManagedPreviewPayload payload;
        try
        {
            payload = JsonSerializer.Deserialize<ManagedPreviewPayload>(managedProtector.Unprotect(token),
                GatewayJson.Options) ?? throw new InvalidOperationException();
        }
        catch
        {
            throw new InvalidOperationException("The OpenAPI preview token is invalid.");
        }

        if (payload.ExpiresAtUtc <= DateTimeOffset.UtcNow)
            throw new InvalidOperationException("The OpenAPI preview token expired.");
        var selected = routeIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var routes = payload.Routes.Where(x => selected.Contains(x.Id) && !x.Conflicts)
            .Select(x => new ImportedRouteDefinition(x.Id, x.Path, x.Methods)).ToArray();
        if (routes.Length == 0) throw new ArgumentException("Select at least one non-conflicting route.");
        return await configuration.ImportRoutesAsync(payload.EnvironmentId, payload.ExpectedConfigurationVersion,
            routes, payload.UpstreamUrl, actor, correlationId, ct);
    }

    public async Task<OpenApiPreview> PreviewAsync(Guid draftId, Guid expectedVersion, string source, string clusterId,
        string? prefix, CancellationToken ct)
    {
        var draft = await db.Revisions.AsNoTracking()
            .SingleAsync(x => x.Id == draftId && x.State == RevisionState.Draft, ct);
        if (draft.ConcurrencyVersion != expectedVersion) throw new GatewayConflictException(draft.ConcurrencyVersion);
        var document = ConfigDocuments.Parse(draft.ConfigJson);
        var generated = OpenApiRouteGenerator.Generate(source, clusterId, prefix);
        var existing = document.Routes.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
        var conflicts = generated.Routes.Where(x => existing.ContainsKey(x.Id))
            .Select(x => new OpenApiRouteConflict(x.Id, existing[x.Id].Match.Path, x.Match.Path)).ToArray();
        var sourceHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
        var payload = new PreviewPayload(draftId, expectedVersion, sourceHash, DateTimeOffset.UtcNow.AddMinutes(10),
            generated.Routes, conflicts.Select(x => x.RouteId).ToArray());
        return new OpenApiPreview(protector.Protect(JsonSerializer.Serialize(payload, GatewayJson.Options)),
            generated.Routes, conflicts, generated.Issues);
    }

    public async Task<ConfigRevision> ApplyAsync(string token, Guid expectedVersion,
        IReadOnlyList<OpenApiConflictResolutionInput> resolutions, string actor, string correlationId,
        CancellationToken ct)
    {
        PreviewPayload payload;
        try
        {
            payload = JsonSerializer.Deserialize<PreviewPayload>(protector.Unprotect(token), GatewayJson.Options) ??
                      throw new InvalidOperationException();
        }
        catch
        {
            throw new InvalidOperationException("The OpenAPI preview token is invalid.");
        }

        if (payload.ExpiresAtUtc <= DateTimeOffset.UtcNow)
            throw new InvalidOperationException("The OpenAPI preview token expired.");
        var draft = await db.Revisions.AsNoTracking().SingleAsync(x => x.Id == payload.DraftId, ct);
        if (expectedVersion != payload.ExpectedVersion || draft.ConcurrencyVersion != expectedVersion)
            throw new GatewayConflictException(draft.ConcurrencyVersion);
        var choices = resolutions.ToDictionary(x => x.RouteId, x => x.Action, StringComparer.OrdinalIgnoreCase);
        foreach (var conflict in payload.ConflictRouteIds)
            if (!choices.TryGetValue(conflict, out var action) || action is not ("replace" or "skip"))
                throw new ArgumentException($"Conflict '{conflict}' requires a replace or skip resolution.");
        var selected = payload.Routes.Where(x =>
                !payload.ConflictRouteIds.Contains(x.Id, StringComparer.OrdinalIgnoreCase) ||
                choices[x.Id] == "replace")
            .ToArray();
        var replaced = selected.Select(x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var document = ConfigDocuments.Parse(draft.ConfigJson);
        var merged = document with
        {
            Routes = document.Routes.Where(x => !replaced.Contains(x.Id)).Concat(selected).ToArray()
        };
        return await lifecycle.SetDraftContentAsync(payload.DraftId, payload.ExpectedVersion,
            ConfigDocuments.Serialize(merged), actor, correlationId, ct);
    }

    private sealed record PreviewPayload(
        Guid DraftId,
        Guid ExpectedVersion,
        string SourceHash,
        DateTimeOffset ExpiresAtUtc,
        IReadOnlyList<GatewayRoute> Routes,
        IReadOnlyList<string> ConflictRouteIds);

    private sealed record ManagedPreviewPayload(
        Guid EnvironmentId,
        Guid ExpectedConfigurationVersion,
        string UpstreamUrl,
        DateTimeOffset ExpiresAtUtc,
        IReadOnlyList<ManagedOpenApiRoutePreview> Routes);
}