using System.Net;

namespace ApiGateway.Domain;

public static class CidrMatcher
{
    public static IReadOnlyList<string> Normalize(IEnumerable<string>? cidrs)
    {
        var result = new List<string>();
        foreach (var value in cidrs ?? [])
        {
            if (!IPNetwork.TryParse(value.Trim(), out var network))
                throw new ArgumentException($"'{value}' is not a valid CIDR range.", nameof(cidrs));
            result.Add(network.ToString());
        }

        return result.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static bool Allows(IPAddress? address, IEnumerable<string>? cidrs)
    {
        var configured = cidrs?.ToArray() ?? [];
        if (configured.Length == 0) return true;
        if (address is null) return false;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        return configured.Any(value => IPNetwork.TryParse(value, out var network) && network.Contains(address));
    }
}