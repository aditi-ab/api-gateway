using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ApiGateway.Domain;

namespace ApiGateway.Application;

public static partial class NamedUpstreamCompiler
{
    private const string Prefix = "__upstream_";

    public static IReadOnlyList<NamedUpstream> List(GatewayConfigDocument document)
    {
        return document.Clusters.Where(IsNamed).Select(ToManaged).OrderBy(x => x.Name).ToArray();
    }

    public static NamedUpstream? Find(GatewayConfigDocument document, string id)
    {
        var cluster = document.Clusters.FirstOrDefault(x => IsNamed(x) &&
                                                            x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        return cluster is null ? null : ToManaged(cluster);
    }

    public static GatewayConfigDocument Create(GatewayConfigDocument document, SaveNamedUpstreamInput input,
        out NamedUpstream created)
    {
        Validate(input);
        var root = Prefix + Slug(input.Name);
        var id = root;
        for (var suffix = 2; document.Clusters.Any(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase)); suffix++)
            id = $"{root}_{suffix}";
        var cluster = Cluster(id, input);
        var clusters = document.Clusters.Append(cluster).OrderBy(x => x.Id).ToArray();
        created = ToManaged(cluster);
        return document with { SchemaVersion = 3, Clusters = clusters };
    }

    public static GatewayConfigDocument Update(GatewayConfigDocument document, string id, string expectedVersion,
        SaveNamedUpstreamInput input, out NamedUpstream updated)
    {
        Validate(input);
        var current = document.Clusters.SingleOrDefault(x => IsNamed(x) &&
                                                              x.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) ??
                      throw new KeyNotFoundException("Upstream not found.");
        var managed = ToManaged(current);
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(managed.Version),
                Encoding.ASCII.GetBytes(expectedVersion)))
            throw new NamedUpstreamConflictException(managed.Version);
        var cluster = Cluster(current.Id, input);
        var clusters = document.Clusters.Where(x => !x.Id.Equals(current.Id, StringComparison.OrdinalIgnoreCase))
            .Append(cluster).OrderBy(x => x.Id).ToArray();
        updated = ToManaged(cluster);
        return document with { SchemaVersion = 3, Clusters = clusters };
    }

    public static GatewayConfigDocument Delete(GatewayConfigDocument document, string id, string expectedVersion,
        out NamedUpstream deleted)
    {
        var current = document.Clusters.SingleOrDefault(x => IsNamed(x) &&
                                                              x.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) ??
                      throw new KeyNotFoundException("Upstream not found.");
        deleted = ToManaged(current);
        if (!string.Equals(deleted.Version, expectedVersion, StringComparison.Ordinal))
            throw new NamedUpstreamConflictException(deleted.Version);
        if (document.Routes.Any(x => x.ClusterId.Equals(current.Id, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("The upstream is used by one or more routes.");
        return document with
        {
            SchemaVersion = 3,
            Clusters = document.Clusters.Where(x => !x.Id.Equals(current.Id, StringComparison.OrdinalIgnoreCase))
                .ToArray()
        };
    }

    public static bool IsNamed(GatewayCluster cluster)
    {
        return cluster.Metadata.ManagedByRouteId is null && cluster.Id.StartsWith(Prefix, StringComparison.Ordinal);
    }

    private static NamedUpstream ToManaged(GatewayCluster cluster)
    {
        var name = cluster.Metadata.DisplayName ?? cluster.Id[Prefix.Length..];
        var value = new
        {
            cluster.Id, Name = name, cluster.Destinations, cluster.LoadBalancingPolicy, cluster.Health,
            cluster.SessionAffinity, cluster.Traffic, cluster.Tls, cluster.HttpClient
        };
        var version = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(value, GatewayJson.Options))));
        return new NamedUpstream(cluster.Id, name, version, cluster.Destinations, cluster.LoadBalancingPolicy,
            cluster.Health, cluster.SessionAffinity, cluster.Traffic, cluster.Tls, cluster.HttpClient);
    }

    private static GatewayCluster Cluster(string id, SaveNamedUpstreamInput input)
    {
        return new GatewayCluster
        {
            Id = id,
            Destinations = input.Destinations.ToDictionary(x => x.Key.Trim(), x => x.Value,
                StringComparer.OrdinalIgnoreCase),
            LoadBalancingPolicy = input.LoadBalancingPolicy,
            Health = input.Health ?? new HealthPolicy(),
            SessionAffinity = input.SessionAffinity,
            Traffic = input.Traffic,
            Tls = input.Tls,
            HttpClient = input.HttpClient ?? new UpstreamHttpPolicy(),
            Metadata = new GatewayMetadata(DisplayName: input.Name.Trim())
        };
    }

    private static void Validate(SaveNamedUpstreamInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Trim().Length > 128)
            throw new ArgumentException("Upstream name must contain 1 to 128 characters.");
        if (input.Destinations.Count == 0)
            throw new ArgumentException("An upstream requires at least one server.");
        if (input.Destinations.Keys.Any(string.IsNullOrWhiteSpace) ||
            input.Destinations.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != input.Destinations.Count)
            throw new ArgumentException("Server names must be non-empty and unique.");
    }

    private static string Slug(string name)
    {
        var value = NonSlug().Replace(name.Trim().ToLowerInvariant(), "_").Trim('_');
        return string.IsNullOrEmpty(value) ? "upstream" : value[..Math.Min(value.Length, 64)];
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonSlug();
}

public sealed class NamedUpstreamConflictException(string currentVersion)
    : Exception("The upstream changed after it was loaded.")
{
    public string CurrentVersion { get; } = currentVersion;
}
