using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace ApiGateway;

public sealed class LastKnownGoodStore(IDataProtectionProvider protection, IOptions<GatewayOptions> options)
{
    private readonly string path = options.Value.LastKnownGoodPath;
    private readonly IDataProtector protector = protection.CreateProtector("ApiGateway.LastKnownGood.v1");

    public async Task SaveAsync(Guid revisionId, string contentHash, string configJson,
        IReadOnlyList<ConsumerVerifier> credentials, CancellationToken ct)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var payload = JsonSerializer.Serialize(new Bundle(revisionId, contentHash, configJson, credentials));
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, protector.Protect(payload), ct);
        File.Move(temporary, path, true);
    }

    public async Task<Bundle?> LoadAsync(CancellationToken ct)
    {
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<Bundle>(protector.Unprotect(await File.ReadAllTextAsync(path, ct)));
        }
        catch (Exception)
        {
            return null;
        }
    }

    public sealed record Bundle(
        Guid RevisionId,
        string ContentHash,
        string ConfigJson,
        IReadOnlyList<ConsumerVerifier>? Credentials);
}