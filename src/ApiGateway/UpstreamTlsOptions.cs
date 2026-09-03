using ApiGateway.Domain;
using Microsoft.Extensions.Options;

namespace ApiGateway;

public sealed class UpstreamTlsOptions
{
    public Dictionary<string, ClientCertificateSecret> ClientCertificates { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> TrustBundles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed record ClientCertificateSecret(string Path, string? Password);

public sealed class SecretReferenceValidator(IOptions<UpstreamTlsOptions> options)
{
    public void Validate(GatewayConfigDocument document)
    {
        foreach (var cluster in document.Clusters)
        {
            if (cluster.Tls?.ClientCertificateRef is { } certificate &&
                (!options.Value.ClientCertificates.TryGetValue(certificate, out var secret) ||
                 !File.Exists(secret.Path)))
                throw new InvalidOperationException($"Client certificate reference '{certificate}' is unavailable.");
            if (cluster.Tls?.TrustBundleRef is { } trust &&
                (!options.Value.TrustBundles.TryGetValue(trust, out var path) || !File.Exists(path)))
                throw new InvalidOperationException($"Trust bundle reference '{trust}' is unavailable.");
        }
    }
}