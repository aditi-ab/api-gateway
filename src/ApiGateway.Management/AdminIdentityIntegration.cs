using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aditify.Identity;
using ApiGateway.Domain;
using ApiGateway.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ApiGateway.Management;

public sealed class GatewayAdminIdentityStore(GatewayDbContext db) : IAdminIdentityStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };

    public async Task<IReadOnlyList<AdminIdentityUser>> ListUsersAsync(CancellationToken ct) =>
        (await db.LocalAdministrators.AsNoTracking().OrderBy(x => x.NormalizedUsername).ToArrayAsync(ct)).Select(x => Map(x)!).ToArray();
    public async Task<AdminIdentityUser?> FindUserAsync(Guid id, CancellationToken ct) =>
        Map(await db.LocalAdministrators.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct));
    public async Task<AdminIdentityUser?> FindUserByUsernameAsync(string normalizedUsername, CancellationToken ct) =>
        Map(await db.LocalAdministrators.AsNoTracking().SingleOrDefaultAsync(x => x.NormalizedUsername == normalizedUsername, ct));
    public async Task SaveUserAsync(AdminIdentityUser user, CancellationToken ct)
    {
        var entity = await db.LocalAdministrators.SingleOrDefaultAsync(x => x.Id == user.Id, ct);
        var roles = user.RoleGrants.Select(x => x.Role).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray();
        if (entity is null)
        {
            entity = new LocalAdministrator { Id = user.Id, Username = user.Username, NormalizedUsername = user.NormalizedUsername,
                PasswordHash = user.PasswordHash, SecurityStamp = user.SecurityStamp };
            db.LocalAdministrators.Add(entity);
        }
        entity.Username = user.Username; entity.NormalizedUsername = user.NormalizedUsername; entity.DisplayName = user.DisplayName;
        entity.PasswordHash = user.PasswordHash; entity.SecurityStamp = user.SecurityStamp; entity.RolesJson = JsonSerializer.Serialize(roles);
        entity.Enabled = user.Enabled; entity.MustChangePassword = user.MustChangePassword; entity.LastLoginAtUtc = user.LastLoginAt;
        entity.ConcurrencyVersion = Guid.Parse(user.Version); entity.ExternalIdentitiesJson = JsonSerializer.Serialize(user.ExternalIdentities, JsonOptions);
        entity.RoleGrantsJson = JsonSerializer.Serialize(user.RoleGrants, JsonOptions);
        await db.SaveChangesAsync(ct);
    }
    public async Task DeleteUserAsync(Guid id, CancellationToken ct)
    {
        var entity = await db.LocalAdministrators.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) return; db.LocalAdministrators.Remove(entity); await db.SaveChangesAsync(ct);
    }
    public async Task<IReadOnlyList<AdminIdentityProvider>> ListProvidersAsync(CancellationToken ct) =>
        (await db.AdminIdentityProviders.AsNoTracking().OrderBy(x => x.Id).Select(x => x.Json).ToArrayAsync(ct)).Select(Provider).ToArray();
    public async Task<AdminIdentityProvider?> FindProviderAsync(string id, CancellationToken ct)
    {
        var normalized = id.Trim().ToLowerInvariant();
        var json = await db.AdminIdentityProviders.AsNoTracking().Where(x => x.Id == normalized).Select(x => x.Json).SingleOrDefaultAsync(ct);
        return json is null ? null : Provider(json);
    }
    public async Task SaveProviderAsync(AdminIdentityProvider provider, CancellationToken ct)
    {
        var id = provider.Id.Trim().ToLowerInvariant(); var json = JsonSerializer.Serialize(provider, JsonOptions);
        var entity = await db.AdminIdentityProviders.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) db.AdminIdentityProviders.Add(new AdminIdentityProviderDocument { Id = id, Json = json }); else entity.Json = json;
        await db.SaveChangesAsync(ct);
    }
    public async Task DeleteProviderAsync(string id, CancellationToken ct)
    {
        var normalized = id.Trim().ToLowerInvariant(); var entity = await db.AdminIdentityProviders.SingleOrDefaultAsync(x => x.Id == normalized, ct);
        if (entity is null) return; db.AdminIdentityProviders.Remove(entity); await db.SaveChangesAsync(ct);
    }
    private static AdminIdentityUser? Map(LocalAdministrator? x)
    {
        if (x is null) return null;
        var grants = Deserialize<AdminRoleGrant>(x.RoleGrantsJson);
        if (grants.Count == 0) grants = LocalAdministratorService.Roles(x).Select(role => new AdminRoleGrant(role, "local")).ToList();
        return new AdminIdentityUser { Id = x.Id, Username = x.Username, NormalizedUsername = x.NormalizedUsername, DisplayName = x.DisplayName,
            PasswordHash = x.PasswordHash, SecurityStamp = x.SecurityStamp, Enabled = x.Enabled, MustChangePassword = x.MustChangePassword,
            LastLoginAt = x.LastLoginAtUtc, Version = x.ConcurrencyVersion.ToString("D"), ExternalIdentities = Deserialize<AdminExternalIdentity>(x.ExternalIdentitiesJson), RoleGrants = grants };
    }
    private static List<T> Deserialize<T>(string json) => string.IsNullOrWhiteSpace(json)
        ? []
        : JsonSerializer.Deserialize<List<T>>(json, JsonOptions) ?? [];
    private static AdminIdentityProvider Provider(string json) => JsonSerializer.Deserialize<AdminIdentityProvider>(json, JsonOptions)!;
}

public sealed class GatewayRoleCatalog : IProductRoleCatalog { public IReadOnlyList<string> Roles => ManagementAuth.LocalRoles; }
public sealed class GatewayIdentityAuditSink(GatewayDbContext db) : IAdminIdentityAuditSink
{
    public async Task WriteAsync(string action, string target, string outcome, ClaimsPrincipal? actor, CancellationToken ct)
    {
        db.AuditEvents.Add(new AuditEvent { ActorType = actor?.Identity?.IsAuthenticated == true ? "User" : "System",
            ActorId = actor?.Identity?.Name ?? "system", Action = action, TargetType = "Identity", TargetId = target,
            DetailsJson = JsonSerializer.Serialize(new { outcome }), CorrelationId = Guid.NewGuid().ToString("N") });
        await db.SaveChangesAsync(ct);
    }
}
