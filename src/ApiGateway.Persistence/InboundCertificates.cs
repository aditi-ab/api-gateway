using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using ApiGateway.Application;
using ApiGateway.Domain;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace ApiGateway.Persistence;

public sealed class CertificateMaterialProtector
{
    private readonly IDataProtector protector;
    private readonly IDataProtectionProvider provider;

    public CertificateMaterialProtector(string keyPath)
    {
        Directory.CreateDirectory(keyPath);
        provider = DataProtectionProvider.Create(new DirectoryInfo(keyPath), options =>
            options.SetApplicationName("ApiGateway.InboundCertificates"));
        protector = provider.CreateProtector("pkcs12", "v1");
    }

    public byte[] Protect(byte[] pkcs12, string password)
    {
        return protector.Protect(JsonSerializer.SerializeToUtf8Bytes(
            new CertificateEnvelope(Convert.ToBase64String(pkcs12), password)));
    }

    public X509Certificate2 Unprotect(byte[] protectedBytes)
    {
        var envelope = JsonSerializer.Deserialize<CertificateEnvelope>(protector.Unprotect(protectedBytes))!;
        return X509CertificateLoader.LoadPkcs12(Convert.FromBase64String(envelope.Pkcs12), envelope.Password,
            X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.Exportable);
    }

    public byte[] ProtectSecret(string purpose, byte[] value)
    {
        return provider.CreateProtector("acme", purpose, "v1").Protect(value);
    }

    public byte[] UnprotectSecret(string purpose, byte[] value)
    {
        return provider.CreateProtector("acme", purpose, "v1").Unprotect(value);
    }

    private sealed record CertificateEnvelope(string Pkcs12, string Password);
}

public sealed class InboundCertificateService(GatewayDbContext db, CertificateMaterialProtector protector)
{
    public Task<List<InboundCertificate>> ListAsync(CancellationToken ct)
    {
        return db.InboundCertificates.AsNoTracking()
            .OrderBy(x => x.Name).ToListAsync(ct);
    }

    public async Task<InboundCertificate> UploadAsync(string name, byte[] pkcs12, string? password, string actor,
        CancellationToken ct)
    {
        name = RequiredName(name);
        if (pkcs12.Length is 0 or > 5_242_880)
            throw new ArgumentException("Certificate files may contain at most 5 MiB.");
        using var certificate = Load(pkcs12, password);
        Validate(certificate);
        var exportPassword = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var record = new InboundCertificate
        {
            Name = name,
            ProtectedPkcs12 =
                protector.Protect(certificate.Export(X509ContentType.Pkcs12, exportPassword), exportPassword),
            Thumbprint = certificate.Thumbprint, Subject = certificate.Subject,
            DnsNamesJson = JsonSerializer.Serialize(DnsNames(certificate)),
            NotBeforeUtc = certificate.NotBefore.ToUniversalTime(),
            NotAfterUtc = certificate.NotAfter.ToUniversalTime(), CreatedBy = actor
        };
        db.InboundCertificates.Add(record);
        Audit("InboundCertificateCreated", record, actor);
        await db.SaveChangesAsync(ct);
        return record;
    }

    public async Task<InboundCertificate> RenameAsync(Guid id, Guid expectedVersion, string name, string actor,
        CancellationToken ct)
    {
        var record = await Required(id, ct);
        if (record.ConcurrencyVersion != expectedVersion) throw new DbUpdateConcurrencyException();
        var previousName = record.Name;
        record.Name = RequiredName(name);
        record.UpdatedAtUtc = DateTimeOffset.UtcNow;
        record.ConcurrencyVersion = Guid.NewGuid();
        db.AuditEvents.Add(new AuditEvent
        {
            ActorType = "User", ActorId = actor, Action = "InboundCertificateRenamed",
            TargetType = nameof(InboundCertificate), TargetId = record.Id.ToString(),
            CorrelationId = Guid.NewGuid().ToString("N"),
            DetailsJson = JsonSerializer.Serialize(new { PreviousName = previousName, record.Name })
        });
        await db.SaveChangesAsync(ct);
        return record;
    }

    public async Task<InboundCertificate> ReplaceAsync(Guid id, Guid expectedVersion, byte[] pkcs12,
        string? password, string actor, CancellationToken ct)
    {
        var record = await Required(id, ct);
        if (record.ConcurrencyVersion != expectedVersion) throw new DbUpdateConcurrencyException();
        using var certificate = Load(pkcs12, password);
        Validate(certificate);
        var names = DnsNames(certificate);
        await EnsureAssignedHostsCovered(id, names, ct);
        var exportPassword = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        record.ProtectedPkcs12 =
            protector.Protect(certificate.Export(X509ContentType.Pkcs12, exportPassword), exportPassword);
        record.Thumbprint = certificate.Thumbprint;
        record.Subject = certificate.Subject;
        record.DnsNamesJson = JsonSerializer.Serialize(names);
        record.NotBeforeUtc = certificate.NotBefore.ToUniversalTime();
        record.NotAfterUtc = certificate.NotAfter.ToUniversalTime();
        record.UpdatedAtUtc = DateTimeOffset.UtcNow;
        record.ConcurrencyVersion = Guid.NewGuid();
        Audit("InboundCertificateReplaced", record, actor);
        await db.SaveChangesAsync(ct);
        return record;
    }

    public async Task DeleteAsync(Guid id, string actor, CancellationToken ct)
    {
        var record = await Required(id, ct);
        await EnsureNotActive(id, ct);
        db.InboundCertificates.Remove(record);
        Audit("InboundCertificateDeleted", record, actor);
        await db.SaveChangesAsync(ct);
    }

    private async Task EnsureNotActive(Guid id, CancellationToken ct)
    {
        foreach (var json in await db.Environments.AsNoTracking().Where(x => x.ActiveRevisionId != null)
                     .Join(db.Revisions, x => x.ActiveRevisionId, x => x.Id, (_, revision) => revision.ConfigJson)
                     .ToListAsync(ct))
            if (ConfigDocuments.Parse(json).Routes.Any(x => x.Inbound.CertificateId == id))
                throw new InvalidOperationException("The certificate is assigned to an active route.");
    }

    private async Task EnsureAssignedHostsCovered(Guid id, IReadOnlyList<string> names, CancellationToken ct)
    {
        foreach (var json in await db.Environments.AsNoTracking().Where(x => x.ActiveRevisionId != null)
                     .Join(db.Revisions, x => x.ActiveRevisionId, x => x.Id, (_, revision) => revision.ConfigJson)
                     .ToListAsync(ct))
        foreach (var host in ConfigDocuments.Parse(json).Routes.Where(x => x.Inbound.CertificateId == id)
                     .SelectMany(x => x.Match.Hosts))
            if (!names.Any(name => Covers(name, host)))
                throw new InvalidOperationException(
                    $"The replacement certificate does not cover assigned host '{host}'.");
    }

    public static bool Covers(string certificateName, string host)
    {
        return DnsHostPattern.Covers(certificateName, host);
    }

    private static X509Certificate2 Load(byte[] bytes, string? password)
    {
        try
        {
            return X509CertificateLoader.LoadPkcs12(bytes, password,
                X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.Exportable);
        }
        catch (Exception exception) when (exception is CryptographicException or ArgumentException)
        {
            throw new ArgumentException("The PKCS#12 file or password is invalid.");
        }
    }

    private static void Validate(X509Certificate2 certificate)
    {
        if (!certificate.HasPrivateKey) throw new ArgumentException("The certificate must contain a private key.");
        if (certificate.NotAfter.ToUniversalTime() <= DateTime.UtcNow)
            throw new ArgumentException("The certificate has expired.");
        var eku = certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>().FirstOrDefault();
        if (eku is not null && !eku.EnhancedKeyUsages.Cast<Oid>().Any(x => x.Value == "1.3.6.1.5.5.7.3.1"))
            throw new ArgumentException("The certificate is not valid for server authentication.");
    }

    public static string[] DnsNames(X509Certificate2 certificate)
    {
        var san = certificate.Extensions.OfType<X509SubjectAlternativeNameExtension>().FirstOrDefault();
        var names = san?.EnumerateDnsNames().Select(x => x.ToLowerInvariant()).Distinct().Order().ToArray() ?? [];
        if (names.Length == 0)
        {
            var common = certificate.GetNameInfo(X509NameType.DnsName, false);
            if (!string.IsNullOrWhiteSpace(common)) names = [common.ToLowerInvariant()];
        }

        return names;
    }

    private async Task<InboundCertificate> Required(Guid id, CancellationToken ct)
    {
        return await db.InboundCertificates.SingleOrDefaultAsync(x => x.Id == id, ct) ??
               throw new KeyNotFoundException("Certificate not found.");
    }

    private static string RequiredName(string name)
    {
        name = name.Trim();
        return name.Length is > 0 and <= 200
            ? name
            : throw new ArgumentException("Certificate name is required and may contain at most 200 characters.");
    }

    private void Audit(string action, InboundCertificate certificate, string actor)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            ActorType = "User", ActorId = actor, Action = action, TargetType = nameof(InboundCertificate),
            TargetId = certificate.Id.ToString(), CorrelationId = Guid.NewGuid().ToString("N"),
            DetailsJson = JsonSerializer.Serialize(new { certificate.Name, certificate.Thumbprint })
        });
    }
}

public sealed class InboundSecuritySettingsService(GatewayDbContext db)
{
    public async Task<InboundSecuritySettings> GetAsync(CancellationToken ct)
    {
        return await db.InboundSecuritySettings.AsNoTracking().SingleOrDefaultAsync(ct) ??
               new InboundSecuritySettings();
    }

    public async Task<InboundSecuritySettings> UpdateAsync(Guid? expectedVersion, bool enabled,
        IReadOnlyList<string> hosts, int maxAgeSeconds, bool includeSubDomains, bool preload, string actor,
        CancellationToken ct)
    {
        if (enabled && hosts.Count == 0) throw new ArgumentException("At least one HSTS hostname is required.");
        if (maxAgeSeconds is < 0 or > 63_072_000)
            throw new ArgumentException("HSTS max-age must be between zero and two years.");
        if (preload && (!includeSubDomains || maxAgeSeconds < 31_536_000))
            throw new ArgumentException("HSTS preload requires includeSubDomains and a max-age of at least one year.");
        foreach (var host in hosts)
        {
            var pattern = host.Trim().TrimEnd('.');
            var dnsName = pattern.StartsWith("*.", StringComparison.Ordinal) ? pattern[2..] : pattern;
            if (pattern.Length == 0 || pattern.Count(x => x == '*') > (pattern.StartsWith("*.") ? 1 : 0) ||
                Uri.CheckHostName(dnsName) != UriHostNameType.Dns)
                throw new ArgumentException($"'{host}' is not a valid HSTS DNS host pattern.");
        }

        var value = await db.InboundSecuritySettings.SingleOrDefaultAsync(ct);
        if (value is null)
        {
            value = new InboundSecuritySettings();
            db.InboundSecuritySettings.Add(value);
        }
        else if (expectedVersion != value.ConcurrencyVersion)
        {
            throw new DbUpdateConcurrencyException();
        }

        value.HstsEnabled = enabled;
        value.HstsHostsJson =
            JsonSerializer.Serialize(hosts.Select(x => x.Trim().ToLowerInvariant()).Distinct().ToArray());
        value.HstsMaxAgeSeconds = maxAgeSeconds;
        value.HstsIncludeSubDomains = includeSubDomains;
        value.HstsPreload = preload;
        value.ConcurrencyVersion = Guid.NewGuid();
        db.AuditEvents.Add(new AuditEvent
        {
            ActorType = "User", ActorId = actor, Action = "InboundSecuritySettingsUpdated",
            TargetType = nameof(InboundSecuritySettings), TargetId = value.Id.ToString(),
            CorrelationId = Guid.NewGuid().ToString("N")
        });
        await db.SaveChangesAsync(ct);
        return value;
    }
}