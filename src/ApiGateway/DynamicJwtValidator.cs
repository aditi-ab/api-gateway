using System.Collections.Concurrent;
using System.Security.Claims;
using ApiGateway.Domain;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace ApiGateway;

public sealed class DynamicJwtValidator
{
    private readonly JsonWebTokenHandler handler = new();

    private readonly ConcurrentDictionary<string, ConfigurationManager<OpenIdConnectConfiguration>> managers =
        new(StringComparer.OrdinalIgnoreCase);

    public async Task<ClaimsPrincipal?> ValidateAsync(string token, AuthorizationPolicy policy, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(policy.Authority) ||
            !Uri.TryCreate(policy.Authority, UriKind.Absolute, out var authority) ||
            authority.Scheme != Uri.UriSchemeHttps) return null;
        var manager = managers.GetOrAdd(policy.Authority,
            value => new ConfigurationManager<OpenIdConnectConfiguration>(
                $"{value.TrimEnd('/')}/.well-known/openid-configuration", new OpenIdConnectConfigurationRetriever(),
                new HttpDocumentRetriever { RequireHttps = true }));
        var configuration = await manager.GetConfigurationAsync(ct);
        var parameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true, IssuerSigningKeys = configuration.SigningKeys, ValidateIssuer = true,
            ValidIssuer = policy.Issuer ?? configuration.Issuer, ValidateAudience = true,
            ValidAudiences = policy.Audiences, ValidateLifetime = true,
            ClockSkew = policy.ClockSkew ?? TimeSpan.FromMinutes(2)
        };
        var result = await handler.ValidateTokenAsync(token, parameters);
        if (!result.IsValid || result.ClaimsIdentity is null) return null;
        var principal = new ClaimsPrincipal(result.ClaimsIdentity);
        return policy.RequiredClaims?.Any(required =>
            !principal.Claims.Any(x => x.Type == required.Key && x.Value == required.Value)) == true
            ? null
            : principal;
    }
}