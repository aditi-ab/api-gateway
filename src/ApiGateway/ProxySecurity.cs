using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.RateLimiting;
using ApiGateway.Application;
using ApiGateway.Domain;
using ApiGateway.Persistence;
using Json.Schema;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Yarp.ReverseProxy.Configuration;

namespace ApiGateway;

public sealed record ConsumerVerifier(
    Guid Id,
    string Prefix,
    byte[] Hash,
    IReadOnlySet<Guid> EnvironmentIds,
    IReadOnlySet<string> RouteIds,
    IReadOnlyDictionary<string, string> Claims,
    IReadOnlyList<string>? AllowedCidrs,
    DateTimeOffset? ExpiresAtUtc);

public sealed class ConsumerCredentialStore
{
    private volatile IReadOnlyDictionary<string, ConsumerVerifier> current = new Dictionary<string, ConsumerVerifier>();
    public IReadOnlyList<ConsumerVerifier> Snapshot => current.Values.ToArray();

    public void Set(IReadOnlyList<ConsumerVerifier> verifiers)
    {
        current = verifiers.ToDictionary(x => x.Prefix, StringComparer.Ordinal);
    }

    public async Task RefreshAsync(GatewayDbContext db, Guid environmentId, CancellationToken ct)
    {
        var records = await db.ConsumerApiKeys.AsNoTracking().Where(x => x.RevokedAtUtc == null).ToListAsync(ct);
        current = records.Where(x => x.ExpiresAtUtc is null || x.ExpiresAtUtc > DateTimeOffset.UtcNow).Select(x =>
                new ConsumerVerifier(x.Id, x.KeyPrefix, x.KeyHash,
                    JsonSerializer.Deserialize<Guid[]>(x.EnvironmentIdsJson)?.ToHashSet() ?? [],
                    JsonSerializer.Deserialize<string[]>(x.RouteIdsJson)?.ToHashSet(StringComparer.OrdinalIgnoreCase) ??
                    new HashSet<string>(),
                    JsonSerializer.Deserialize<Dictionary<string, string>>(x.ClaimsJson) ??
                    new Dictionary<string, string>(), JsonSerializer.Deserialize<string[]>(x.AllowedCidrsJson),
                    x.ExpiresAtUtc)).Where(x => x.EnvironmentIds.Count == 0 || x.EnvironmentIds.Contains(environmentId))
            .ToDictionary(x => x.Prefix, StringComparer.Ordinal);
    }

    public ConsumerVerifier? Authenticate(string secret, IPAddress? remoteAddress)
    {
        if (secret.Length < 8 || !current.TryGetValue(secret[..8], out var verifier)) return null;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
        return (verifier.ExpiresAtUtc is null || verifier.ExpiresAtUtc > DateTimeOffset.UtcNow) &&
               CryptographicOperations.FixedTimeEquals(hash, verifier.Hash) &&
               CidrMatcher.Allows(remoteAddress, verifier.AllowedCidrs)
            ? verifier
            : null;
    }
}

public sealed class GatewayPolicyStore
{
    private volatile GatewayConfigDocument document = new();
    public GatewayConfigDocument Current => document;
    public string Version { get; private set; } = "empty";

    public void Set(GatewayConfigDocument value)
    {
        document = GatewayFeatureSwitches.Apply(value);
        Version = ConfigDocuments.Hash(ConfigDocuments.Serialize(document));
    }
}

public sealed class ProxyPolicyMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, GatewayPolicyStore policies, ConsumerCredentialStore credentials,
        DynamicJwtValidator jwt, DynamicRateLimiter rateLimiter, MirrorDispatcher mirrors, ApiKeyUsageQueue usage,
        RouteResponseCache responseCache, RouteRequestTracker requestTracker)
    {
        var feature = context.GetReverseProxyFeature();
        var route = feature.Route.Config;
        DynamicRateLimiter.Lease? rateLease = null;
        IDisposable? requestLease = null;
        var started = Stopwatch.GetTimestamp();
        using var activity = GatewayTelemetry.Activities.StartActivity("proxy.request");
        activity?.SetTag("gateway.route", route.RouteId);
        activity?.SetTag("gateway.cluster", route.ClusterId);
        try
        {
            var configuredRoute = policies.Current.Routes
                .FirstOrDefault(x => x.Id.Equals(route.RouteId, StringComparison.OrdinalIgnoreCase));
            if (configuredRoute is null)
            {
                context.Response.StatusCode = 503;
                return;
            }

            if (configuredRoute.Inbound.Scheme == InboundScheme.HttpOnly && context.Request.IsHttps)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            if (configuredRoute.Inbound.Scheme == InboundScheme.HttpsRedirect && !context.Request.IsHttps)
            {
                context.Response.StatusCode = StatusCodes.Status308PermanentRedirect;
                context.Response.Headers.Location =
                    $"https://{context.Request.Host}{context.Request.PathBase}{context.Request.Path}{context.Request.QueryString}";
                return;
            }

            if (!configuredRoute.Inbound.WebSocketsAllowed && context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            var operationalState = route.Metadata?.GetValueOrDefault("ApiGateway.OperationalState");
            if (Enum.TryParse<RouteOperationalState>(operationalState, true, out var state) &&
                state != RouteOperationalState.Online)
            {
                var responseJson = route.Metadata?.GetValueOrDefault("ApiGateway.UnavailableResponse");
                var response = string.IsNullOrWhiteSpace(responseJson)
                    ? null
                    : JsonSerializer.Deserialize<RouteUnavailableResponse>(responseJson, GatewayJson.Options);
                await ApplyOperationalStateAsync(context,
                    configuredRoute with { Operations = new RouteOperationalPolicy(state, response) }, next);
                return;
            }

            requestLease = requestTracker.Enter(route.RouteId);

            if (!ApplyAccess(context, configuredRoute)) return;
            if (!await ValidateRequestAsync(context, configuredRoute)) return;
            if (await responseCache.TryServeAsync(context, route.RouteId, configuredRoute, policies.Version)) return;
            await using var cacheCapture = responseCache.BeginCapture(context, route.RouteId, configuredRoute,
                policies.Version);
            if (await ApplyCorsAsync(context, route, policies.Current)) return;
            var policyName = route.Metadata?.GetValueOrDefault("ApiGateway.Authorization") ?? "Anonymous";
            if (policyName != "Anonymous")
            {
                var authentication = await AuthenticateAsync(policyName, route.RouteId, context, policies.Current,
                    credentials, jwt, usage, []);
                if (authentication.Status != 200)
                {
                    GatewayTelemetry.AuthorizationRejections.Add(1);
                    context.Response.StatusCode = authentication.Status;
                    return;
                }

                context.User = authentication.Principal!;
            }

            var rateName = route.Metadata?.GetValueOrDefault("ApiGateway.RateLimit");
            if (!string.IsNullOrWhiteSpace(rateName) &&
                policies.Current.Policies.RateLimits.TryGetValue(rateName, out var rate))
            {
                rateLease = await rateLimiter.AcquireAsync(route.RouteId, rateName, rate, context);
                if (!rateLease.Acquired)
                {
                    GatewayTelemetry.RateLimitRejections.Add(1);
                    context.Response.StatusCode = 429;
                    context.Response.Headers.RetryAfter =
                        Math.Max(1, (int)rateLease.RetryAfter.TotalSeconds).ToString();
                    return;
                }
            }

            var mirror = policies.Current.Routes
                .FirstOrDefault(x => x.Id.Equals(route.RouteId, StringComparison.OrdinalIgnoreCase))?.Mirror;
            if (mirror is not null && await MirrorRequestFactory.CreateAsync(context, policies.Current, mirror) is
                    { } work) _ = mirrors.TryEnqueue(work);
            await next(context);
            await cacheCapture.CompleteAsync(context.RequestAborted);
        }
        finally
        {
            rateLease?.Dispose();
            requestLease?.Dispose();
            var tags = new TagList
            {
                { "route", route.RouteId }, { "cluster", route.ClusterId ?? "none" },
                { "response_class", $"{Math.Clamp(context.Response.StatusCode / 100, 0, 9)}xx" },
                { "outcome", context.Response.StatusCode < 500 ? "success" : "failure" }
            };
            GatewayTelemetry.ProxyRequests.Add(1, tags);
            GatewayTelemetry.ProxyDuration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds, tags);
        }
    }

    public static async Task ApplyOperationalStateAsync(HttpContext context, GatewayRoute route,
        RequestDelegate next)
    {
        var state = route.Operations.State;
        var response = route.Operations.Response ?? new RouteUnavailableResponse();
        if (response.RetryAfter is { } retryAfter)
            context.Response.Headers.RetryAfter = Math.Max(1, (long)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
        context.Response.Headers.CacheControl = "no-store";
        GatewayTelemetry.OperationalStateResponses.Add(1,
            new KeyValuePair<string, object?>("route", route.Id),
            new KeyValuePair<string, object?>("state", state.ToString().ToLowerInvariant()));

        if (!string.IsNullOrWhiteSpace(response.UpstreamUrl))
        {
            await next(context);
            return;
        }

        var title = response.Title ?? state switch
        {
            RouteOperationalState.Draining => "Service is draining",
            RouteOperationalState.Maintenance => "Service under maintenance",
            _ => "Service offline"
        };
        var message = response.Message ?? state switch
        {
            RouteOperationalState.Draining =>
                "Existing requests are finishing. New requests are temporarily unavailable.",
            RouteOperationalState.Maintenance =>
                "This service is temporarily unavailable while maintenance is in progress.",
            _ => "This service is currently unavailable."
        };
        context.Response.StatusCode = response.StatusCode;
        if (HttpMethods.IsHead(context.Request.Method)) return;
        if (context.Request.GetTypedHeaders().Accept?.Any(x =>
                x.MediaType.Value?.Contains("html", StringComparison.OrdinalIgnoreCase) == true) == true)
        {
            context.Response.ContentType = "text/html; charset=utf-8";
            var encodedTitle = HtmlEncoder.Default.Encode(title);
            var encodedMessage = HtmlEncoder.Default.Encode(message);
            await context.Response.WriteAsync(
                $"<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>{encodedTitle}</title></head><body><main><h1>{encodedTitle}</h1><p>{encodedMessage}</p></main></body></html>",
                context.RequestAborted);
            return;
        }

        await context.Response.WriteAsJsonAsync(new
        {
            status = response.StatusCode,
            code = $"ROUTE_{state.ToString().ToUpperInvariant()}",
            message
        }, context.RequestAborted);
    }

    private static bool ApplyAccess(HttpContext context, GatewayRoute route)
    {
        if (route.Access is null) return true;
        var address = context.Connection.RemoteIpAddress;
        if ((route.Access.DeniedCidrs?.Count > 0 && CidrMatcher.Allows(address, route.Access.DeniedCidrs)) ||
            (route.Access.AllowedCidrs?.Count > 0 && !CidrMatcher.Allows(address, route.Access.AllowedCidrs)))
        {
            GatewayTelemetry.AccessRejections.Add(1);
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return false;
        }

        if (route.Access.MaximumRequestBodyBytes is { } maximum)
        {
            var feature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (feature is { IsReadOnly: false }) feature.MaxRequestBodySize = maximum;
            if (context.Request.ContentLength > maximum)
            {
                GatewayTelemetry.RequestSizeRejections.Add(1);
                context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                return false;
            }
        }

        return true;
    }

    private static async Task<bool> ValidateRequestAsync(HttpContext context, GatewayRoute route)
    {
        var policy = route.RequestValidation;
        if (policy is null || context.Request.ContentLength == 0) return true;
        if (context.Request.ContentLength > policy.MaximumBodyBytes)
        {
            GatewayTelemetry.RequestSizeRejections.Add(1);
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            return false;
        }

        var bodySizeFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (bodySizeFeature is { IsReadOnly: false })
        {
            var configuredMaximum = bodySizeFeature.MaxRequestBodySize;
            bodySizeFeature.MaxRequestBodySize = configuredMaximum is null
                ? policy.MaximumBodyBytes
                : Math.Min(configuredMaximum.Value, policy.MaximumBodyBytes);
        }

        if (!(context.Request.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) ?? false))
        {
            context.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
            return false;
        }

        context.Request.EnableBuffering((int)Math.Min(policy.MaximumBodyBytes, int.MaxValue));
        try
        {
            using var request = await JsonDocument.ParseAsync(context.Request.Body,
                cancellationToken: context.RequestAborted);
            var schema = JsonSchema.FromText(policy.JsonSchema);
            var result = schema.Evaluate(request.RootElement,
                new EvaluationOptions { OutputFormat = OutputFormat.List });
            context.Request.Body.Position = 0;
            if (result.IsValid) return true;
        }
        catch (JsonException)
        {
            if (context.Request.Body.CanSeek) context.Request.Body.Position = 0;
        }

        GatewayTelemetry.RequestValidationRejections.Add(1);
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(new { code = "REQUEST_VALIDATION_FAILED" }, context.RequestAborted);
        return false;
    }

    private static async Task<PolicyResult> AuthenticateAsync(string policyName, string routeId, HttpContext context,
        GatewayConfigDocument document, ConsumerCredentialStore credentials, DynamicJwtValidator jwt,
        ApiKeyUsageQueue usage, HashSet<string> stack)
    {
        if (!stack.Add(policyName) || !document.Policies.Authorization.TryGetValue(policyName, out var policy))
            return new PolicyResult(503, null);
        try
        {
            if (policy.Type.Equals("apiKey", StringComparison.OrdinalIgnoreCase))
            {
                if (!context.Request.Headers.TryGetValue("X-Api-Key", out var header) || header.Count != 1)
                    return new PolicyResult(401, null);
                var verifier = credentials.Authenticate(header.ToString(), context.Connection.RemoteIpAddress);
                if (verifier is null) return new PolicyResult(401, null);
                if (verifier.RouteIds.Count > 0 && !verifier.RouteIds.Contains(routeId))
                    return new PolicyResult(403, null);
                if (policy.RequiredScopes?.Any(scope =>
                        !verifier.Claims.TryGetValue("scope", out var value) || !value
                            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                            .Contains(scope, StringComparer.Ordinal)) == true) return new PolicyResult(403, null);
                usage.TryRecord(verifier.Id, false);
                return new PolicyResult(200,
                    new ClaimsPrincipal(
                        new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, verifier.Id.ToString())], "ApiKey")));
            }

            if (policy.Type.Equals("jwt", StringComparison.OrdinalIgnoreCase))
            {
                var authorization = context.Request.Headers.Authorization.ToString();
                var principal = authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                    ? await jwt.ValidateAsync(authorization[7..], policy, context.RequestAborted)
                    : null;
                return principal is null ? new PolicyResult(401, null) : new PolicyResult(200, principal);
            }

            if (policy.Type.Equals("anyOf", StringComparison.OrdinalIgnoreCase))
            {
                var failures = new List<int>();
                foreach (var child in policy.Policies ?? [])
                {
                    var result = await AuthenticateAsync(child, routeId, context, document, credentials, jwt, usage,
                        new HashSet<string>(stack, StringComparer.OrdinalIgnoreCase));
                    if (result.Status == 200) return result;
                    failures.Add(result.Status);
                }

                return new PolicyResult(failures.Contains(403) ? 403 : failures.Contains(503) ? 503 : 401, null);
            }

            if (policy.Type.Equals("allOf", StringComparison.OrdinalIgnoreCase))
            {
                var identities = new List<ClaimsIdentity>();
                foreach (var child in policy.Policies ?? [])
                {
                    var result = await AuthenticateAsync(child, routeId, context, document, credentials, jwt, usage,
                        new HashSet<string>(stack, StringComparer.OrdinalIgnoreCase));
                    if (result.Status != 200) return result;
                    identities.AddRange(result.Principal!.Identities);
                }

                return identities.Count == 0
                    ? new PolicyResult(503, null)
                    : new PolicyResult(200, new ClaimsPrincipal(identities));
            }

            return new PolicyResult(503, null);
        }
        finally
        {
            stack.Remove(policyName);
        }
    }

    private static async Task<bool> ApplyCorsAsync(HttpContext context, RouteConfig route,
        GatewayConfigDocument document)
    {
        var policyName = route.Metadata?.GetValueOrDefault("ApiGateway.Cors");
        if (string.IsNullOrWhiteSpace(policyName) ||
            !document.Policies.Cors.TryGetValue(policyName, out var policy)) return false;
        var origin = context.Request.Headers.Origin.ToString();
        if (string.IsNullOrWhiteSpace(origin)) return false;
        var allowed = policy.Origins.Contains("*", StringComparer.Ordinal) ||
                      policy.Origins.Contains(origin, StringComparer.OrdinalIgnoreCase);
        if (!allowed)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return true;
        }

        context.Response.Headers.AccessControlAllowOrigin = policy.AllowCredentials ? origin :
            policy.Origins.Contains("*", StringComparer.Ordinal) ? "*" : origin;
        context.Response.Headers.Vary = "Origin";
        if (policy.AllowCredentials) context.Response.Headers.AccessControlAllowCredentials = "true";
        if (HttpMethods.IsOptions(context.Request.Method) &&
            context.Request.Headers.ContainsKey("Access-Control-Request-Method"))
        {
            var requestedMethod = context.Request.Headers.AccessControlRequestMethod.ToString();
            if (!policy.Methods.Contains("*", StringComparer.Ordinal) &&
                !policy.Methods.Contains(requestedMethod, StringComparer.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return true;
            }

            context.Response.Headers.AccessControlAllowMethods = string.Join(", ", policy.Methods);
            var requestedHeaders = context.Request.Headers.AccessControlRequestHeaders.ToString().Split(',',
                StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (!policy.Headers.Contains("*", StringComparer.Ordinal) &&
                requestedHeaders.Any(x => !policy.Headers.Contains(x, StringComparer.OrdinalIgnoreCase)))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return true;
            }

            context.Response.Headers.AccessControlAllowHeaders = string.Join(", ", policy.Headers);
            if (policy.PreflightMaxAge is { } age)
                context.Response.Headers.AccessControlMaxAge = Math.Max(0, (long)age.TotalSeconds).ToString();
            context.Response.StatusCode = StatusCodes.Status204NoContent;
            return true;
        }

        if (policy.ExposedHeaders?.Count > 0)
            context.Response.Headers.AccessControlExposeHeaders = string.Join(", ", policy.ExposedHeaders);
        await Task.CompletedTask;
        return false;
    }

    private sealed record PolicyResult(int Status, ClaimsPrincipal? Principal);
}

public sealed class DynamicRateLimiter
{
    private readonly ConcurrentDictionary<string, LimiterEntry> limiters = new(StringComparer.Ordinal);

    private static string Partition(RateLimitPolicy policy, HttpContext context)
    {
        return policy.PartitionBy.ToLowerInvariant() switch
        {
            "clientip" => context.Connection.RemoteIpAddress?.ToString(),
            "consumerkey" => context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
            "jwtsubject" => context.User.FindFirst("sub")?.Value,
            "header" => context.Request.Headers[policy.PartitionName ?? string.Empty].ToString(),
            "route" => context.GetReverseProxyFeature().Route.Config.RouteId, _ => "global"
        } ?? "anonymous";
    }

    private RateLimiter GetLimiter(string routeId, string policyName, RateLimitPolicy policy, HttpContext context)
    {
        var key = $"{routeId}:{policyName}:{Partition(policy, context)}";
        var signature = JsonSerializer.Serialize(policy, GatewayJson.Options);
        var entry = limiters.AddOrUpdate(key, _ => new LimiterEntry(signature, Create(policy)), (_, current) =>
        {
            if (current.Signature == signature) return current;
            current.Limiter.Dispose();
            return new LimiterEntry(signature, Create(policy));
        });
        return entry.Limiter;
    }

    private static RateLimiter Create(RateLimitPolicy policy)
    {
        var order = policy.QueueOrder == "newestFirst"
            ? QueueProcessingOrder.NewestFirst
            : QueueProcessingOrder.OldestFirst;
        return policy.Type.ToLowerInvariant() switch
        {
            "slidingwindow" => new SlidingWindowRateLimiter(new SlidingWindowRateLimiterOptions
            {
                PermitLimit = policy.PermitLimit, Window = policy.Window!.Value,
                SegmentsPerWindow = policy.SegmentsPerWindow, QueueLimit = policy.QueueLimit,
                QueueProcessingOrder = order, AutoReplenishment = true
            }),
            "tokenbucket" => new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
            {
                TokenLimit = policy.PermitLimit, TokensPerPeriod = policy.TokensPerPeriod ?? policy.PermitLimit,
                ReplenishmentPeriod = policy.Window!.Value, QueueLimit = policy.QueueLimit,
                QueueProcessingOrder = order, AutoReplenishment = true
            }),
            "concurrency" => new ConcurrencyLimiter(new ConcurrencyLimiterOptions
                { PermitLimit = policy.PermitLimit, QueueLimit = policy.QueueLimit, QueueProcessingOrder = order }),
            _ => new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
            {
                PermitLimit = policy.PermitLimit, Window = policy.Window!.Value, QueueLimit = policy.QueueLimit,
                QueueProcessingOrder = order, AutoReplenishment = true
            })
        };
    }

    public bool TryAcquire(string routeId, string policyName, RateLimitPolicy policy, HttpContext context,
        out TimeSpan retryAfter)
    {
        using var lease = GetLimiter(routeId, policyName, policy, context).AttemptAcquire();
        retryAfter = lease.TryGetMetadata(MetadataName.RetryAfter, out var retry)
            ? retry
            : policy.Window ?? TimeSpan.FromSeconds(1);
        return lease.IsAcquired;
    }

    public async Task<Lease> AcquireAsync(string routeId, string policyName, RateLimitPolicy policy,
        HttpContext context)
    {
        var limiter = GetLimiter(routeId, policyName, policy, context);
        var immediate = limiter.AttemptAcquire();
        if (immediate.IsAcquired) return new Lease(true, TimeSpan.Zero, false, immediate);
        var immediateRetry = immediate.TryGetMetadata(MetadataName.RetryAfter, out var retry)
            ? retry
            : policy.Window ?? TimeSpan.FromSeconds(1);
        immediate.Dispose();
        if (policy.QueueLimit <= 0) return new Lease(false, immediateRetry, false);
        var queued = await limiter.AcquireAsync(1, context.RequestAborted);
        var queuedRetry = queued.TryGetMetadata(MetadataName.RetryAfter, out retry)
            ? retry
            : policy.Window ?? TimeSpan.FromSeconds(1);
        return new Lease(queued.IsAcquired, queuedRetry, true, queued);
    }

    private sealed record LimiterEntry(string Signature, RateLimiter Limiter);

    public sealed class Lease(bool acquired, TimeSpan retryAfter, bool queued, RateLimitLease? inner = null)
        : IDisposable
    {
        public bool Acquired { get; } = acquired;
        public TimeSpan RetryAfter { get; } = retryAfter;
        public bool Queued { get; } = queued;

        public void Dispose()
        {
            inner?.Dispose();
        }
    }
}