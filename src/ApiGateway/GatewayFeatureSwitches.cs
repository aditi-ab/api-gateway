using ApiGateway.Domain;

namespace ApiGateway;

public static class GatewayFeatureSwitches
{
    public static GatewayConfigDocument Apply(GatewayConfigDocument document)
    {
        var routes = document.Routes.Select(Apply).ToArray();
        var disabledResilienceClusters = routes
            .Where(route => route.DisabledFeatures.Contains("resilience", StringComparer.OrdinalIgnoreCase))
            .Select(route => route.ClusterId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var clusters = document.Clusters.Select(cluster => disabledResilienceClusters.Contains(cluster.Id)
            ? cluster with { ResiliencePolicy = null }
            : cluster).ToArray();

        return document with { Routes = routes, Clusters = clusters };
    }

    private static GatewayRoute Apply(GatewayRoute route)
    {
        bool Disabled(string id)
        {
            return route.DisabledFeatures.Contains(id, StringComparer.OrdinalIgnoreCase);
        }

        var access = route.Access;
        if (access is not null)
        {
            access = access with
            {
                AllowedCidrs = Disabled("ip-restrictions") ? null : access.AllowedCidrs,
                DeniedCidrs = Disabled("ip-restrictions") ? null : access.DeniedCidrs,
                MaximumRequestBodyBytes = Disabled("request-size") ? null : access.MaximumRequestBodyBytes
            };
            if (access.AllowedCidrs is null && access.DeniedCidrs is null && access.MaximumRequestBodyBytes is null)
                access = null;
        }

        var transforms = route.Transforms.Where(transform =>
        {
            var keys = transform.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (Disabled("headers") && (keys.Contains("RequestHeader") || keys.Contains("ResponseHeader")))
                return false;
            return !Disabled("transforms") || keys.Contains("RequestHeaderOriginalHost") ||
                   keys.Contains("RequestHeader") || keys.Contains("ResponseHeader");
        }).ToArray();

        return route with
        {
            AuthorizationPolicy = Disabled("authorization") ? "Anonymous" : route.AuthorizationPolicy,
            RateLimitPolicy = Disabled("rate-limit") ? null : route.RateLimitPolicy,
            TimeoutPolicy = Disabled("timeout") ? null : route.TimeoutPolicy,
            CorsPolicy = Disabled("cors") ? null : route.CorsPolicy,
            Transforms = transforms,
            Mirror = Disabled("mirror") ? null : route.Mirror,
            Access = access,
            RequestValidation = Disabled("request-validation") ? null : route.RequestValidation,
            ResponseCache = Disabled("response-cache") ? null : route.ResponseCache
        };
    }
}