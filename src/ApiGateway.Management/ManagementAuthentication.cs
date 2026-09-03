using System.Data;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using ApiGateway.Domain;
using ApiGateway.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using KeyNotFoundException = System.Collections.Generic.KeyNotFoundException;

namespace ApiGateway.Management;

public static class ManagementAuth
{
    public const string PolicyScheme = "Management";
    public const string CookieScheme = "ManagementCookie";
    public const string ApiKeyScheme = "ManagementApiKey";
    public const string AdministratorRole = "Administrator";
    public const string ReaderPolicy = "Reader";
    public const string WritePolicy = "ConfigurationEditor";
    public const string PublishPolicy = "Publisher";
    public const string ManageConfigurationPolicy = "ConfigurationManager";
    public const string AdministratorPolicy = "Administrator";
    public const string InstancesReadPolicy = "InstancesRead";
    public const string CredentialsReadPolicy = "CredentialsRead";
    public const string CredentialsWritePolicy = "CredentialsWrite";
    public const string AuditReadPolicy = "AuditRead";
    public const string MustChangePasswordClaim = "apigateway.must-change-password";
    public static readonly string[] LocalRoles = ["Reader", "Publisher", AdministratorRole];
}

public sealed class LocalAdministratorService(GatewayDbContext db, IPasswordHasher<LocalAdministrator> hasher)
{
    public async Task<(bool BootstrapRequired, string? Username)> StatusAsync(ClaimsPrincipal user,
        CancellationToken ct)
    {
        return (!await db.LocalAdministrators.AnyAsync(ct),
            user.Identity?.IsAuthenticated == true ? user.Identity.Name : null);
    }

    public async Task<LocalAdministrator> BootstrapAsync(string username, string password, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        if (await db.LocalAdministrators.AnyAsync(ct))
            throw new InvalidOperationException("Local administrator bootstrap is no longer available.");
        Validate(username, password);
        var admin = new LocalAdministrator
        {
            Username = username.Trim(), NormalizedUsername = username.Trim().ToUpperInvariant(),
            PasswordHash = "pending", SecurityStamp = Stamp(),
            RolesJson = JsonSerializer.Serialize(new[] { ManagementAuth.AdministratorRole })
        };
        admin.PasswordHash = hasher.HashPassword(admin, password);
        db.LocalAdministrators.Add(admin);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return admin;
    }

    public async Task<LocalAdministrator?> ValidateAsync(string username, string password, CancellationToken ct)
    {
        var normalized = username.Trim().ToUpperInvariant();
        var admin = await db.LocalAdministrators.SingleOrDefaultAsync(x => x.NormalizedUsername == normalized, ct);
        if (admin is null || !admin.Enabled) return null;
        if (hasher.VerifyHashedPassword(admin, admin.PasswordHash, password) ==
            PasswordVerificationResult.Failed) return null;
        admin.LastLoginAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return admin;
    }

    public static ClaimsPrincipal Principal(LocalAdministrator admin)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, admin.Id.ToString()), new Claim(ClaimTypes.Name, admin.Username),
            .. Roles(admin).Select(role => new Claim(ClaimTypes.Role, role)),
            new Claim("apigateway.security-stamp", admin.SecurityStamp),
            new Claim(ManagementAuth.MustChangePasswordClaim, admin.MustChangePassword ? "true" : "false")
        ], ManagementAuth.CookieScheme));
    }

    public Task<List<LocalAdministrator>> ListAsync(CancellationToken ct)
    {
        return db.LocalAdministrators.AsNoTracking().OrderBy(x => x.Username).ToListAsync(ct);
    }

    public async Task<(LocalAdministrator User, string TemporaryPassword)> CreateAsync(string username,
        string? displayName, IReadOnlyList<string> roles, string actor, CancellationToken ct)
    {
        ValidateUsernameAndRoles(username, roles);
        var password = TemporaryPassword();
        var user = new LocalAdministrator
        {
            Username = username.Trim(), NormalizedUsername = username.Trim().ToUpperInvariant(),
            DisplayName = NormalizeDisplayName(displayName),
            PasswordHash = "pending",
            SecurityStamp = Stamp(), RolesJson = JsonSerializer.Serialize(roles.Distinct().Order().ToArray()),
            MustChangePassword = true
        };
        user.PasswordHash = hasher.HashPassword(user, password);
        db.LocalAdministrators.Add(user);
        Audit("LocalUserCreated", user, actor);
        await db.SaveChangesAsync(ct);
        return (user, password);
    }

    public async Task<LocalAdministrator> UpdateAsync(Guid id, Guid expectedVersion, string? displayName,
        IReadOnlyList<string> roles, bool enabled, string actor, CancellationToken ct)
    {
        ValidateRoles(roles);
        var user = await Required(id, ct);
        if (user.ConcurrencyVersion != expectedVersion) throw new DbUpdateConcurrencyException();
        if ((!enabled || !roles.Contains(ManagementAuth.AdministratorRole)) &&
            Roles(user).Contains(ManagementAuth.AdministratorRole) && user.Enabled)
            await EnsureAnotherAdministrator(id, ct);
        var normalizedRoles = roles.Distinct().Order().ToArray();
        var accessChanged = user.Enabled != enabled || !Roles(user).Order().SequenceEqual(normalizedRoles);
        user.DisplayName = NormalizeDisplayName(displayName);
        user.RolesJson = JsonSerializer.Serialize(normalizedRoles);
        user.Enabled = enabled;
        if (accessChanged) user.SecurityStamp = Stamp();
        user.ConcurrencyVersion = Guid.NewGuid();
        Audit("LocalUserUpdated", user, actor);
        await db.SaveChangesAsync(ct);
        return user;
    }

    public async Task<(LocalAdministrator User, string TemporaryPassword)> ResetAsync(Guid id, string actor,
        CancellationToken ct)
    {
        var user = await Required(id, ct);
        var password = TemporaryPassword();
        user.PasswordHash = hasher.HashPassword(user, password);
        user.MustChangePassword = true;
        user.SecurityStamp = Stamp();
        user.ConcurrencyVersion = Guid.NewGuid();
        Audit("LocalUserPasswordReset", user, actor);
        await db.SaveChangesAsync(ct);
        return (user, password);
    }

    public async Task ChangePasswordAsync(Guid id, string currentPassword, string newPassword, string actor,
        CancellationToken ct)
    {
        var user = await Required(id, ct);
        if (hasher.VerifyHashedPassword(user, user.PasswordHash, currentPassword) == PasswordVerificationResult.Failed)
            throw new ArgumentException("The current password is incorrect.");
        Validate(user.Username, newPassword);
        user.PasswordHash = hasher.HashPassword(user, newPassword);
        user.MustChangePassword = false;
        user.SecurityStamp = Stamp();
        user.ConcurrencyVersion = Guid.NewGuid();
        Audit("LocalUserPasswordChanged", user, actor);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, string actor, CancellationToken ct)
    {
        var user = await Required(id, ct);
        if (user.Enabled && Roles(user).Contains(ManagementAuth.AdministratorRole))
            await EnsureAnotherAdministrator(id, ct);
        db.LocalAdministrators.Remove(user);
        Audit("LocalUserDeleted", user, actor);
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> ValidatePrincipalAsync(ClaimsPrincipal principal, CancellationToken ct)
    {
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) return false;
        var stamp = principal.FindFirstValue("apigateway.security-stamp");
        return await db.LocalAdministrators.AsNoTracking()
            .AnyAsync(x => x.Id == id && x.Enabled && x.SecurityStamp == stamp, ct);
    }

    public static string[] Roles(LocalAdministrator user)
    {
        return JsonSerializer.Deserialize<string[]>(user.RolesJson) ?? [];
    }

    private async Task<LocalAdministrator> Required(Guid id, CancellationToken ct)
    {
        return await db.LocalAdministrators.SingleOrDefaultAsync(x => x.Id == id, ct) ??
               throw new KeyNotFoundException("Local user not found.");
    }

    private async Task EnsureAnotherAdministrator(Guid id, CancellationToken ct)
    {
        var users = await db.LocalAdministrators.AsNoTracking().Where(x => x.Id != id && x.Enabled).ToListAsync(ct);
        if (!users.Any(x => Roles(x).Contains(ManagementAuth.AdministratorRole)))
            throw new InvalidOperationException("The last enabled administrator cannot be changed.");
    }

    private void Audit(string action, LocalAdministrator user, string actor)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            ActorType = "User", ActorId = actor, Action = action, TargetType = "LocalUser",
            TargetId = user.Id.ToString(),
            CorrelationId = Guid.NewGuid().ToString("N")
        });
    }

    private static void ValidateUsernameAndRoles(string username, IReadOnlyList<string> roles)
    {
        Validate(username, TemporaryPassword());
        ValidateRoles(roles);
    }

    private static void ValidateRoles(IReadOnlyList<string> roles)
    {
        if (roles.Count == 0 || roles.Any(x => !ManagementAuth.LocalRoles.Contains(x, StringComparer.Ordinal)))
            throw new ArgumentException("Select at least one valid role.");
    }

    private static string? NormalizeDisplayName(string? displayName)
    {
        var normalized = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
        if (normalized?.Length > 200)
            throw new ArgumentException("Display name must contain at most 200 characters.", nameof(displayName));
        return normalized;
    }

    private static string Stamp()
    {
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    }

    private static string TemporaryPassword()
    {
        return $"Ag!{Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).Replace('/', 'x').Replace('+', 'Y')}9";
    }

    private static void Validate(string username, string password)
    {
        if (username.Trim().Length is < 3 or > 100)
            throw new ArgumentException("Username must contain 3 to 100 characters.");
        if (password.Length is < 12 or > 128)
            throw new ArgumentException("Password must contain 12 to 128 characters.");
        if (password.Contains(username, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Password must not contain the username.");
    }
}

public sealed class ManagementApiKeyHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    GatewayDbContext db,
    ApiKeyUsageQueue usage) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Management-Api-Key", out var values) || values.Count != 1)
            return AuthenticateResult.NoResult();
        var raw = values.ToString();
        if (raw.Length is < 32 or > 256) return AuthenticateResult.Fail("Invalid management API key.");
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        var key = await db.ManagementApiKeys.SingleOrDefaultAsync(x => x.KeyHash == hash, Context.RequestAborted);
        if (key is null || key.RevokedAtUtc is not null || key.ExpiresAtUtc <= DateTimeOffset.UtcNow ||
            !CryptographicOperations.FixedTimeEquals(hash, key.KeyHash))
            return AuthenticateResult.Fail("Invalid management API key.");
        var allowedCidrs = JsonSerializer.Deserialize<string[]>(key.AllowedCidrsJson) ?? [];
        if (!CidrMatcher.Allows(Context.Connection.RemoteIpAddress, allowedCidrs))
            return AuthenticateResult.Fail("Invalid management API key.");
        var scopes = JsonSerializer.Deserialize<string[]>(key.ScopesJson) ?? [];
        var claims = new List<Claim>
            { new(ClaimTypes.NameIdentifier, key.Id.ToString()), new(ClaimTypes.Name, key.Name) };
        claims.AddRange(scopes.Select(x => new Claim("apigateway.scope", x)));
        claims.AddRange(scopes.Where(x => x is "Reader" or "ConfigurationEditor" or "Publisher" or "Administrator")
            .Select(x => new Claim(ClaimTypes.Role, x)));
        if (scopes.Contains("system:admin", StringComparer.Ordinal))
            claims.Add(new Claim(ClaimTypes.Role, ManagementAuth.AdministratorRole));
        usage.TryRecord(key.Id, true);
        return AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name));
    }
}

public sealed record LoginRequest(string Username, string Password, string? ProviderId = null);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
