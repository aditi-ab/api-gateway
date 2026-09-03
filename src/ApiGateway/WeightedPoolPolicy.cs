using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ApiGateway.Domain;
using Yarp.ReverseProxy.LoadBalancing;
using Yarp.ReverseProxy.Model;

namespace ApiGateway;

public sealed class WeightedPoolPolicy : ILoadBalancingPolicy
{
    public string Name => "ApiGatewayWeightedPools";

    public DestinationState? PickDestination(HttpContext context, ClusterState cluster,
        IReadOnlyList<DestinationState> availableDestinations)
    {
        if (cluster.Model.Config.Metadata is null ||
            !cluster.Model.Config.Metadata.TryGetValue("ApiGateway.Traffic", out var raw) ||
            string.IsNullOrWhiteSpace(raw))
            return availableDestinations.Count == 0
                ? null
                : availableDestinations[Random.Shared.Next(availableDestinations.Count)];
        var policy = JsonSerializer.Deserialize<TrafficPolicy>(raw, GatewayJson.Options);
        if (policy is null) return null;
        var bucket = Bucket(context, policy);
        if (bucket is null) return null;
        var cumulative = 0;
        string? selectedPool = null;
        foreach (var allocation in policy.Allocations.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            cumulative += allocation.Value;
            if (bucket.Value < cumulative)
            {
                selectedPool = allocation.Key;
                break;
            }
        }

        var candidates = availableDestinations.Where(x =>
            string.Equals(x.Model.Config.Metadata?.GetValueOrDefault("ApiGateway.Pool") ?? "default", selectedPool,
                StringComparison.OrdinalIgnoreCase)).ToArray();
        if (candidates.Length == 0 && policy.FallbackToHealthyPool) candidates = availableDestinations.ToArray();
        return candidates.Length == 0 ? null : candidates[Random.Shared.Next(candidates.Length)];
    }

    private static int? Bucket(HttpContext context, TrafficPolicy policy)
    {
        if (policy.Mode.Equals("random", StringComparison.OrdinalIgnoreCase)) return Random.Shared.Next(100);
        var value = policy.KeySource?.ToLowerInvariant() switch
        {
            "header" => context.Request.Headers[policy.Key ?? string.Empty].ToString(),
            "cookie" => context.Request.Cookies[policy.Key ?? string.Empty] ?? string.Empty,
            "claim" => context.User.FindFirst(policy.Key ?? string.Empty)?.Value ?? string.Empty,
            "consumerkey" => context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty,
            _ => string.Empty
        };
        if (string.IsNullOrEmpty(value)) return null;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return (int)(BitConverter.ToUInt32(hash, 0) % 100);
    }
}