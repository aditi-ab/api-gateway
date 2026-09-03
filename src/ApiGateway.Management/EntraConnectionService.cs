using System.Text.Json;
using ApiGateway.Domain;
using ApiGateway.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace ApiGateway.Management;

public sealed record EntraConnectionSnapshot(
    bool Enabled,
    string Authority,
    string Audience,
    string ClientId,
    string Scope,
    Guid Version)
{
    public bool Configured => Enabled && !string.IsNullOrWhiteSpace(Authority) &&
                              !string.IsNullOrWhiteSpace(Audience) &&
                              !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(Scope);
}

public sealed class EntraConnectionState(EntraConnectionSnapshot initial)
{
    private EntraConnectionSnapshot current = initial;
    public EntraConnectionSnapshot Current => Volatile.Read(ref current);

    public void Set(EntraConnectionSnapshot value)
    {
        Volatile.Write(ref current, value);
    }
}

public sealed class DynamicEntraJwtOptions(EntraConnectionState state) : IConfigureNamedOptions<JwtBearerOptions>
{
    public void Configure(string? name, JwtBearerOptions options)
    {
        if (name != JwtBearerDefaults.AuthenticationScheme)
            return;

        var settings = state.Current;
        options.Authority = settings.Configured ? settings.Authority : null;
        options.Audience = settings.Configured ? settings.Audience : null;

        options.RequireHttpsMetadata = true;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            NameClaimType = "preferred_username", RoleClaimType = "roles", ValidateIssuer = true,
            ValidateAudience = true, ValidateLifetime = true
        };
    }

    public void Configure(JwtBearerOptions options)
    {
        Configure(Options.DefaultName, options);
    }
}

public sealed class EntraConnectionService(
    GatewayDbContext db,
    EntraConnectionState state,
    IOptionsMonitorCache<JwtBearerOptions> optionsCache)
{
    private static readonly Guid SettingsId = Guid.Parse("00000000-0000-0000-0000-000000000003");

    public EntraConnectionSnapshot Get()
    {
        return state.Current;
    }

    public async Task LoadAsync(CancellationToken ct)
    {
        if ((await db.Database.GetPendingMigrationsAsync(ct)).Any())
            return;

        var entity = await db.EntraConnectionSettings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == SettingsId, ct);
        if (entity is not null)
            Apply(entity);
    }

    public async Task<EntraConnectionSnapshot> UpdateAsync(bool enabled, string authority, string audience,
        string clientId, string scope, Guid expectedVersion, string actor, string correlationId, CancellationToken ct)
    {
        authority = authority.Trim().TrimEnd('/');
        audience = audience.Trim();
        clientId = clientId.Trim();
        scope = scope.Trim();
        Validate(enabled, authority, audience, clientId, scope);
        var entity = await db.EntraConnectionSettings.SingleOrDefaultAsync(x => x.Id == SettingsId, ct);
        if (entity is null)
        {
            if (expectedVersion != Guid.Empty && expectedVersion != state.Current.Version)
                throw new DbUpdateConcurrencyException("The Entra connection was changed by another administrator.");
            entity = new EntraConnectionSettings { Id = SettingsId };
            db.EntraConnectionSettings.Add(entity);
        }
        else if (entity.ConcurrencyVersion != expectedVersion)
        {
            throw new DbUpdateConcurrencyException("The Entra connection was changed by another administrator.");
        }

        entity.Enabled = enabled;
        entity.Authority = authority;
        entity.Audience = audience;
        entity.ClientId = clientId;
        entity.Scope = scope;
        entity.ConcurrencyVersion = Guid.NewGuid();
        db.AuditEvents.Add(new AuditEvent
        {
            ActorType = "user", ActorId = actor, Action = "EntraConnectionUpdated", TargetType = "EntraConnection",
            TargetId = SettingsId.ToString(), CorrelationId = correlationId,
            DetailsJson = JsonSerializer.Serialize(new { enabled, authority, audience, clientId })
        });
        await db.SaveChangesAsync(ct);
        Apply(entity);
        return state.Current;
    }

    private void Apply(EntraConnectionSettings entity)
    {
        state.Set(new EntraConnectionSnapshot(entity.Enabled, entity.Authority, entity.Audience, entity.ClientId,
            entity.Scope, entity.ConcurrencyVersion));
        optionsCache.TryRemove(JwtBearerDefaults.AuthenticationScheme);
    }

    private static void Validate(bool enabled, string authority, string audience, string clientId, string scope)
    {
        if (!enabled)
            return;
        if (!Uri.TryCreate(authority, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("Authority must be an absolute HTTPS URL.");
        if (string.IsNullOrWhiteSpace(audience) || string.IsNullOrWhiteSpace(clientId) ||
            string.IsNullOrWhiteSpace(scope))
            throw new ArgumentException("Audience, client ID, and delegated scope are required when Entra is enabled.");
    }
}