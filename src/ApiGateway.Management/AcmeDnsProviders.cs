using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Amazon;
using Amazon.Route53;
using Amazon.Route53.Model;
using Amazon.Runtime;
using ApiGateway.Domain;
using Azure.Core;
using Azure.Identity;
using Google.Apis.Auth.OAuth2;

namespace ApiGateway.Management;

public sealed record DnsManagedZone(string Name, string Id);

public sealed record DnsProviderCredentials(
    string? ApiToken = null,
    string? AccessKeyId = null,
    string? SecretAccessKey = null,
    string? SessionToken = null,
    string? TenantId = null,
    string? ClientId = null,
    string? ClientSecret = null,
    string? SubscriptionId = null,
    string? ResourceGroup = null,
    string? ProjectId = null,
    string? ServiceAccountJson = null,
    string? Username = null,
    string? Password = null,
    string? CustomerNumber = null);

public interface IDnsChallengeProvider
{
    DnsProviderKind Kind { get; }
    Task<IReadOnlyList<DnsManagedZone>> ListZonesAsync(DnsProviderCredentials credentials, CancellationToken ct);

    Task<string?> PresentAsync(DnsProviderCredentials credentials, DnsManagedZone zone, string recordName,
        string value, CancellationToken ct);

    Task CleanupAsync(DnsProviderCredentials credentials, DnsManagedZone zone, string recordName, string value,
        string? providerRecordId, CancellationToken ct);
}

public sealed class DnsChallengeProviderFactory(IEnumerable<IDnsChallengeProvider> providers)
{
    public IDnsChallengeProvider Get(DnsProviderKind kind)
    {
        return providers.SingleOrDefault(x => x.Kind == kind) ??
               throw new InvalidOperationException($"DNS provider '{kind}' is not registered.");
    }

    public static DnsManagedZone SelectZone(IReadOnlyList<DnsManagedZone> zones, string recordName)
    {
        var normalized = NormalizeDnsName(recordName);
        return zones.Select(x => (Zone: x, Name: NormalizeDnsName(x.Name)))
                   .Where(x => normalized.Equals(x.Name, StringComparison.OrdinalIgnoreCase) ||
                               normalized.EndsWith('.' + x.Name, StringComparison.OrdinalIgnoreCase))
                   .OrderByDescending(x => x.Name.Length).Select(x => x.Zone).FirstOrDefault() ??
               throw new InvalidOperationException($"No managed DNS zone covers '{recordName}'.");
    }

    internal static string NormalizeDnsName(string value)
    {
        return new IdnMapping().GetAscii(value.TrimEnd('.')).ToLowerInvariant();
    }
}

public abstract class TokenRestDnsProvider(IHttpClientFactory clients)
{
    protected HttpClient Client(string token, string scheme = "Bearer")
    {
        var client = clients.CreateClient(nameof(AcmeDnsProviders));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(scheme, token);
        return client;
    }

    protected static async Task<JsonDocument> Json(HttpResponseMessage response, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"DNS provider request failed ({(int)response.StatusCode}).");
        return JsonDocument.Parse(body);
    }

    protected static StringContent Body(object value)
    {
        return new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");
    }
}

public sealed class CloudflareDnsProvider(IHttpClientFactory clients) : TokenRestDnsProvider(clients),
    IDnsChallengeProvider
{
    public DnsProviderKind Kind => DnsProviderKind.Cloudflare;

    public async Task<IReadOnlyList<DnsManagedZone>> ListZonesAsync(DnsProviderCredentials credentials,
        CancellationToken ct)
    {
        var client = Client(Required(credentials.ApiToken, "API token"));
        using var json = await Json(await client.GetAsync("https://api.cloudflare.com/client/v4/zones?per_page=50", ct),
            ct);
        return json.RootElement.GetProperty("result").EnumerateArray()
            .Select(x => new DnsManagedZone(x.GetProperty("name").GetString()!, x.GetProperty("id").GetString()!))
            .ToArray();
    }

    public async Task<string?> PresentAsync(DnsProviderCredentials credentials, DnsManagedZone zone,
        string recordName, string value, CancellationToken ct)
    {
        var client = Client(Required(credentials.ApiToken, "API token"));
        using var response = await client.PostAsync($"https://api.cloudflare.com/client/v4/zones/{zone.Id}/dns_records",
            Body(new { type = "TXT", name = recordName, content = value, ttl = 60 }), ct);
        using var json = await Json(response, ct);
        return json.RootElement.GetProperty("result").GetProperty("id").GetString();
    }

    public async Task CleanupAsync(DnsProviderCredentials credentials, DnsManagedZone zone, string recordName,
        string value, string? providerRecordId, CancellationToken ct)
    {
        if (providerRecordId is null) return;
        var client = Client(Required(credentials.ApiToken, "API token"));
        using var response = await client.DeleteAsync(
            $"https://api.cloudflare.com/client/v4/zones/{zone.Id}/dns_records/{providerRecordId}", ct);
        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
            throw new InvalidOperationException($"Cloudflare cleanup failed ({(int)response.StatusCode}).");
    }

    private static string Required(string? value, string field)
    {
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"Cloudflare {field} is required.");
    }
}

public sealed class DigitalOceanDnsProvider(IHttpClientFactory clients) : TokenRestDnsProvider(clients),
    IDnsChallengeProvider
{
    public DnsProviderKind Kind => DnsProviderKind.DigitalOcean;

    public async Task<IReadOnlyList<DnsManagedZone>> ListZonesAsync(DnsProviderCredentials credentials,
        CancellationToken ct)
    {
        var client = Client(Required(credentials.ApiToken));
        using var json = await Json(await client.GetAsync("https://api.digitalocean.com/v2/domains?per_page=200", ct),
            ct);
        return json.RootElement.GetProperty("domains").EnumerateArray().Select(x => x.GetProperty("name").GetString()!)
            .Select(x => new DnsManagedZone(x, x)).ToArray();
    }

    public async Task<string?> PresentAsync(DnsProviderCredentials credentials, DnsManagedZone zone,
        string recordName, string value, CancellationToken ct)
    {
        var relative = Relative(zone.Name, recordName);
        var client = Client(Required(credentials.ApiToken));
        using var response = await client.PostAsync($"https://api.digitalocean.com/v2/domains/{zone.Id}/records",
            Body(new { type = "TXT", name = relative, data = value, ttl = 60 }), ct);
        using var json = await Json(response, ct);
        return json.RootElement.GetProperty("domain_record").GetProperty("id").GetInt64().ToString();
    }

    public async Task CleanupAsync(DnsProviderCredentials credentials, DnsManagedZone zone, string recordName,
        string value, string? providerRecordId, CancellationToken ct)
    {
        if (providerRecordId is null) return;
        var client = Client(Required(credentials.ApiToken));
        using var response = await client.DeleteAsync(
            $"https://api.digitalocean.com/v2/domains/{zone.Id}/records/{providerRecordId}", ct);
        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
            throw new InvalidOperationException($"DigitalOcean cleanup failed ({(int)response.StatusCode}).");
    }

    private static string Required(string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException("DigitalOcean API token is required.");
    }

    internal static string Relative(string zone, string name)
    {
        var normalizedZone = DnsChallengeProviderFactory.NormalizeDnsName(zone);
        var suffix = '.' + normalizedZone;
        var normalized = DnsChallengeProviderFactory.NormalizeDnsName(name);
        return normalized.Equals(normalizedZone, StringComparison.OrdinalIgnoreCase)
            ? "@"
            : normalized.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                ? normalized[..^suffix.Length]
                : throw new ArgumentException("The record is outside the selected DNS zone.");
    }
}

public sealed class Route53DnsProvider : IDnsChallengeProvider
{
    public DnsProviderKind Kind => DnsProviderKind.Route53;

    public async Task<IReadOnlyList<DnsManagedZone>> ListZonesAsync(DnsProviderCredentials credentials,
        CancellationToken ct)
    {
        using var client = Client(credentials);
        var zones = new List<DnsManagedZone>();
        string? marker = null;
        do
        {
            var response = await client.ListHostedZonesAsync(new ListHostedZonesRequest { Marker = marker }, ct);
            zones.AddRange(response.HostedZones.Select(x =>
                new DnsManagedZone(x.Name.TrimEnd('.'), x.Id.Replace("/hostedzone/", string.Empty))));
            marker = response.IsTruncated == true ? response.NextMarker : null;
        } while (marker is not null);

        return zones;
    }

    public async Task<string?> PresentAsync(DnsProviderCredentials credentials, DnsManagedZone zone,
        string recordName, string value, CancellationToken ct)
    {
        using var client = Client(credentials);
        var values = await Values(client, zone, recordName, ct);
        if (!values.Contains(value, StringComparer.Ordinal)) values.Add(value);
        var response = await Change(client, zone, recordName, values, false, ct);
        return response.ChangeInfo.Id;
    }

    public async Task CleanupAsync(DnsProviderCredentials credentials, DnsManagedZone zone, string recordName,
        string value, string? providerRecordId, CancellationToken ct)
    {
        using var client = Client(credentials);
        var values = await Values(client, zone, recordName, ct);
        values.RemoveAll(x => x == value);
        await Change(client, zone, recordName, values.Count == 0 ? [value] : values, values.Count == 0, ct);
    }

    private static AmazonRoute53Client Client(DnsProviderCredentials value)
    {
        if (string.IsNullOrWhiteSpace(value.AccessKeyId) || string.IsNullOrWhiteSpace(value.SecretAccessKey))
            throw new ArgumentException("Route 53 access key ID and secret access key are required.");
        AWSCredentials credentials = string.IsNullOrWhiteSpace(value.SessionToken)
            ? new BasicAWSCredentials(value.AccessKeyId, value.SecretAccessKey)
            : new SessionAWSCredentials(value.AccessKeyId, value.SecretAccessKey, value.SessionToken);
        return new AmazonRoute53Client(credentials, RegionEndpoint.USEast1);
    }

    private static async Task<List<string>> Values(IAmazonRoute53 client, DnsManagedZone zone, string name,
        CancellationToken ct)
    {
        var response = await client.ListResourceRecordSetsAsync(new ListResourceRecordSetsRequest
        {
            HostedZoneId = zone.Id, StartRecordName = name, StartRecordType = RRType.TXT, MaxItems = "1"
        }, ct);
        var set = response.ResourceRecordSets.FirstOrDefault(x => x.Type == RRType.TXT &&
                                                                  x.Name.TrimEnd('.').Equals(name.TrimEnd('.'),
                                                                      StringComparison.OrdinalIgnoreCase));
        return set?.ResourceRecords.Select(x => x.Value.Trim('"')).ToList() ?? [];
    }

    private static Task<ChangeResourceRecordSetsResponse> Change(IAmazonRoute53 client, DnsManagedZone zone,
        string name, IReadOnlyList<string> values, bool delete, CancellationToken ct)
    {
        var action = delete ? ChangeAction.DELETE : ChangeAction.UPSERT;
        var set = new ResourceRecordSet(name, RRType.TXT)
        {
            TTL = 60, ResourceRecords = values.Select(x => new ResourceRecord($"\"{x}\"")).ToList()
        };
        return client.ChangeResourceRecordSetsAsync(new ChangeResourceRecordSetsRequest
        {
            HostedZoneId = zone.Id,
            ChangeBatch = new ChangeBatch { Changes = [new Change(action, set)] }
        }, ct);
    }
}

public sealed class AzureDnsProvider(IHttpClientFactory clients) : TokenRestDnsProvider(clients),
    IDnsChallengeProvider
{
    public DnsProviderKind Kind => DnsProviderKind.AzureDns;

    public async Task<IReadOnlyList<DnsManagedZone>> ListZonesAsync(DnsProviderCredentials credentials,
        CancellationToken ct)
    {
        var (client, subscription, group) = await Client(credentials, ct);
        using var json = await Json(await client.GetAsync(
            $"https://management.azure.com/subscriptions/{subscription}/resourceGroups/{group}/providers/Microsoft.Network/dnsZones?api-version=2018-05-01",
            ct), ct);
        return json.RootElement.GetProperty("value").EnumerateArray().Select(x =>
            new DnsManagedZone(x.GetProperty("name").GetString()!, x.GetProperty("id").GetString()!)).ToArray();
    }

    public async Task<string?> PresentAsync(DnsProviderCredentials credentials, DnsManagedZone zone,
        string recordName, string value, CancellationToken ct)
    {
        await Change(credentials, zone, recordName, value, true, ct);
        return recordName;
    }

    public Task CleanupAsync(DnsProviderCredentials credentials, DnsManagedZone zone, string recordName,
        string value, string? providerRecordId, CancellationToken ct)
    {
        return Change(credentials, zone, recordName, value, false, ct);
    }

    private async Task Change(DnsProviderCredentials credentials, DnsManagedZone zone, string name, string value,
        bool add, CancellationToken ct)
    {
        var (client, _, _) = await Client(credentials, ct);
        var relative = DigitalOceanDnsProvider.Relative(zone.Name, name);
        var url = $"https://management.azure.com{zone.Id}/TXT/{Uri.EscapeDataString(relative)}?api-version=2018-05-01";
        var values = new List<string>();
        using (var response = await client.GetAsync(url, ct))
        {
            if (response.IsSuccessStatusCode)
            {
                using var json = await Json(response, ct);
                values.AddRange(json.RootElement.GetProperty("properties").GetProperty("TXTRecords")
                    .EnumerateArray().SelectMany(x => x.GetProperty("value").EnumerateArray())
                    .Select(x => x.GetString()!));
            }
            else if (response.StatusCode != HttpStatusCode.NotFound)
            {
                throw new InvalidOperationException($"Azure DNS read failed ({(int)response.StatusCode}).");
            }
        }

        if (add && !values.Contains(value)) values.Add(value);
        if (!add) values.RemoveAll(x => x == value);
        if (values.Count == 0)
        {
            using var deleted = await client.DeleteAsync(url, ct);
            if (!deleted.IsSuccessStatusCode && deleted.StatusCode != HttpStatusCode.NotFound)
                throw new InvalidOperationException($"Azure DNS cleanup failed ({(int)deleted.StatusCode}).");
            return;
        }

        using var put = new HttpRequestMessage(HttpMethod.Put, url)
        {
            Content = Body(new
                { properties = new { TTL = 60, TXTRecords = values.Select(x => new { value = new[] { x } }) } })
        };
        using var result = await client.SendAsync(put, ct);
        using var ignored = await Json(result, ct);
    }

    private async Task<(HttpClient Client, string Subscription, string Group)> Client(
        DnsProviderCredentials value, CancellationToken ct)
    {
        if (new[] { value.TenantId, value.ClientId, value.ClientSecret, value.SubscriptionId, value.ResourceGroup }
            .Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException(
                "Azure tenant, client, client secret, subscription, and resource group are required.");
        var credential = new ClientSecretCredential(value.TenantId, value.ClientId, value.ClientSecret);
        var token = await credential.GetTokenAsync(new TokenRequestContext(["https://management.azure.com/.default"]),
            ct);
        return (base.Client(token.Token), value.SubscriptionId!, value.ResourceGroup!);
    }
}

public sealed class GoogleCloudDnsProvider(IHttpClientFactory clients) : TokenRestDnsProvider(clients),
    IDnsChallengeProvider
{
    public DnsProviderKind Kind => DnsProviderKind.GoogleCloudDns;

    public async Task<IReadOnlyList<DnsManagedZone>> ListZonesAsync(DnsProviderCredentials credentials,
        CancellationToken ct)
    {
        var (client, project) = await Client(credentials, ct);
        using var json = await Json(await client.GetAsync(
            $"https://dns.googleapis.com/dns/v1/projects/{Uri.EscapeDataString(project)}/managedZones", ct), ct);
        return json.RootElement.GetProperty("managedZones").EnumerateArray().Select(x =>
                new DnsManagedZone(x.GetProperty("dnsName").GetString()!.TrimEnd('.'),
                    x.GetProperty("name").GetString()!))
            .ToArray();
    }

    public async Task<string?> PresentAsync(DnsProviderCredentials credentials, DnsManagedZone zone,
        string recordName, string value, CancellationToken ct)
    {
        await Change(credentials, zone, recordName, value, true, ct);
        return recordName;
    }

    public Task CleanupAsync(DnsProviderCredentials credentials, DnsManagedZone zone, string recordName,
        string value, string? providerRecordId, CancellationToken ct)
    {
        return Change(credentials, zone, recordName, value, false, ct);
    }

    private async Task Change(DnsProviderCredentials credentials, DnsManagedZone zone, string name, string value,
        bool add, CancellationToken ct)
    {
        var (client, project) = await Client(credentials, ct);
        var fqdn = name.TrimEnd('.') + ".";
        var listUrl =
            $"https://dns.googleapis.com/dns/v1/projects/{Uri.EscapeDataString(project)}/managedZones/{Uri.EscapeDataString(zone.Id)}/rrsets?name={Uri.EscapeDataString(fqdn)}&type=TXT";
        using var json = await Json(await client.GetAsync(listUrl, ct), ct);
        var existing = json.RootElement.TryGetProperty("rrsets", out var sets)
            ? sets.EnumerateArray().FirstOrDefault()
            : default;
        var current = existing.ValueKind == JsonValueKind.Object
            ? existing.GetProperty("rrdatas").EnumerateArray().Select(x => x.GetString()!.Trim('"')).ToList()
            : [];
        if (add && !current.Contains(value)) current.Add(value);
        if (!add) current.RemoveAll(x => x == value);
        var deletion = existing.ValueKind == JsonValueKind.Object ? existing.Deserialize<object>() : null;
        object[] additions = current.Count == 0
            ? []
            : [new { name = fqdn, type = "TXT", ttl = 60, rrdatas = current.Select(x => $"\"{x}\"") }];
        using var response = await client.PostAsync(
            $"https://dns.googleapis.com/dns/v1/projects/{Uri.EscapeDataString(project)}/managedZones/{Uri.EscapeDataString(zone.Id)}/changes",
            Body(new { additions, deletions = deletion is null ? Array.Empty<object>() : new[] { deletion } }), ct);
        using var ignored = await Json(response, ct);
    }

    private async Task<(HttpClient Client, string Project)> Client(DnsProviderCredentials value, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(value.ProjectId) || string.IsNullOrWhiteSpace(value.ServiceAccountJson))
            throw new ArgumentException("Google Cloud project and service-account JSON are required.");
        var credential = CredentialFactory.FromJson<ServiceAccountCredential>(value.ServiceAccountJson)
            .ToGoogleCredential().CreateScoped("https://www.googleapis.com/auth/ndev.clouddns.readwrite");
        var token = await credential.UnderlyingCredential.GetAccessTokenForRequestAsync(cancellationToken: ct);
        return (base.Client(token), value.ProjectId);
    }
}

public sealed class LoopiaDnsProvider(IHttpClientFactory clients) : IDnsChallengeProvider
{
    private const string Endpoint = "https://api.loopia.se/RPCSERV";
    private const int RecordTtlSeconds = 300;
    public DnsProviderKind Kind => DnsProviderKind.Loopia;

    public async Task<IReadOnlyList<DnsManagedZone>> ListZonesAsync(DnsProviderCredentials credentials,
        CancellationToken ct)
    {
        var document = await Call(credentials, "getDomains", [], ct);
        var names = document.Descendants("struct").Select(structure => structure.Elements("member")
                .FirstOrDefault(x => x.Element("name")?.Value == "domain")?.Element("value")?.Value)
            .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return names.Select(x => new DnsManagedZone(x!, x!)).ToArray();
    }

    public async Task<string?> PresentAsync(DnsProviderCredentials credentials, DnsManagedZone zone,
        string recordName, string value, CancellationToken ct)
    {
        var relative = DigitalOceanDnsProvider.Relative(zone.Name, recordName);
        await EnsureSubdomain(credentials, zone.Name, relative, ct);
        var record = new XElement("struct",
            Member("type", "TXT"), Member("ttl", RecordTtlSeconds), Member("priority", 0), Member("rdata", value));
        await CallStatus(credentials, "addZoneRecord", [String(zone.Name), String(relative), Value(record)], ct);
        try
        {
            var records = await Call(credentials, "getZoneRecords", [String(zone.Name), String(relative)], ct);
            return records.Descendants("struct")
                       .Where(x => Field(x, "type") == "TXT" && Field(x, "rdata") == value)
                       .Select(x => Field(x, "record_id")).LastOrDefault() ??
                   throw new InvalidOperationException("Loopia did not return the TXT record after creating it.");
        }
        catch
        {
            try
            {
                await CleanupAsync(credentials, zone, recordName, value, null, ct);
            }
            catch when (!ct.IsCancellationRequested)
            {
            }

            throw;
        }
    }

    public async Task CleanupAsync(DnsProviderCredentials credentials, DnsManagedZone zone, string recordName,
        string value, string? providerRecordId, CancellationToken ct)
    {
        var relative = DigitalOceanDnsProvider.Relative(zone.Name, recordName);
        var recordIds = new List<string>();
        if (providerRecordId is not null)
        {
            recordIds.Add(providerRecordId);
        }
        else
        {
            var records = await Call(credentials, "getZoneRecords", [String(zone.Name), String(relative)], ct);
            recordIds.AddRange(records.Descendants("struct")
                .Where(x => Field(x, "type") == "TXT" && Field(x, "rdata") == value)
                .Select(x => Field(x, "record_id")).Where(x => !string.IsNullOrWhiteSpace(x)).OfType<string>());
        }

        foreach (var recordId in recordIds)
            await CallStatus(credentials, "removeZoneRecord",
                [String(zone.Name), String(relative), Integer(recordId)], ct);
    }

    private async Task EnsureSubdomain(DnsProviderCredentials credentials, string zoneName, string relative,
        CancellationToken ct)
    {
        if (relative == "@") return;
        var subdomains = await Call(credentials, "getSubdomains", [String(zoneName)], ct);
        if (subdomains.Descendants("array").Descendants("string")
            .Any(x => x.Value.Equals(relative, StringComparison.OrdinalIgnoreCase))) return;
        await CallStatus(credentials, "addSubdomain", [String(zoneName), String(relative)], ct);
    }

    private async Task CallStatus(DnsProviderCredentials credentials, string method,
        IReadOnlyList<XElement> additional, CancellationToken ct)
    {
        var response = await Call(credentials, method, additional, ct);
        var status = response.Root?.Element("params")?.Element("param")?.Element("value")?.Value.Trim();
        if (!string.Equals(status, "OK", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Loopia API rejected {method} with status '{SafeStatus(status)}'.");
    }

    private async Task<XDocument> Call(DnsProviderCredentials credentials, string method,
        IReadOnlyList<XElement> additional, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(credentials.Username) || string.IsNullOrWhiteSpace(credentials.Password))
            throw new ArgumentException("Loopia API username and password are required.");
        var parameters = new List<XElement> { String(credentials.Username), String(credentials.Password) };
        if (!string.IsNullOrWhiteSpace(credentials.CustomerNumber)) parameters.Add(String(credentials.CustomerNumber));
        parameters.AddRange(additional);
        var request = new XDocument(new XElement("methodCall", new XElement("methodName", method),
            new XElement("params", parameters.Select(x => new XElement("param", x)))));
        var client = clients.CreateClient(nameof(AcmeDnsProviders));
        using var response = await client.PostAsync(Endpoint,
            new StringContent(request.ToString(SaveOptions.DisableFormatting), Encoding.UTF8, "text/xml"), ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Loopia API failed ({(int)response.StatusCode}).");
        var document = XDocument.Parse(body);
        var fault = document.Descendants("fault").Descendants("struct").FirstOrDefault();
        if (fault is not null)
        {
            var code = SafeStatus(Field(fault, "faultCode"));
            var message = SafeStatus(Field(fault, "faultString"));
            throw new InvalidOperationException($"Loopia API rejected {method} ({code}): {message}");
        }

        return document;
    }

    private static string SafeStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return "UNKNOWN";
        return status.Length <= 100 ? status : status[..100];
    }

    private static XElement String(string value)
    {
        return Value(new XElement("string", value));
    }

    private static XElement Integer(string value)
    {
        return Value(new XElement("int", value));
    }

    private static XElement Value(XElement value)
    {
        return new XElement("value", value);
    }

    private static XElement Member(string name, object value)
    {
        return new XElement("member", new XElement("name", name),
            value is int number ? Value(new XElement("int", number)) : String(value.ToString()!));
    }

    private static string? Field(XElement value, string name)
    {
        return value.Elements("member")
            .FirstOrDefault(x => x.Element("name")?.Value == name)?.Element("value")?.Value;
    }
}

public sealed class SimplyDnsProvider(IHttpClientFactory clients) : TokenRestDnsProvider(clients),
    IDnsChallengeProvider
{
    private const string Endpoint = "https://api.simply.com/2";
    private const int RecordTtlSeconds = 300;
    public DnsProviderKind Kind => DnsProviderKind.Simply;

    public async Task<IReadOnlyList<DnsManagedZone>> ListZonesAsync(DnsProviderCredentials credentials,
        CancellationToken ct)
    {
        var client = Client(Required(credentials.ApiToken));
        using var response = await client.GetAsync($"{Endpoint}/my/products/", ct);
        using var json = await Json(response, ct);
        if (!json.RootElement.TryGetProperty("products", out var products) ||
            products.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Simply.com did not return a product list.");

        return products.EnumerateArray()
            .Where(x => !x.TryGetProperty("cancelled", out var cancelled) || !cancelled.GetBoolean())
            .Select(ProductZone)
            .Where(x => x is not null)
            .OfType<DnsManagedZone>()
            .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .ToArray();
    }

    public async Task<string?> PresentAsync(DnsProviderCredentials credentials, DnsManagedZone zone,
        string recordName, string value, CancellationToken ct)
    {
        var client = Client(Required(credentials.ApiToken));
        var relative = DigitalOceanDnsProvider.Relative(zone.Name, recordName);
        using var response = await client.PostAsync(RecordsEndpoint(zone),
            Body(new { type = "TXT", name = relative, data = value, ttl = RecordTtlSeconds }), ct);
        using var json = await Json(response, ct);
        if (!json.RootElement.TryGetProperty("record", out var record) ||
            !record.TryGetProperty("id", out var id) || !id.TryGetInt64(out var recordId))
            throw new InvalidOperationException("Simply.com did not return the created DNS record ID.");
        return recordId.ToString(CultureInfo.InvariantCulture);
    }

    public async Task CleanupAsync(DnsProviderCredentials credentials, DnsManagedZone zone, string recordName,
        string value, string? providerRecordId, CancellationToken ct)
    {
        var client = Client(Required(credentials.ApiToken));
        var recordIds = providerRecordId is null
            ? await FindRecordIds(client, zone, recordName, value, ct)
            : [providerRecordId];
        foreach (var recordId in recordIds)
        {
            using var response = await client.DeleteAsync(
                $"{RecordsEndpoint(zone)}{Uri.EscapeDataString(recordId)}/", ct);
            if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
                throw new InvalidOperationException($"Simply.com cleanup failed ({(int)response.StatusCode}).");
        }
    }

    private async Task<IReadOnlyList<string>> FindRecordIds(HttpClient client, DnsManagedZone zone,
        string recordName, string value, CancellationToken ct)
    {
        using var response = await client.GetAsync(RecordsEndpoint(zone), ct);
        using var json = await Json(response, ct);
        if (!json.RootElement.TryGetProperty("records", out var records) ||
            records.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Simply.com did not return a DNS record list.");

        var relative = DigitalOceanDnsProvider.Relative(zone.Name, recordName);
        return records.EnumerateArray()
            .Where(x => x.TryGetProperty("type", out var type) && type.GetString() == "TXT" &&
                        x.TryGetProperty("data", out var data) && data.GetString() == value &&
                        x.TryGetProperty("name", out var name) &&
                        (string.Equals(name.GetString()?.TrimEnd('.'), relative, StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(name.GetString()?.TrimEnd('.'), recordName.TrimEnd('.'),
                             StringComparison.OrdinalIgnoreCase)))
            .Select(x => x.GetProperty("record_id").GetInt64()
                .ToString(CultureInfo.InvariantCulture))
            .ToArray();
    }

    private static DnsManagedZone? ProductZone(JsonElement product)
    {
        if (!product.TryGetProperty("object", out var identifier) ||
            string.IsNullOrWhiteSpace(identifier.GetString()) ||
            !product.TryGetProperty("domain", out var domain) || domain.ValueKind != JsonValueKind.Object ||
            !domain.TryGetProperty("name", out var name) || string.IsNullOrWhiteSpace(name.GetString()))
            return null;
        return new DnsManagedZone(name.GetString()!, identifier.GetString()!);
    }

    private static string RecordsEndpoint(DnsManagedZone zone)
    {
        return $"{Endpoint}/my/products/{Uri.EscapeDataString(zone.Id)}/dns/records/";
    }

    private static string Required(string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException("Simply.com API key is required.");
    }
}

internal static class AcmeDnsProviders
{
}