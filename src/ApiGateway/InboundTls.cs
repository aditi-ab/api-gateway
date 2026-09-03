using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using ApiGateway.Application;
using ApiGateway.Domain;
using ApiGateway.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ApiGateway;

public sealed class InboundCertificateRegistry : IDisposable
{
    private readonly string cachePath;
    private readonly CertificateMaterialProtector protector;
    private readonly List<X509Certificate2> retiredCertificates = [];
    private string? cacheFingerprint;

    private volatile IReadOnlyDictionary<string, X509Certificate2> certificates =
        new Dictionary<string, X509Certificate2>();

    public InboundCertificateRegistry(CertificateMaterialProtector protector, string cachePath)
    {
        this.protector = protector;
        this.cachePath = Path.GetFullPath(cachePath);
        try
        {
            if (File.Exists(this.cachePath))
            {
                var json = File.ReadAllText(this.cachePath);
                LoadCache(JsonSerializer.Deserialize<CertificateCache>(json));
                cacheFingerprint = Fingerprint(json);
            }
        }
        catch
        {
            certificates = new Dictionary<string, X509Certificate2>();
        }
    }

    public void Dispose()
    {
        foreach (var certificate in certificates.Values.Distinct()) certificate.Dispose();
        lock (retiredCertificates)
        {
            foreach (var certificate in retiredCertificates) certificate.Dispose();
        }
    }

    public X509Certificate2? Select(string? host)
    {
        return Select(certificates, host);
    }

    internal static TValue? Select<TValue>(IReadOnlyDictionary<string, TValue> values, string? host)
        where TValue : class
    {
        if (host is null) return null;
        try
        {
            host = DnsHostPattern.Normalize(host);
        }
        catch (ArgumentException)
        {
            return null;
        }

        return values.TryGetValue(host, out var exact)
            ? exact
            : values.Where(x => x.Key.StartsWith("*.") && DnsHostPattern.Covers(x.Key, host))
                .OrderByDescending(x => x.Key.Length).Select(x => x.Value).FirstOrDefault();
    }

    public async Task RefreshAsync(GatewayDbContext db, string environmentSlug, CancellationToken ct)
    {
        var environment = await db.Environments.AsNoTracking().SingleOrDefaultAsync(x => x.Slug == environmentSlug, ct);
        if (environment?.ActiveRevisionId is null) return;
        var json = await db.Revisions.AsNoTracking().Where(x => x.Id == environment.ActiveRevisionId)
            .Select(x => x.ConfigJson).SingleAsync(ct);
        var document = ConfigDocuments.Parse(json);
        var assignments = document.Routes.Where(x => x.Enabled && x.Inbound.CertificateId is not null)
            .SelectMany(route =>
                route.Match.Hosts.Select(host =>
                    (Host: DnsHostPattern.Normalize(host), Id: route.Inbound.CertificateId!.Value)))
            .ToArray();
        var ids = assignments.Select(x => x.Id).Distinct().ToArray();
        var records = await db.InboundCertificates.AsNoTracking().Where(x => ids.Contains(x.Id)).ToListAsync(ct);
        var cached = new CertificateCache(assignments.Select(x => new CertificateAssignment(x.Host, x.Id)).ToArray(),
            records.Select(x => new CachedCertificate(x.Id, Convert.ToBase64String(x.ProtectedPkcs12))).ToArray());
        var jsonCache = JsonSerializer.Serialize(cached);
        var fingerprint = Fingerprint(jsonCache);
        if (fingerprint == cacheFingerprint) return;
        LoadCache(cached);
        cacheFingerprint = fingerprint;
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        await File.WriteAllTextAsync(cachePath, jsonCache, ct);
    }

    private void LoadCache(CertificateCache? cache)
    {
        if (cache is null) return;
        var loaded = cache.Certificates.ToDictionary(x => x.Id,
            x => protector.Unprotect(Convert.FromBase64String(x.ProtectedPkcs12)));
        var next = cache.Assignments.Where(x => loaded.ContainsKey(x.Id)).ToDictionary(x => x.Host, x => loaded[x.Id],
            StringComparer.OrdinalIgnoreCase);
        var previous = Interlocked.Exchange(ref certificates, next);
        lock (retiredCertificates)
        {
            retiredCertificates.AddRange(previous.Values.Distinct().Where(x => !next.Values.Contains(x)));
        }
    }

    private static string Fingerprint(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    private sealed record CertificateAssignment(string Host, Guid Id);

    private sealed record CachedCertificate(Guid Id, string ProtectedPkcs12);

    private sealed record CertificateCache(
        IReadOnlyList<CertificateAssignment> Assignments,
        IReadOnlyList<CachedCertificate> Certificates);
}

public sealed class InboundCertificateRefreshService(
    IServiceScopeFactory scopes,
    InboundCertificateRegistry registry,
    InboundSecurityStore security,
    AcmeHttpChallengeStore acmeChallenges,
    IOptions<GatewayOptions> options,
    ILogger<InboundCertificateRefreshService> logger) : BackgroundService
{
    private readonly HashSet<(Guid Id, Guid Version)> expiryWarnings = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();
                await registry.RefreshAsync(db, options.Value.Environment, stoppingToken);
                await security.RefreshAsync(db, stoppingToken);
                await acmeChallenges.RefreshAsync(db, stoppingToken);
                var warningThreshold = DateTimeOffset.UtcNow.AddDays(30);
                var certificateMetadata = await db.InboundCertificates.AsNoTracking()
                    .Select(x => new { x.Id, Version = x.ConcurrencyVersion, x.NotAfterUtc })
                    .ToListAsync(stoppingToken);
                var expiring = certificateMetadata.Where(x => x.NotAfterUtc <= warningThreshold);
                foreach (var certificate in expiring.Where(x => expiryWarnings.Add((x.Id, x.Version))))
                    logger.LogWarning("Inbound TLS certificate {CertificateId} expires at {NotAfterUtc}.",
                        certificate.Id, certificate.NotAfterUtc);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(exception, "Inbound certificates could not be refreshed.");
            }

            await Task.Delay(options.Value.PollInterval, stoppingToken);
        }
    }
}

public sealed class AcmeHttpChallengeStore
{
    private volatile IReadOnlyDictionary<string, string> challenges = new Dictionary<string, string>();

    public bool TryGet(string host, string token, out string keyAuthorization)
    {
        return challenges.TryGetValue(Key(host, token), out keyAuthorization!);
    }

    public async Task RefreshAsync(GatewayDbContext db, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var values = await db.AcmeChallenges.AsNoTracking().ToListAsync(ct);
        challenges = values.Where(x => x.Kind == AcmeChallengeKind.Http01 && x.ExpiresAtUtc > now &&
                                       x.Token is not null && x.KeyAuthorization is not null)
            .ToDictionary(x => Key(x.Host, x.Token!), x => x.KeyAuthorization!,
                StringComparer.OrdinalIgnoreCase);
    }

    private static string Key(string host, string token)
    {
        return $"{host.TrimEnd('.')}\n{token}";
    }
}

public sealed record InboundSecuritySnapshot(
    bool Enabled,
    IReadOnlyList<string> Hosts,
    int MaxAgeSeconds,
    bool IncludeSubDomains,
    bool Preload);

public sealed class InboundSecurityStore
{
    private volatile InboundSecuritySnapshot current = new(false, [], 15_552_000, false, false);
    public InboundSecuritySnapshot Current => current;

    public async Task RefreshAsync(GatewayDbContext db, CancellationToken ct)
    {
        var value = await db.InboundSecuritySettings.AsNoTracking().SingleOrDefaultAsync(ct);
        current = value is null
            ? new InboundSecuritySnapshot(false, [], 15_552_000, false, false)
            : new InboundSecuritySnapshot(value.HstsEnabled,
                JsonSerializer.Deserialize<string[]>(value.HstsHostsJson) ?? [], value.HstsMaxAgeSeconds,
                value.HstsIncludeSubDomains, value.HstsPreload);
    }
}