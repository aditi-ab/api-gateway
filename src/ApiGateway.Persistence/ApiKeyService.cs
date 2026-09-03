using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ApiGateway.Domain;
using Microsoft.EntityFrameworkCore;

namespace ApiGateway.Persistence;

public sealed record CreatedApiKey(Guid Id, string Prefix, string Secret);

public sealed class ApiKeyService(GatewayDbContext db)
{
    private static readonly HashSet<string> ManagementScopes = new(StringComparer.Ordinal)
    {
        "config:read", "config:manage", "config:write", "config:publish", "instances:read",
        "credentials:read", "credentials:write", "audit:read", "system:admin"
    };

    public async Task<CreatedApiKey> CreateManagementAsync(string name, IReadOnlyList<string> scopes,
        IReadOnlyList<string>? allowedCidrs, DateTimeOffset? expiresAtUtc, string actor, string correlationId,
        CancellationToken ct)
    {
        var normalizedScopes = NormalizeManagementScopes(scopes);
        var normalizedCidrs = CidrMatcher.Normalize(allowedCidrs);
        var (secret, prefix, hash) = Generate();
        var key = new ManagementApiKey
        {
            Name = name.Trim(), KeyPrefix = prefix, KeyHash = hash,
            ScopesJson = JsonSerializer.Serialize(normalizedScopes),
            AllowedCidrsJson = JsonSerializer.Serialize(normalizedCidrs), ExpiresAtUtc = expiresAtUtc, CreatedBy = actor
        };
        db.ManagementApiKeys.Add(key);
        Audit("ManagementApiKeyCreated", nameof(ManagementApiKey), key.Id, actor, correlationId,
            new { key.Name, key.KeyPrefix, scopes, allowedCidrs = normalizedCidrs, expiresAtUtc });
        await db.SaveChangesAsync(ct);
        return new CreatedApiKey(key.Id, prefix, secret);
    }

    public async Task<CreatedApiKey> CreateConsumerAsync(string name, IReadOnlyList<Guid> environmentIds,
        IReadOnlyList<string> routeIds, IReadOnlyDictionary<string, string> claims, IReadOnlyList<string>? allowedCidrs,
        DateTimeOffset? expiresAtUtc, string actor, string correlationId, CancellationToken ct)
    {
        var normalizedCidrs = CidrMatcher.Normalize(allowedCidrs);
        var (secret, prefix, hash) = Generate();
        var key = new ConsumerApiKey
        {
            Name = name.Trim(), KeyPrefix = prefix, KeyHash = hash,
            EnvironmentIdsJson = JsonSerializer.Serialize(environmentIds.Distinct()),
            RouteIdsJson = JsonSerializer.Serialize(routeIds.Distinct(StringComparer.OrdinalIgnoreCase)),
            ClaimsJson = JsonSerializer.Serialize(claims), AllowedCidrsJson = JsonSerializer.Serialize(normalizedCidrs),
            ExpiresAtUtc = expiresAtUtc, CreatedBy = actor
        };
        db.ConsumerApiKeys.Add(key);
        Audit("ConsumerApiKeyCreated", nameof(ConsumerApiKey), key.Id, actor, correlationId,
            new
            {
                key.Name, key.KeyPrefix, environmentIds, routeIds, claimNames = claims.Keys,
                allowedCidrs = normalizedCidrs, expiresAtUtc
            });
        await db.SaveChangesAsync(ct);
        return new CreatedApiKey(key.Id, prefix, secret);
    }

    public async Task<bool> RevokeManagementAsync(Guid id, string actor, string correlationId, CancellationToken ct)
    {
        var key = await db.ManagementApiKeys.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (key is null) return false;
        key.RevokedAtUtc ??= DateTimeOffset.UtcNow;
        Audit("ManagementApiKeyRevoked", nameof(ManagementApiKey), key.Id, actor, correlationId,
            new { key.Name, key.KeyPrefix });
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> RevokeConsumerAsync(Guid id, string actor, string correlationId, CancellationToken ct)
    {
        var key = await db.ConsumerApiKeys.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (key is null) return false;
        key.RevokedAtUtc ??= DateTimeOffset.UtcNow;
        Audit("ConsumerApiKeyRevoked", nameof(ConsumerApiKey), key.Id, actor, correlationId,
            new { key.Name, key.KeyPrefix });
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<CreatedApiKey> RotateManagementAsync(Guid id, string actor, string correlationId,
        CancellationToken ct)
    {
        var key = await db.ManagementApiKeys.SingleOrDefaultAsync(x => x.Id == id && x.RevokedAtUtc == null, ct) ??
                  throw new KeyNotFoundException("Management API key not found.");
        var generated = Generate();
        key.KeyPrefix = generated.Prefix;
        key.KeyHash = generated.Hash;
        key.LastUsedAtUtc = null;
        Audit("ManagementApiKeyRotated", nameof(ManagementApiKey), key.Id, actor, correlationId,
            new { key.Name, key.KeyPrefix });
        await db.SaveChangesAsync(ct);
        return new CreatedApiKey(key.Id, generated.Prefix, generated.Secret);
    }

    public async Task<CreatedApiKey> RotateConsumerAsync(Guid id, string actor, string correlationId,
        CancellationToken ct)
    {
        var key = await db.ConsumerApiKeys.SingleOrDefaultAsync(x => x.Id == id && x.RevokedAtUtc == null, ct) ??
                  throw new KeyNotFoundException("Consumer API key not found.");
        var generated = Generate();
        key.KeyPrefix = generated.Prefix;
        key.KeyHash = generated.Hash;
        key.LastUsedAtUtc = null;
        Audit("ConsumerApiKeyRotated", nameof(ConsumerApiKey), key.Id, actor, correlationId,
            new { key.Name, key.KeyPrefix });
        await db.SaveChangesAsync(ct);
        return new CreatedApiKey(key.Id, generated.Prefix, generated.Secret);
    }

    public async Task<bool> UpdateManagementAsync(Guid id, string name, IReadOnlyList<string> scopes,
        IReadOnlyList<string>? allowedCidrs, DateTimeOffset? expiresAtUtc, string actor, string correlationId,
        CancellationToken ct)
    {
        var key = await db.ManagementApiKeys.SingleOrDefaultAsync(x => x.Id == id, ct) ??
                  throw new KeyNotFoundException("Management API key not found.");
        var normalizedScopes = NormalizeManagementScopes(scopes);
        key.Name = name.Trim();
        key.ScopesJson = JsonSerializer.Serialize(normalizedScopes);
        key.AllowedCidrsJson = JsonSerializer.Serialize(CidrMatcher.Normalize(allowedCidrs));
        key.ExpiresAtUtc = expiresAtUtc;
        Audit("ManagementApiKeyUpdated", nameof(ManagementApiKey), key.Id, actor, correlationId,
            new { key.Name, scopes = normalizedScopes, expiresAtUtc });
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> UpdateConsumerAsync(Guid id, string name, IReadOnlyList<Guid> environmentIds,
        IReadOnlyList<string> routeIds, IReadOnlyDictionary<string, string> claims, IReadOnlyList<string>? allowedCidrs,
        DateTimeOffset? expiresAtUtc, string actor, string correlationId, CancellationToken ct)
    {
        var key = await db.ConsumerApiKeys.SingleOrDefaultAsync(x => x.Id == id, ct) ??
                  throw new KeyNotFoundException("Consumer API key not found.");
        key.Name = name.Trim();
        key.EnvironmentIdsJson = JsonSerializer.Serialize(environmentIds.Distinct());
        key.RouteIdsJson = JsonSerializer.Serialize(routeIds.Distinct(StringComparer.OrdinalIgnoreCase));
        key.ClaimsJson = JsonSerializer.Serialize(claims);
        key.AllowedCidrsJson = JsonSerializer.Serialize(CidrMatcher.Normalize(allowedCidrs));
        key.ExpiresAtUtc = expiresAtUtc;
        Audit("ConsumerApiKeyUpdated", nameof(ConsumerApiKey), key.Id, actor, correlationId,
            new { key.Name, environmentIds, routeIds, claimNames = claims.Keys, expiresAtUtc });
        await db.SaveChangesAsync(ct);
        return true;
    }

    private void Audit(string action, string targetType, Guid targetId, string actor, string correlationId,
        object details)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            ActorType = "User", ActorId = actor, Action = action, TargetType = targetType,
            TargetId = targetId.ToString(), CorrelationId = correlationId,
            DetailsJson = JsonSerializer.Serialize(details)
        });
    }

    private static string[] NormalizeManagementScopes(IReadOnlyList<string> scopes)
    {
        var normalized = scopes.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var invalid = normalized.Where(scope => !ManagementScopes.Contains(scope)).ToArray();
        if (invalid.Length > 0)
            throw new ArgumentException($"Unsupported management API key scope: {string.Join(", ", invalid)}.",
                nameof(scopes));
        if (normalized.Length == 0)
            throw new ArgumentException("At least one management API key scope is required.", nameof(scopes));
        return normalized;
    }

    private static (string Secret, string Prefix, byte[] Hash) Generate()
    {
        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-')
            .Replace('/', '_');
        return (secret, secret[..8], SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
    }
}