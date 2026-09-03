using System.Globalization;

namespace ApiGateway.Domain;

public static class DnsHostPattern
{
    public static string Normalize(string value)
    {
        var normalized = value.Trim().TrimEnd('.').ToLowerInvariant();
        var wildcard = normalized.StartsWith("*.", StringComparison.Ordinal);
        var host = wildcard ? normalized[2..] : normalized;
        var ascii = new IdnMapping().GetAscii(host).ToLowerInvariant();
        return wildcard ? "*." + ascii : ascii;
    }

    public static string ToUnicode(string value)
    {
        var normalized = Normalize(value);
        var wildcard = normalized.StartsWith("*.", StringComparison.Ordinal);
        var host = wildcard ? normalized[2..] : normalized;
        var unicode = new IdnMapping().GetUnicode(host).ToLowerInvariant();
        return wildcard ? "*." + unicode : unicode;
    }

    public static bool Covers(string pattern, string host)
    {
        try
        {
            pattern = Normalize(pattern);
            host = Normalize(host);
        }
        catch (ArgumentException)
        {
            return false;
        }

        if (pattern == host) return true;
        return pattern.StartsWith("*.", StringComparison.Ordinal) &&
               host.EndsWith(pattern[1..], StringComparison.Ordinal) &&
               host.Count(x => x == '.') == pattern.Count(x => x == '.');
    }
}