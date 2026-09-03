using System.Data;
using System.Globalization;
using System.Net;
using System.Net.Mail;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ApiGateway.Domain;
using ApiGateway.Persistence;
using Certes;
using Certes.Acme;
using Certes.Acme.Resource;
using DnsClient;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using KeyNotFoundException = System.Collections.Generic.KeyNotFoundException;

namespace ApiGateway.Management;

public sealed class AcmeOptions
{
    public string DirectoryUrl { get; set; } = "https://acme-v02.api.letsencrypt.org/directory";
    public string StagingDirectoryUrl { get; set; } = "https://acme-staging-v02.api.letsencrypt.org/directory";
    public TimeSpan WorkerInterval { get; set; } = TimeSpan.FromMinutes(1);
    public int MaxConcurrentOrders { get; set; } = 4;
    public TimeSpan RenewalInfoInterval { get; set; } = TimeSpan.FromHours(12);
    public TimeSpan HttpChallengePropagationDelay { get; set; } = TimeSpan.FromSeconds(15);
    public TimeSpan DnsPropagationTimeout { get; set; } = TimeSpan.FromHours(6);
    public TimeSpan DnsPropagationPollInterval { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan OrderFinalizationTimeout { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan OrderFinalizationPollInterval { get; set; } = TimeSpan.FromSeconds(2);
    public TimeSpan InProgressTimeout { get; set; } = TimeSpan.FromMinutes(30);
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(2);
    public TimeSpan LeaseRenewalInterval { get; set; } = TimeSpan.FromSeconds(30);
}

public sealed record AcmeDirectorySnapshot(
    string Name,
    string DirectoryUrl,
    bool IsStaging,
    string? TermsOfServiceUrl,
    Guid? AccountId)
{
    public bool Registered => AccountId is not null;
}

public sealed class AcmeAccountService(
    GatewayDbContext db,
    CertificateMaterialProtector protector,
    IOptions<AcmeOptions> options)
{
    public async Task<AcmeDirectorySnapshot> DirectoryAsync(CancellationToken ct)
    {
        return (await DirectoriesAsync(ct)).First(x => !x.IsStaging);
    }

    public async Task<IReadOnlyList<AcmeDirectorySnapshot>> DirectoriesAsync(CancellationToken ct)
    {
        var accounts = await db.AcmeAccounts.AsNoTracking().ToListAsync(ct);
        var result = new List<AcmeDirectorySnapshot>();
        foreach (var configured in ConfiguredDirectories())
        {
            var account = accounts.SingleOrDefault(x => x.DirectoryUrl == configured.Url);
            var terms = account?.TermsOfServiceUrl;
            if (account is null)
            {
                ct.ThrowIfCancellationRequested();
                var directory = await new AcmeContext(new Uri(configured.Url)).GetDirectory();
                terms = directory.Meta?.TermsOfService?.ToString();
            }

            result.Add(new AcmeDirectorySnapshot(configured.Name, configured.Url, configured.IsStaging, terms,
                account?.Id));
        }

        return result;
    }

    public Task<List<AcmeAccount>> ListAsync(CancellationToken ct)
    {
        return db.AcmeAccounts.AsNoTracking().OrderByDescending(x => x.IsDefault).ThenBy(x => x.IsStaging)
            .ThenBy(x => x.Name).ToListAsync(ct);
    }

    public Task<AcmeAccount?> GetAsync(CancellationToken ct)
    {
        return db.AcmeAccounts.AsNoTracking().OrderByDescending(x => x.IsDefault).ThenBy(x => x.IsStaging)
            .FirstOrDefaultAsync(ct);
    }

    public Task<AcmeAccount> RegisterAsync(string email, bool termsAccepted, string actor, CancellationToken ct)
    {
        return RegisterAsync(null, email, termsAccepted, actor, ct);
    }

    public async Task<AcmeAccount> RegisterAsync(string? directoryUrl, string email, bool termsAccepted, string actor,
        CancellationToken ct)
    {
        var configured = ResolveDirectory(directoryUrl);
        if (await db.AcmeAccounts.AnyAsync(x => x.DirectoryUrl == configured.Url, ct))
            throw new InvalidOperationException($"An account is already registered for {configured.Name}.");
        email = NormalizeEmail(email);
        if (!termsAccepted) throw new ArgumentException("The current Let's Encrypt terms of service must be accepted.");
        ct.ThrowIfCancellationRequested();
        var key = KeyFactory.NewKey(KeyAlgorithm.ES256);
        var context = new AcmeContext(new Uri(configured.Url), key);
        var directory = await context.GetDirectory();
        var accountContext = await context.NewAccount(email, true);
        var existing = await db.AcmeAccounts.ToListAsync(ct);
        var makeDefault = existing.Count == 0 || (!configured.IsStaging && existing.All(x => x.IsStaging));
        if (makeDefault)
            foreach (var current in existing.Where(x => x.IsDefault))
            {
                current.IsDefault = false;
                current.UpdatedAtUtc = DateTimeOffset.UtcNow;
                current.ConcurrencyVersion = Guid.NewGuid();
            }

        var account = new AcmeAccount
        {
            Name = configured.Name,
            DirectoryUrl = configured.Url,
            IsStaging = configured.IsStaging,
            IsDefault = makeDefault,
            ContactEmail = email,
            ProtectedAccountKey = protector.ProtectSecret("account-key", Encoding.UTF8.GetBytes(key.ToPem())),
            AccountUrl = accountContext.Location.ToString(),
            TermsOfServiceUrl = directory.Meta?.TermsOfService?.ToString(),
            TermsAcceptedAtUtc = DateTimeOffset.UtcNow
        };
        db.AcmeAccounts.Add(account);
        Audit(db, "AcmeAccountRegistered", nameof(AcmeAccount), account.Id.ToString(), actor,
            new { account.ContactEmail, account.DirectoryUrl });
        await db.SaveChangesAsync(ct);
        return account;
    }

    public async Task<AcmeAccount> UpdateContactAsync(Guid expectedVersion, string email, string actor,
        CancellationToken ct)
    {
        var account = await db.AcmeAccounts.OrderByDescending(x => x.IsDefault).ThenBy(x => x.IsStaging)
                          .FirstOrDefaultAsync(ct) ??
                      throw new InvalidOperationException("Register a Let's Encrypt account first.");
        return await UpdateContactAsync(account.Id, expectedVersion, email, actor, ct);
    }

    public async Task<AcmeAccount> UpdateContactAsync(Guid id, Guid expectedVersion, string email, string actor,
        CancellationToken ct)
    {
        var account = await db.AcmeAccounts.SingleOrDefaultAsync(x => x.Id == id, ct) ??
                      throw new KeyNotFoundException("ACME account not found.");
        if (account.ConcurrencyVersion != expectedVersion) throw new DbUpdateConcurrencyException();
        email = NormalizeEmail(email);
        var context = Context(account, protector);
        var remote = await context.Account();
        await remote.Update(["mailto:" + email]);
        account.ContactEmail = email;
        account.UpdatedAtUtc = DateTimeOffset.UtcNow;
        account.ConcurrencyVersion = Guid.NewGuid();
        Audit(db, "AcmeAccountContactUpdated", nameof(AcmeAccount), account.Id.ToString(), actor,
            new { account.ContactEmail });
        await db.SaveChangesAsync(ct);
        return account;
    }

    public async Task<AcmeAccount> SetDefaultAsync(Guid id, Guid expectedVersion, string actor, CancellationToken ct)
    {
        var accounts = await db.AcmeAccounts.ToListAsync(ct);
        var selected = accounts.SingleOrDefault(x => x.Id == id) ??
                       throw new KeyNotFoundException("ACME account not found.");
        if (selected.ConcurrencyVersion != expectedVersion) throw new DbUpdateConcurrencyException();
        foreach (var account in accounts)
        {
            var isDefault = account.Id == id;
            if (account.IsDefault == isDefault) continue;
            account.IsDefault = isDefault;
            account.UpdatedAtUtc = DateTimeOffset.UtcNow;
            account.ConcurrencyVersion = Guid.NewGuid();
        }

        Audit(db, "AcmeAccountDefaultChanged", nameof(AcmeAccount), id.ToString(), actor,
            new { selected.Name, selected.DirectoryUrl });
        await db.SaveChangesAsync(ct);
        return selected;
    }

    public async Task DeleteAsync(Guid id, string actor, CancellationToken ct)
    {
        var account = await db.AcmeAccounts.SingleOrDefaultAsync(x => x.Id == id, ct) ??
                      throw new KeyNotFoundException("ACME account not found.");
        if (await db.ManagedCertificates.AnyAsync(x => x.AcmeAccountId == id, ct))
            throw new InvalidOperationException("The ACME account is used by a managed certificate.");
        db.AcmeAccounts.Remove(account);
        if (account.IsDefault)
        {
            var replacement = await db.AcmeAccounts.Where(x => x.Id != id).OrderBy(x => x.IsStaging)
                .FirstOrDefaultAsync(ct);
            if (replacement is not null)
            {
                replacement.IsDefault = true;
                replacement.UpdatedAtUtc = DateTimeOffset.UtcNow;
                replacement.ConcurrencyVersion = Guid.NewGuid();
            }
        }

        Audit(db, "AcmeAccountDeleted", nameof(AcmeAccount), id.ToString(), actor,
            new { account.Name, account.DirectoryUrl });
        await db.SaveChangesAsync(ct);
    }

    internal static AcmeContext Context(AcmeAccount account, CertificateMaterialProtector protector)
    {
        var pem = Encoding.UTF8.GetString(protector.UnprotectSecret("account-key", account.ProtectedAccountKey));
        return new AcmeContext(new Uri(account.DirectoryUrl), KeyFactory.FromPem(pem));
    }

    private static string NormalizeEmail(string value)
    {
        value = value.Trim();
        if (value.Length > 320 || !MailAddress.TryCreate(value, out var address) ||
            !address.Address.Equals(value, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A valid contact email address is required.");
        return value;
    }

    private IReadOnlyList<ConfiguredDirectory> ConfiguredDirectories()
    {
        var production = NormalizeDirectoryUrl(options.Value.DirectoryUrl);
        var staging = NormalizeDirectoryUrl(options.Value.StagingDirectoryUrl);
        var values = new List<ConfiguredDirectory>
        {
            new("Let's Encrypt Production", production, false)
        };
        if (!staging.Equals(production, StringComparison.OrdinalIgnoreCase))
            values.Add(new ConfiguredDirectory("Let's Encrypt Staging", staging, true));
        return values;
    }

    private ConfiguredDirectory ResolveDirectory(string? directoryUrl)
    {
        var normalized = directoryUrl is null
            ? NormalizeDirectoryUrl(options.Value.DirectoryUrl)
            : NormalizeDirectoryUrl(directoryUrl);
        return ConfiguredDirectories()
                   .SingleOrDefault(x => x.Url.Equals(normalized, StringComparison.OrdinalIgnoreCase)) ??
               throw new ArgumentException("The ACME directory is not configured for this installation.");
    }

    private static string NormalizeDirectoryUrl(string value)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("https" or "http"))
            throw new InvalidOperationException("ACME directory URLs must be absolute HTTP or HTTPS URLs.");
        return uri.ToString();
    }

    internal static void Audit(GatewayDbContext db, string action, string targetType, string targetId, string actor,
        object? details = null)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            ActorType = actor.StartsWith("system:", StringComparison.Ordinal) ? "System" : "User",
            ActorId = actor, Action = action, TargetType = targetType, TargetId = targetId,
            CorrelationId = Guid.NewGuid().ToString("N"),
            DetailsJson = details is null ? "{}" : JsonSerializer.Serialize(details)
        });
    }

    private sealed record ConfiguredDirectory(string Name, string Url, bool IsStaging);
}

public sealed class DnsProviderProfileService(
    GatewayDbContext db,
    CertificateMaterialProtector protector,
    DnsChallengeProviderFactory providers)
{
    public Task<List<DnsProviderProfile>> ListAsync(CancellationToken ct)
    {
        return db.DnsProviderProfiles.AsNoTracking()
            .OrderBy(x => x.Name).ToListAsync(ct);
    }

    public async Task<DnsProviderProfile> CreateAsync(string name, DnsProviderKind provider,
        DnsProviderCredentials credentials, string actor, CancellationToken ct)
    {
        name = RequiredName(name);
        var zones = await providers.Get(provider).ListZonesAsync(credentials, ct);
        if (zones.Count == 0) throw new InvalidOperationException("The credentials cannot access any DNS zones.");
        var profile = new DnsProviderProfile
        {
            Name = name, Provider = provider,
            ProtectedCredentials = Protect(credentials), ManagedZonesJson = JsonSerializer.Serialize(zones),
            CreatedBy = actor
        };
        db.DnsProviderProfiles.Add(profile);
        AcmeAccountService.Audit(db, "DnsProviderProfileCreated", nameof(DnsProviderProfile), profile.Id.ToString(),
            actor, new { profile.Name, profile.Provider, zoneCount = zones.Count });
        await db.SaveChangesAsync(ct);
        return profile;
    }

    public async Task<DnsProviderProfile> UpdateAsync(Guid id, Guid expectedVersion, string name,
        DnsProviderCredentials? credentials, string actor, CancellationToken ct)
    {
        var profile = await Required(id, ct);
        if (profile.ConcurrencyVersion != expectedVersion) throw new DbUpdateConcurrencyException();
        var current = credentials ?? Credentials(profile);
        var zones = await providers.Get(profile.Provider).ListZonesAsync(current, ct);
        if (zones.Count == 0) throw new InvalidOperationException("The credentials cannot access any DNS zones.");
        profile.Name = RequiredName(name);
        if (credentials is not null) profile.ProtectedCredentials = Protect(credentials);
        profile.ManagedZonesJson = JsonSerializer.Serialize(zones);
        profile.UpdatedAtUtc = DateTimeOffset.UtcNow;
        profile.ConcurrencyVersion = Guid.NewGuid();
        AcmeAccountService.Audit(db, "DnsProviderProfileUpdated", nameof(DnsProviderProfile), profile.Id.ToString(),
            actor,
            new
            {
                profile.Name, profile.Provider, zoneCount = zones.Count, credentialsRotated = credentials is not null
            });
        await db.SaveChangesAsync(ct);
        return profile;
    }

    public async Task<IReadOnlyList<DnsManagedZone>> TestAsync(Guid id, CancellationToken ct)
    {
        var profile = await Required(id, ct);
        var provider = providers.Get(profile.Provider);
        var credentials = Credentials(profile);
        var zones = await provider.ListZonesAsync(credentials, ct);
        if (zones.Count == 0) throw new InvalidOperationException("The credentials cannot access any DNS zones.");
        var zone = zones.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).First();
        var zoneName = zone.Name.TrimEnd('.');
        var recordName = $"_apigateway-credential-test.{zoneName}";
        var value = $"apigateway-test-{Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant()}";
        string? recordId = null;
        Exception? presentationFailure = null;
        try
        {
            recordId = await provider.PresentAsync(credentials, zone, recordName, value, ct);
        }
        catch (Exception exception)
        {
            presentationFailure = exception;
        }

        try
        {
            await provider.CleanupAsync(credentials, zone, recordName, value, recordId, ct);
        }
        catch when (presentationFailure is not null && !ct.IsCancellationRequested)
        {
        }

        if (presentationFailure is not null) ExceptionDispatchInfo.Capture(presentationFailure).Throw();
        return zones;
    }

    public async Task DeleteAsync(Guid id, string actor, CancellationToken ct)
    {
        var profile = await Required(id, ct);
        if (await db.ManagedCertificates.AnyAsync(x => x.DnsProviderProfileId == id, ct))
            throw new InvalidOperationException("The DNS provider profile is used by a managed certificate.");
        db.Remove(profile);
        AcmeAccountService.Audit(db, "DnsProviderProfileDeleted", nameof(DnsProviderProfile), id.ToString(), actor,
            new { profile.Name, profile.Provider });
        await db.SaveChangesAsync(ct);
    }

    internal DnsProviderCredentials Credentials(DnsProviderProfile profile)
    {
        return JsonSerializer.Deserialize<DnsProviderCredentials>(
            protector.UnprotectSecret("dns-provider-credentials", profile.ProtectedCredentials))!;
    }

    internal static IReadOnlyList<DnsManagedZone> Zones(DnsProviderProfile profile)
    {
        return JsonSerializer.Deserialize<DnsManagedZone[]>(profile.ManagedZonesJson) ?? [];
    }

    private byte[] Protect(DnsProviderCredentials value)
    {
        return protector.ProtectSecret("dns-provider-credentials", JsonSerializer.SerializeToUtf8Bytes(value));
    }

    private Task<DnsProviderProfile> Required(Guid id, CancellationToken ct)
    {
        return db.DnsProviderProfiles.SingleOrDefaultAsync(x => x.Id == id, ct).ContinueWith(x =>
            x.Result ?? throw new KeyNotFoundException("DNS provider profile not found."), ct);
    }

    private static string RequiredName(string value)
    {
        value = value.Trim();
        if (value.Length is 0 or > 200)
            throw new ArgumentException("Profile name is required and may contain at most 200 characters.");
        return value;
    }
}

public sealed class ManagedCertificateService(GatewayDbContext db)
{
    public Task<List<ManagedCertificate>> ListAsync(CancellationToken ct)
    {
        return db.ManagedCertificates.AsNoTracking()
            .Include(x => x.AcmeAccount).Include(x => x.InboundCertificate).Include(x => x.DnsProviderProfile)
            .OrderBy(x => x.Name).ToListAsync(ct);
    }

    public async Task<ManagedCertificate> IssueAsync(string name, IReadOnlyList<string> dnsNames,
        AcmeChallengeKind challengeKind, Guid? dnsProviderProfileId, string actor, CancellationToken ct)
    {
        return await IssueAsync(name, dnsNames, challengeKind, dnsProviderProfileId, null, actor, ct);
    }

    public async Task<ManagedCertificate> IssueAsync(string name, IReadOnlyList<string> dnsNames,
        AcmeChallengeKind challengeKind, Guid? dnsProviderProfileId, Guid? acmeAccountId, string actor,
        CancellationToken ct)
    {
        var account = acmeAccountId is null
            ? await db.AcmeAccounts.OrderByDescending(x => x.IsDefault).ThenBy(x => x.IsStaging).FirstOrDefaultAsync(ct)
            : await db.AcmeAccounts.SingleOrDefaultAsync(x => x.Id == acmeAccountId, ct);
        if (account is null)
            throw new InvalidOperationException("Register the selected Let's Encrypt account first.");
        name = name.Trim();
        if (name.Length is 0 or > 200)
            throw new ArgumentException("Certificate name is required and may contain at most 200 characters.");
        var names = NormalizeDnsNames(dnsNames);
        if (challengeKind == AcmeChallengeKind.Http01 && names.Any(x => x.StartsWith("*.", StringComparison.Ordinal)))
            throw new ArgumentException("Wildcard certificates require DNS-01 validation.");
        if (challengeKind == AcmeChallengeKind.Dns01)
        {
            if (dnsProviderProfileId is null)
                throw new ArgumentException("A DNS provider profile is required for DNS-01 validation.");
            var profile = await db.DnsProviderProfiles.AsNoTracking()
                              .SingleOrDefaultAsync(x => x.Id == dnsProviderProfileId, ct) ??
                          throw new KeyNotFoundException("DNS provider profile not found.");
            foreach (var dnsName in names)
                DnsChallengeProviderFactory.SelectZone(DnsProviderProfileService.Zones(profile),
                    "_acme-challenge." + dnsName.TrimStart('*', '.'));
        }
        else if (dnsProviderProfileId is not null)
        {
            throw new ArgumentException("This validation method cannot select a DNS provider profile.");
        }

        var certificate = new ManagedCertificate
        {
            AcmeAccountId = account.Id, Name = name, DnsNamesJson = JsonSerializer.Serialize(names),
            ChallengeKind = challengeKind,
            DnsProviderProfileId = dnsProviderProfileId, CreatedBy = actor
        };
        db.ManagedCertificates.Add(certificate);
        AcmeAccountService.Audit(db, "ManagedCertificateRequested", nameof(ManagedCertificate),
            certificate.Id.ToString(), actor,
            new { certificate.Name, dnsNames = names, certificate.ChallengeKind, acmeAccountId = account.Id });
        await db.SaveChangesAsync(ct);
        return certificate;
    }

    public async Task<ManagedCertificate> RenewAsync(Guid id, Guid expectedVersion, string actor,
        CancellationToken ct)
    {
        var value = await Required(id, ct);
        if (value.ConcurrencyVersion != expectedVersion) throw new DbUpdateConcurrencyException();
        if (value.State is ManagedCertificateState.Issuing or ManagedCertificateState.Renewing)
            throw new InvalidOperationException("Certificate issuance is already in progress.");
        var now = DateTimeOffset.UtcNow;
        if (value.LastErrorCode == "ACME_RATE_LIMITED" && value.NextAttemptAtUtc > now)
            throw new InvalidOperationException(
                $"Let's Encrypt requested that this certificate not be retried before {value.NextAttemptAtUtc:u}.");
        if (value.LastAttemptAtUtc > now.AddMinutes(-5))
            throw new InvalidOperationException("Wait at least five minutes before requesting another attempt.");
        value.State = value.InboundCertificateId is null
            ? ManagedCertificateState.Pending
            : ManagedCertificateState.Active;
        value.NextAttemptAtUtc = now;
        value.LastErrorCode = null;
        value.LastErrorMessage = null;
        value.ConcurrencyVersion = Guid.NewGuid();
        value.UpdatedAtUtc = DateTimeOffset.UtcNow;
        AcmeAccountService.Audit(db, "ManagedCertificateRenewalRequested", nameof(ManagedCertificate), id.ToString(),
            actor);
        await db.SaveChangesAsync(ct);
        return value;
    }

    public async Task<ManagedCertificate> RenameAsync(Guid id, Guid expectedVersion, string name, string actor,
        CancellationToken ct)
    {
        var value = await Required(id, ct);
        if (value.ConcurrencyVersion != expectedVersion) throw new DbUpdateConcurrencyException();
        name = name.Trim();
        if (name.Length is 0 or > 200)
            throw new ArgumentException("Certificate name is required and may contain at most 200 characters.");
        var previousName = value.Name;
        value.Name = name;
        value.UpdatedAtUtc = DateTimeOffset.UtcNow;
        value.ConcurrencyVersion = Guid.NewGuid();
        if (value.InboundCertificate is not null)
        {
            value.InboundCertificate.Name = name;
            value.InboundCertificate.UpdatedAtUtc = DateTimeOffset.UtcNow;
            value.InboundCertificate.ConcurrencyVersion = Guid.NewGuid();
        }

        AcmeAccountService.Audit(db, "ManagedCertificateRenamed", nameof(ManagedCertificate), id.ToString(), actor,
            new { PreviousName = previousName, value.Name });
        await db.SaveChangesAsync(ct);
        return value;
    }

    public async Task DeleteAsync(Guid id, string actor, InboundCertificateService inbound, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var value = await Required(id, ct);
        var inboundCertificateId = value.InboundCertificateId;
        db.ManagedCertificates.Remove(value);
        AcmeAccountService.Audit(db, "ManagedCertificateDeleted", nameof(ManagedCertificate), id.ToString(), actor,
            new { value.Name });
        await db.SaveChangesAsync(ct);
        if (inboundCertificateId is not null) await inbound.DeleteAsync(inboundCertificateId.Value, actor, ct);
        await transaction.CommitAsync(ct);
    }

    public static string[] NormalizeDnsNames(IReadOnlyList<string> values)
    {
        if (values.Count is 0 or > 100) throw new ArgumentException("One to 100 DNS names are required.");
        var idn = new IdnMapping();
        var result = new List<string>();
        foreach (var raw in values)
        {
            var value = raw.Trim().TrimEnd('.').ToLowerInvariant();
            var wildcard = value.StartsWith("*.", StringComparison.Ordinal);
            var host = wildcard ? value[2..] : value;
            if (host.Contains('*') || IPAddress.TryParse(host, out _))
                throw new ArgumentException($"'{raw}' is not a valid certificate DNS name.");
            try
            {
                host = idn.GetAscii(host).ToLowerInvariant();
            }
            catch (ArgumentException)
            {
                throw new ArgumentException($"'{raw}' is not a valid certificate DNS name.");
            }

            if (host.Length > 253 || Uri.CheckHostName(host) != UriHostNameType.Dns)
                throw new ArgumentException($"'{raw}' is not a valid certificate DNS name.");
            var normalized = wildcard ? "*." + host : host;
            if (!result.Contains(normalized, StringComparer.OrdinalIgnoreCase)) result.Add(normalized);
        }

        return result.Order(StringComparer.Ordinal).ToArray();
    }

    public static DateTimeOffset FallbackRenewal(X509Certificate2 certificate)
    {
        var start = new DateTimeOffset(certificate.NotBefore.ToUniversalTime());
        var end = new DateTimeOffset(certificate.NotAfter.ToUniversalTime());
        var lifetime = end - start;
        return lifetime < TimeSpan.FromDays(10) ? start + lifetime / 2 : end - lifetime / 3;
    }

    public static TimeSpan RetryDelay(int failedAttempts, DateTimeOffset? expires, DateTimeOffset now)
    {
        if (expires is not null && expires <= now.AddDays(7)) return TimeSpan.FromHours(1);
        return failedAttempts switch
        {
            <= 1 => TimeSpan.FromMinutes(15),
            2 => TimeSpan.FromHours(1),
            3 => TimeSpan.FromHours(6),
            _ => TimeSpan.FromHours(24)
        };
    }

    private Task<ManagedCertificate> Required(Guid id, CancellationToken ct)
    {
        return db.ManagedCertificates.Include(x => x.AcmeAccount).Include(x => x.InboundCertificate)
            .Include(x => x.DnsProviderProfile).SingleOrDefaultAsync(x => x.Id == id, ct).ContinueWith(x =>
                x.Result ?? throw new KeyNotFoundException("Managed certificate not found."), ct);
    }
}

public sealed class AcmeCertificateWorker(
    IServiceScopeFactory scopes,
    IOptions<AcmeOptions> options,
    ILogger<AcmeCertificateWorker> logger) : BackgroundService
{
    private readonly string owner = $"{Environment.MachineName}:{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.WorkerInterval);
        do
        {
            try
            {
                if (!await TryAcquireLease(stoppingToken)) continue;
                using var execution = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                var heartbeat = RenewLease(execution);
                try
                {
                    await ProcessAvailable(execution.Token);
                }
                finally
                {
                    await execution.CancelAsync();
                    await heartbeat;
                    await ReleaseLease(CancellationToken.None);
                }
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(exception, "ACME certificate maintenance failed.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task ProcessAvailable(CancellationToken ct)
    {
        var active = new Dictionary<Guid, Task>();
        while (!ct.IsCancellationRequested)
        {
            var available = options.Value.MaxConcurrentOrders - active.Count;
            Guid[] certificateIds = [];
            if (available > 0)
            {
                await using var scope = scopes.CreateAsyncScope();
                certificateIds = await scope.ServiceProvider.GetRequiredService<AcmeOrderProcessor>()
                    .ClaimDueAsync(available, active.Keys.ToArray(), ct);
                foreach (var id in certificateIds) active.Add(id, ProcessClaimed(id, ct));
            }

            if (active.Count == 0) return;
            if (certificateIds.Length > 0 && active.Count < options.Value.MaxConcurrentOrders) continue;

            var completed = await Task.WhenAny(active.Values);
            active.Remove(active.Single(x => x.Value == completed).Key);
            await completed;
        }
    }

    private async Task ProcessClaimed(Guid managedCertificateId, CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<AcmeOrderProcessor>()
                .ProcessClaimedAsync(managedCertificateId, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Claimed ACME certificate {ManagedCertificateId} could not be processed.",
                managedCertificateId);
        }
    }

    private async Task<bool> TryAcquireLease(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AcmeWorkerLeaseService>()
            .TryAcquireAsync(owner, options.Value.LeaseDuration, ct);
    }

    private async Task RenewLease(CancellationTokenSource execution)
    {
        try
        {
            using var timer = new PeriodicTimer(options.Value.LeaseRenewalInterval);
            while (await timer.WaitForNextTickAsync(execution.Token))
            {
                await using var scope = scopes.CreateAsyncScope();
                if (await scope.ServiceProvider.GetRequiredService<AcmeWorkerLeaseService>()
                        .RenewAsync(owner, options.Value.LeaseDuration, execution.Token)) continue;
                logger.LogError("ACME worker lost its database lease. The active operation will be stopped.");
                await execution.CancelAsync();
                return;
            }
        }
        catch (OperationCanceledException) when (execution.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception,
                "ACME worker could not renew its database lease. The active operation will be stopped.");
            await execution.CancelAsync();
        }
    }

    private async Task ReleaseLease(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AcmeWorkerLeaseService>().ReleaseAsync(owner, ct);
    }
}

public sealed class AcmeWorkerLeaseService(GatewayDbContext db)
{
    private const string JobName = "acme-certificates";

    public async Task<bool> TryAcquireAsync(string owner, TimeSpan duration, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,
            ct);
        var now = DateTimeOffset.UtcNow;
        var lease = await db.MaintenanceLeases.SingleOrDefaultAsync(x => x.JobName == JobName, ct);
        if (lease is not null && lease.LeaseExpiresAtUtc > now && lease.OwnerInstance != owner)
        {
            await transaction.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            return false;
        }

        if (lease is null)
        {
            db.MaintenanceLeases.Add(new MaintenanceLease
            {
                JobName = JobName, OwnerInstance = owner, LeaseExpiresAtUtc = now + duration
            });
        }
        else
        {
            lease.OwnerInstance = owner;
            lease.LeaseExpiresAtUtc = now + duration;
            lease.ConcurrencyVersion = Guid.NewGuid();
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return true;
    }

    public async Task<bool> RenewAsync(string owner, TimeSpan duration, CancellationToken ct)
    {
        var lease = await db.MaintenanceLeases.SingleOrDefaultAsync(x => x.JobName == JobName, ct);
        if (lease is null || lease.OwnerInstance != owner) return false;
        lease.LeaseExpiresAtUtc = DateTimeOffset.UtcNow + duration;
        lease.ConcurrencyVersion = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task ReleaseAsync(string owner, CancellationToken ct)
    {
        var lease = await db.MaintenanceLeases.SingleOrDefaultAsync(x => x.JobName == JobName, ct);
        if (lease is null || lease.OwnerInstance != owner) return;
        lease.LeaseExpiresAtUtc = DateTimeOffset.UtcNow;
        lease.ConcurrencyVersion = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
    }
}

public sealed record DnsTxtLookupResult(bool Found, string Detail);

public interface IAcmeDnsTxtLookup
{
    Task<DnsTxtLookupResult> LookupAsync(string zoneName, string recordName, string expected,
        CancellationToken ct);
}

public sealed class AcmeDnsTxtLookup(IHttpClientFactory httpClients) : IAcmeDnsTxtLookup
{
    private static readonly string[] PublicResolvers =
    [
        "https://cloudflare-dns.com/dns-query", "https://dns.google/resolve"
    ];

    public async Task<DnsTxtLookupResult> LookupAsync(string zoneName, string recordName, string expected,
        CancellationToken ct)
    {
        var authoritative = await LookupAuthoritative(zoneName, recordName, expected, ct);
        if (authoritative is not null) return authoritative;

        var successfulResolvers = 0;
        foreach (var endpoint in PublicResolvers)
            try
            {
                var client = httpClients.CreateClient(nameof(AcmeCertificateWorker));
                using var request = new HttpRequestMessage(HttpMethod.Get,
                    $"{endpoint}?name={Uri.EscapeDataString(recordName)}&type=TXT");
                request.Headers.Accept.ParseAdd("application/dns-json");
                using var response = await client.SendAsync(request, ct);
                if (!response.IsSuccessStatusCode) continue;
                successfulResolvers++;
                if (AcmeOrderProcessor.DnsResponseContainsTxt(await response.Content.ReadAsStringAsync(ct), expected))
                    return new DnsTxtLookupResult(true,
                        "The TXT value is visible through a public DNS resolver; direct authoritative queries were unavailable.");
            }
            catch (Exception exception) when (exception is HttpRequestException or JsonException or TimeoutException)
            {
            }

        return successfulResolvers > 0
            ? new DnsTxtLookupResult(false,
                $"The TXT value is not visible through {successfulResolvers} public DNS resolver(s), and direct authoritative queries are unavailable.")
            : new DnsTxtLookupResult(false,
                "Authoritative and public DNS lookups are unavailable from the Management container. Check outbound DNS and HTTPS connectivity.");
    }

    private static async Task<DnsTxtLookupResult?> LookupAuthoritative(string zoneName, string recordName,
        string expected, CancellationToken ct)
    {
        try
        {
            var resolver = new LookupClient(new LookupClientOptions
            {
                UseCache = false, Timeout = TimeSpan.FromSeconds(5), Retries = 1, UseTcpFallback = true
            });
            var candidate = zoneName.TrimEnd('.');
            string[] nameServers = [];
            while (candidate.Contains('.'))
            {
                var nameServerResponse = await resolver.QueryAsync(candidate, QueryType.NS, QueryClass.IN, ct);
                nameServers = nameServerResponse.Answers.NsRecords().Select(x => x.NSDName.Value.TrimEnd('.'))
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                if (nameServers.Length > 0) break;
                candidate = candidate[(candidate.IndexOf('.') + 1)..];
            }

            if (nameServers.Length == 0) return null;

            var queried = 0;
            var propagated = 0;
            foreach (var nameServer in nameServers)
            {
                IPAddress[] addresses;
                try
                {
                    addresses = await Dns.GetHostAddressesAsync(nameServer, ct);
                }
                catch (Exception exception) when (exception is SocketException or OperationCanceledException &&
                                                  !ct.IsCancellationRequested)
                {
                    continue;
                }

                var serverResponded = false;
                var serverContainsValue = false;
                foreach (var address in addresses.OrderBy(x => x.AddressFamily == AddressFamily.InterNetwork ? 0 : 1))
                    try
                    {
                        var authoritativeResolver = new LookupClient(new LookupClientOptions(address)
                        {
                            UseCache = false, Recursion = false, Timeout = TimeSpan.FromSeconds(5), Retries = 0,
                            UseTcpFallback = true
                        });
                        var response = await authoritativeResolver.QueryAsync(recordName, QueryType.TXT,
                            QueryClass.IN, ct);
                        serverResponded = true;
                        serverContainsValue = response.Answers.TxtRecords()
                            .Any(record => string.Concat(record.Text) == expected);
                        break;
                    }
                    catch (Exception exception) when (exception is DnsResponseException or SocketException or
                                                      TimeoutException or OperationCanceledException &&
                                                      !ct.IsCancellationRequested)
                    {
                    }

                if (!serverResponded) continue;
                queried++;
                if (serverContainsValue) propagated++;
            }

            if (queried == 0) return null;
            return propagated == nameServers.Length
                ? new DnsTxtLookupResult(true,
                    $"The TXT value is visible on all {nameServers.Length} authoritative name server(s).")
                : new DnsTxtLookupResult(false,
                    $"The TXT value is visible on {propagated} of {nameServers.Length} authoritative name server(s); {queried} responded.");
        }
        catch (Exception exception) when (exception is DnsResponseException or SocketException or TimeoutException or
                                          OperationCanceledException && !ct.IsCancellationRequested)
        {
            return null;
        }
    }
}

public sealed class AcmeOrderProcessor(
    GatewayDbContext db,
    CertificateMaterialProtector protector,
    DnsProviderProfileService profiles,
    DnsChallengeProviderFactory providers,
    InboundCertificateService inbound,
    IHttpClientFactory httpClients,
    IAcmeDnsTxtLookup dnsLookup,
    IOptions<AcmeOptions> options,
    ILogger<AcmeOrderProcessor> logger)
{
    public async Task<Guid[]> ClaimDueAsync(int maximumCount, IReadOnlyCollection<Guid> activeCertificateIds,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        await RecoverInterruptedAttempts(now, activeCertificateIds, ct);
        await RefreshAriOnce(now, ct);
        var values = SelectDueCertificates(await db.ManagedCertificates.ToListAsync(ct), now, maximumCount);
        foreach (var value in values)
        {
            value.State = value.InboundCertificateId is null
                ? ManagedCertificateState.Issuing
                : ManagedCertificateState.Renewing;
            value.LastAttemptAtUtc = now;
            value.NextAttemptAtUtc = now.AddMinutes(30);
            value.ConcurrencyVersion = Guid.NewGuid();
        }

        await db.SaveChangesAsync(ct);
        return values.Select(x => x.Id).ToArray();
    }

    internal static ManagedCertificate[] SelectDueCertificates(IEnumerable<ManagedCertificate> certificates,
        DateTimeOffset now, int maximumCount)
    {
        return certificates.Where(x => x.NextAttemptAtUtc <= now &&
                                       x.State != ManagedCertificateState.Issuing &&
                                       x.State != ManagedCertificateState.Renewing)
            .OrderBy(x => x.NextAttemptAtUtc).Take(maximumCount).ToArray();
    }

    internal static DateTimeOffset DnsChallengeExpiresAt(DateTimeOffset createdAt, TimeSpan propagationTimeout)
    {
        return createdAt + propagationTimeout;
    }

    public async Task ProcessClaimedAsync(Guid managedCertificateId, CancellationToken ct)
    {
        var managed = await db.ManagedCertificates.Include(x => x.AcmeAccount)
            .Include(x => x.InboundCertificate).Include(x => x.DnsProviderProfile)
            .SingleOrDefaultAsync(x => x.Id == managedCertificateId, ct);
        if (managed is null) return;
        await Process(managed, ct);
    }

    private async Task Process(ManagedCertificate managed, CancellationToken ct)
    {
        var orderRecord = new AcmeOrder { ManagedCertificateId = managed.Id };
        db.AcmeOrders.Add(orderRecord);
        AcmeAccountService.Audit(db, "ManagedCertificateAttemptStarted", nameof(ManagedCertificate),
            managed.Id.ToString(), "system:acme", new { managed.Name, managed.ChallengeKind });
        await db.SaveChangesAsync(ct);
        var dnsPresented = new List<(IDnsChallengeProvider Provider, DnsProviderCredentials Credentials,
            DnsManagedZone Zone, AcmeChallenge Challenge)>();
        try
        {
            var renewal = managed.InboundCertificateId is not null;
            var account = managed.AcmeAccount;
            var context = AcmeAccountService.Context(account, protector);
            var names = JsonSerializer.Deserialize<string[]>(managed.DnsNamesJson) ?? [];
            var replaces = managed.InboundCertificate is null
                ? null
                : AcmeProtocol.CertificateId(
                    protector.Unprotect(managed.InboundCertificate.ProtectedPkcs12));
            var order = await AcmeProtocol.NewOrderAsync(context, names, replaces);
            orderRecord.OrderUrl = order.Location.ToString();
            orderRecord.State = "Authorizing";
            AcmeAccountService.Audit(db, "ManagedCertificateOrderCreated", nameof(ManagedCertificate),
                managed.Id.ToString(), "system:acme");
            await db.SaveChangesAsync(ct);
            foreach (var authorization in await order.Authorizations())
            {
                var resource = await authorization.Resource();
                if (resource.Status == AuthorizationStatus.Valid) continue;
                IChallengeContext challenge;
                AcmeChallenge row;
                if (managed.ChallengeKind == AcmeChallengeKind.Http01)
                {
                    challenge = await authorization.Http();
                    row = new AcmeChallenge
                    {
                        AcmeOrderId = orderRecord.Id, Kind = AcmeChallengeKind.Http01,
                        Host = resource.Identifier.Value, Token = challenge.Token, KeyAuthorization = challenge.KeyAuthz
                    };
                    db.AcmeChallenges.Add(row);
                    await db.SaveChangesAsync(ct);
                    await Task.Delay(options.Value.HttpChallengePropagationDelay, ct);
                }
                else
                {
                    challenge = await authorization.Dns();
                    var host = resource.Identifier.Value.TrimStart('*', '.');
                    var recordName = "_acme-challenge." + host;
                    var dnsValue = context.AccountKey.DnsTxt(challenge.Token);
                    row = new AcmeChallenge
                    {
                        AcmeOrderId = orderRecord.Id, Kind = managed.ChallengeKind, Host = host,
                        Token = challenge.Token, DnsRecordName = recordName, DnsRecordValue = dnsValue
                    };
                    db.AcmeChallenges.Add(row);
                    string zoneName;
                    if (managed.ChallengeKind == AcmeChallengeKind.Dns01)
                    {
                        var profile = managed.DnsProviderProfile ??
                                      throw new InvalidOperationException("DNS profile is missing.");
                        var provider = providers.Get(profile.Provider);
                        var credentials = profiles.Credentials(profile);
                        var zone = DnsChallengeProviderFactory.SelectZone(DnsProviderProfileService.Zones(profile),
                            recordName);
                        row.ProviderRecordId = await provider.PresentAsync(credentials, zone, recordName, dnsValue, ct);
                        zoneName = zone.Name;
                        logger.LogInformation(
                            "Published ACME DNS TXT record {RecordName} with value {RecordValue} using {DnsProvider} for certificate {ManagedCertificateId}.",
                            recordName, dnsValue, profile.Provider, managed.Id);
                        AcmeAccountService.Audit(db, "ManagedCertificateDnsRecordPresented",
                            nameof(ManagedCertificate), managed.Id.ToString(), "system:acme",
                            new { host, recordName, provider = profile.Provider });
                        dnsPresented.Add((provider, credentials, zone, row));
                    }
                    else
                    {
                        zoneName = recordName;
                        AcmeAccountService.Audit(db, "ManagedCertificateManualDnsRecordRequired",
                            nameof(ManagedCertificate), managed.Id.ToString(), "system:acme",
                            new { host, recordName });
                    }

                    row.ExpiresAtUtc = DnsChallengeExpiresAt(DateTimeOffset.UtcNow,
                        options.Value.DnsPropagationTimeout);
                    await db.SaveChangesAsync(ct);
                    await WaitForDns(managed.Id, zoneName, recordName, dnsValue, ct);
                    AcmeAccountService.Audit(db, "ManagedCertificateDnsPropagationObserved",
                        nameof(ManagedCertificate), managed.Id.ToString(), "system:acme", new { host, recordName });
                    await db.SaveChangesAsync(ct);
                }

                AcmeAccountService.Audit(db, "ManagedCertificateValidationRequested", nameof(ManagedCertificate),
                    managed.Id.ToString(), "system:acme", new { host = resource.Identifier.Value });
                await db.SaveChangesAsync(ct);
                await challenge.Validate();
                await WaitForChallenge(challenge, ct);
                AcmeAccountService.Audit(db, "ManagedCertificateValidationCompleted", nameof(ManagedCertificate),
                    managed.Id.ToString(), "system:acme", new { host = resource.Identifier.Value });
                await db.SaveChangesAsync(ct);
            }

            orderRecord.State = "Finalizing";
            AcmeAccountService.Audit(db, "ManagedCertificateFinalizationStarted", nameof(ManagedCertificate),
                managed.Id.ToString(), "system:acme");
            var key = KeyFactory.NewKey(KeyAlgorithm.ES256);
            orderRecord.ProtectedCertificateKey =
                protector.ProtectSecret("certificate-key", Encoding.UTF8.GetBytes(key.ToPem()));
            await db.SaveChangesAsync(ct);
            var certificate = await FinalizeOrderAsync(order, new CsrInfo { CommonName = names[0] }, key,
                options.Value.OrderFinalizationTimeout, options.Value.OrderFinalizationPollInterval, ct);
            var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            var pfx = BuildPkcs12(certificate, key, password);
            InboundCertificate stored;
            if (managed.InboundCertificateId is null)
            {
                stored = await inbound.UploadAsync(managed.Name, pfx, password, "system:acme", ct);
            }
            else
            {
                var current = await db.InboundCertificates.AsNoTracking()
                    .SingleAsync(x => x.Id == managed.InboundCertificateId, ct);
                stored = await inbound.ReplaceAsync(current.Id, current.ConcurrencyVersion, pfx, password,
                    "system:acme", ct);
            }

            managed.InboundCertificateId = stored.Id;
            managed.State = ManagedCertificateState.Active;
            managed.FailedAttemptCount = 0;
            managed.LastErrorCode = null;
            managed.LastErrorMessage = null;
            managed.LastSuccessAtUtc = DateTimeOffset.UtcNow;
            using var issued = X509CertificateLoader.LoadPkcs12(pfx, password, X509KeyStorageFlags.EphemeralKeySet);
            managed.NextAttemptAtUtc = ManagedCertificateService.FallbackRenewal(issued);
            managed.ConcurrencyVersion = Guid.NewGuid();
            managed.UpdatedAtUtc = DateTimeOffset.UtcNow;
            orderRecord.State = "Completed";
            AcmeAccountService.Audit(db, renewal ? "ManagedCertificateRenewed" : "ManagedCertificateIssued",
                nameof(ManagedCertificate),
                managed.Id.ToString(), "system:acme", new { managed.Name, dnsNames = names, stored.Thumbprint });
            await db.SaveChangesAsync(ct);
            logger.LogInformation("ACME certificate {ManagedCertificateId} is active until {NotAfterUtc}.", managed.Id,
                stored.NotAfterUtc);
        }
        catch (ManagedCertificateDeletedException)
        {
            db.ChangeTracker.Clear();
            logger.LogInformation("Stopped ACME certificate {ManagedCertificateId} because it was deleted.",
                managed.Id);
        }
        catch (Exception exception) when (!ct.IsCancellationRequested)
        {
            managed.State = ManagedCertificateState.Failed;
            managed.FailedAttemptCount++;
            managed.LastErrorCode = ErrorCode(exception);
            managed.LastErrorMessage = SafeMessage(exception);
            var failureTime = DateTimeOffset.UtcNow;
            managed.NextAttemptAtUtc = RetryAt(exception, managed.FailedAttemptCount,
                managed.InboundCertificate?.NotAfterUtc, failureTime);
            managed.ConcurrencyVersion = Guid.NewGuid();
            managed.UpdatedAtUtc = DateTimeOffset.UtcNow;
            orderRecord.State = "Failed";
            AcmeAccountService.Audit(db, "ManagedCertificateIssuanceFailed", nameof(ManagedCertificate),
                managed.Id.ToString(), "system:acme", new { managed.Name, errorCode = managed.LastErrorCode });
            await db.SaveChangesAsync(ct);
            logger.LogWarning(exception, "ACME certificate {ManagedCertificateId} could not be issued or renewed.",
                managed.Id);
        }
        finally
        {
            foreach (var item in dnsPresented)
                try
                {
                    await item.Provider.CleanupAsync(item.Credentials, item.Zone, item.Challenge.DnsRecordName!,
                        item.Challenge.DnsRecordValue!, item.Challenge.ProviderRecordId, ct);
                }
                catch (Exception exception) when (!ct.IsCancellationRequested)
                {
                    logger.LogWarning(exception, "ACME DNS challenge cleanup failed for {RecordName}.",
                        item.Challenge.DnsRecordName);
                }

            await db.AcmeChallenges.Where(x => x.AcmeOrderId == orderRecord.Id).ExecuteDeleteAsync(ct);
        }
    }

    private async Task RecoverInterruptedAttempts(DateTimeOffset now, IReadOnlyCollection<Guid> activeCertificateIds,
        CancellationToken ct)
    {
        var interrupted = ExcludeActiveAttempts(
            await FindInterruptedAttempts(db, now - options.Value.InProgressTimeout, ct), activeCertificateIds);
        foreach (var managed in interrupted)
        {
            var orders = await db.AcmeOrders.Where(x => x.ManagedCertificateId == managed.Id &&
                                                        x.State != "Completed" && x.State != "Failed")
                .ToListAsync(ct);
            var orderIds = orders.Select(x => x.Id).ToArray();
            var challenges = await db.AcmeChallenges.Where(x => orderIds.Contains(x.AcmeOrderId)).ToListAsync(ct);
            foreach (var challenge in challenges.Where(x => x.Kind == AcmeChallengeKind.Dns01 &&
                                                            x.DnsRecordName != null && x.DnsRecordValue != null))
                try
                {
                    var profile = managed.DnsProviderProfile ??
                                  throw new InvalidOperationException("DNS profile is missing.");
                    var provider = providers.Get(profile.Provider);
                    var credentials = profiles.Credentials(profile);
                    var zone = DnsChallengeProviderFactory.SelectZone(DnsProviderProfileService.Zones(profile),
                        challenge.DnsRecordName!);
                    await provider.CleanupAsync(credentials, zone, challenge.DnsRecordName!, challenge.DnsRecordValue!,
                        challenge.ProviderRecordId, ct);
                }
                catch (Exception exception) when (!ct.IsCancellationRequested)
                {
                    logger.LogWarning(exception, "Interrupted ACME DNS challenge cleanup failed for {RecordName}.",
                        challenge.DnsRecordName);
                }

            db.AcmeChallenges.RemoveRange(challenges);
            foreach (var order in orders) order.State = "Interrupted";
            managed.State = ManagedCertificateState.Failed;
            managed.FailedAttemptCount++;
            managed.LastErrorCode = "ACME_ATTEMPT_INTERRUPTED";
            managed.LastErrorMessage =
                "The previous certificate attempt was interrupted and will be retried safely.";
            managed.NextAttemptAtUtc = now + ManagedCertificateService.RetryDelay(managed.FailedAttemptCount,
                managed.InboundCertificate?.NotAfterUtc, now);
            managed.ConcurrencyVersion = Guid.NewGuid();
            managed.UpdatedAtUtc = now;
            AcmeAccountService.Audit(db, "ManagedCertificateAttemptRecovered", nameof(ManagedCertificate),
                managed.Id.ToString(), "system:acme", new { managed.Name });
            logger.LogWarning("Recovered interrupted ACME certificate attempt {ManagedCertificateId}.", managed.Id);
        }

        if (interrupted.Count > 0) await db.SaveChangesAsync(ct);
    }

    internal static async Task<List<ManagedCertificate>> FindInterruptedAttempts(GatewayDbContext db,
        DateTimeOffset cutoff, CancellationToken ct)
    {
        var candidates = await db.ManagedCertificates.Include(x => x.InboundCertificate)
            .Include(x => x.DnsProviderProfile)
            .Where(x => x.State == ManagedCertificateState.Issuing || x.State == ManagedCertificateState.Renewing)
            .ToListAsync(ct);
        return candidates.Where(x => x.LastAttemptAtUtc <= cutoff).ToList();
    }

    internal static List<ManagedCertificate> ExcludeActiveAttempts(IEnumerable<ManagedCertificate> candidates,
        IReadOnlyCollection<Guid> activeCertificateIds)
    {
        return candidates.Where(x => !activeCertificateIds.Contains(x.Id)).ToList();
    }

    private async Task RefreshAriOnce(DateTimeOffset now, CancellationToken ct)
    {
        var managed = (await db.ManagedCertificates.Include(x => x.AcmeAccount).Include(x => x.InboundCertificate)
                .ToListAsync(ct))
            .Where(x => x.State == ManagedCertificateState.Active && x.InboundCertificateId != null &&
                        (x.LastAriCheckAtUtc == null ||
                         x.LastAriCheckAtUtc <= now - options.Value.RenewalInfoInterval))
            .OrderBy(x => x.LastAriCheckAtUtc).FirstOrDefault();
        if (managed?.InboundCertificate is null) return;
        managed.LastAriCheckAtUtc = now;
        try
        {
            using var certificate = protector.Unprotect(managed.InboundCertificate.ProtectedPkcs12);
            var client = httpClients.CreateClient(nameof(AcmeCertificateWorker));
            var window = await AcmeProtocol.RenewalWindowAsync(client, managed.AcmeAccount.DirectoryUrl, certificate,
                ct);
            if (window is not null)
            {
                managed.AriWindowStartUtc = window.Value.Start;
                managed.AriWindowEndUtc = window.Value.End;
                managed.NextAttemptAtUtc = AcmeProtocol.SelectRenewalTime(window.Value.Start, window.Value.End, now);
            }
        }
        catch (Exception exception) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(exception, "ACME renewal information could not be refreshed for {ManagedCertificateId}.",
                managed.Id);
        }

        managed.ConcurrencyVersion = Guid.NewGuid();
        managed.UpdatedAtUtc = now;
        await db.SaveChangesAsync(ct);
    }

    private async Task WaitForDns(Guid managedCertificateId, string zoneName, string recordName, string expected,
        CancellationToken ct)
    {
        var until = DateTimeOffset.UtcNow + options.Value.DnsPropagationTimeout;
        string? lastDetail = null;
        var nextLogAt = DateTimeOffset.MinValue;
        do
        {
            await EnsureManagedCertificateExists(db, managedCertificateId, ct);
            var result = await dnsLookup.LookupAsync(zoneName, recordName, expected, ct);
            if (result.Found) return;
            lastDetail = result.Detail;
            if (DateTimeOffset.UtcNow >= nextLogAt)
            {
                logger.LogInformation(
                    "Waiting for ACME DNS TXT propagation for {RecordName} in authoritative zone {ZoneName}. {Detail}",
                    recordName, zoneName, result.Detail);
                nextLogAt = DateTimeOffset.UtcNow.AddMinutes(5);
            }

            await Task.Delay(options.Value.DnsPropagationPollInterval, ct);
        } while (DateTimeOffset.UtcNow < until);

        throw new TimeoutException($"DNS challenge propagation timed out. Last check: {lastDetail ?? "no result"}");
    }

    internal static async Task EnsureManagedCertificateExists(GatewayDbContext context, Guid managedCertificateId,
        CancellationToken ct)
    {
        if (!await context.ManagedCertificates.AsNoTracking().AnyAsync(x => x.Id == managedCertificateId, ct))
            throw new ManagedCertificateDeletedException();
    }

    public static bool DnsResponseContainsTxt(string json, string expected)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("Answer", out var answers) ||
            answers.ValueKind != JsonValueKind.Array) return false;
        return answers.EnumerateArray().Any(x => x.TryGetProperty("type", out var type) && type.GetInt32() == 16 &&
                                                 x.TryGetProperty("data", out var data) &&
                                                 data.GetString()?.Trim('"') == expected);
    }

    private static async Task WaitForChallenge(IChallengeContext challenge, CancellationToken ct)
    {
        var until = DateTimeOffset.UtcNow.AddMinutes(3);
        var fallbackDelay = TimeSpan.FromSeconds(2);
        do
        {
            ct.ThrowIfCancellationRequested();
            var value = await challenge.Resource();
            if (value.Status == ChallengeStatus.Valid) return;
            if (value.Status == ChallengeStatus.Invalid)
                throw new InvalidOperationException("The ACME challenge was rejected.");
            var requestedDelay = challenge.RetryAfter > 0
                ? TimeSpan.FromSeconds(challenge.RetryAfter)
                : fallbackDelay;
            var remaining = until - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero) break;
            await Task.Delay(requestedDelay < remaining ? requestedDelay : remaining, ct);
            fallbackDelay = TimeSpan.FromSeconds(Math.Min(30, fallbackDelay.TotalSeconds * 2));
        } while (DateTimeOffset.UtcNow < until);

        throw new TimeoutException("The ACME challenge did not complete in time.");
    }

    internal static async Task<CertificateChain> FinalizeOrderAsync(IOrderContext order, CsrInfo csr, IKey key,
        TimeSpan timeout, TimeSpan fallbackPollInterval, CancellationToken ct)
    {
        var resource = await order.Finalize(csr, key);
        var until = DateTimeOffset.UtcNow + timeout;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (resource?.Status == OrderStatus.Valid) return await order.Download();
            if (resource?.Status == OrderStatus.Invalid)
                throw AcmeFinalizationException.From(resource.Error);
            if (resource is not null && resource.Status != OrderStatus.Processing)
                throw new AcmeFinalizationException(
                    $"Certificate finalization returned unexpected status '{resource.Status}'.");

            var remaining = until - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
                throw new TimeoutException(
                    "The certificate authority did not finish certificate finalization in time.");
            var requestedDelay = order.RetryAfter > 0
                ? TimeSpan.FromSeconds(order.RetryAfter)
                : fallbackPollInterval;
            await Task.Delay(requestedDelay < remaining ? requestedDelay : remaining, ct);
            resource = await order.Resource();
        }
    }

    internal static byte[] BuildPkcs12(CertificateChain chain, IKey key, string password)
    {
        using var certificateKey = ECDsa.Create();
        certificateKey.ImportFromPem(key.ToPem());
        using var leaf = X509CertificateLoader.LoadCertificate(chain.Certificate.ToDer());
        using var leafWithKey = leaf.CopyWithPrivateKey(certificateKey);
        var certificates = new X509Certificate2Collection { leafWithKey };
        foreach (var issuer in chain.Issuers)
            certificates.Add(X509CertificateLoader.LoadCertificate(issuer.ToDer()));

        try
        {
            return certificates.ExportPkcs12(Pkcs12ExportPbeParameters.Pbes2Aes256Sha256, password);
        }
        finally
        {
            foreach (var issuer in certificates.Cast<X509Certificate2>().Skip(1)) issuer.Dispose();
        }
    }

    private static string ErrorCode(Exception exception)
    {
        return exception switch
        {
            AcmeRequestException request when IsRateLimited(request) => "ACME_RATE_LIMITED",
            AcmeFinalizationException => "ACME_FINALIZATION_FAILED",
            TimeoutException => "ACME_TIMEOUT",
            ArgumentException => "ACME_CONFIGURATION_INVALID",
            HttpRequestException => "ACME_NETWORK_ERROR",
            _ => "ACME_ISSUANCE_FAILED"
        };
    }

    public static DateTimeOffset RetryAt(Exception exception, int failedAttempts, DateTimeOffset? expires,
        DateTimeOffset now)
    {
        if (exception is AcmeRequestException request && IsRateLimited(request))
            return ParseRetryAfter(request.Error?.Detail, now) ?? now.AddHours(24);
        var delay = ManagedCertificateService.RetryDelay(failedAttempts, expires, now);
        var jitter = TimeSpan.FromSeconds(Random.Shared.NextDouble() * Math.Min(900, delay.TotalSeconds * 0.1));
        return now + delay + jitter;
    }

    private static bool IsRateLimited(AcmeRequestException exception)
    {
        return exception.Error?.Type?.EndsWith(":rateLimited", StringComparison.OrdinalIgnoreCase) == true ||
               exception.Error?.Detail?.Contains("rate limit", StringComparison.OrdinalIgnoreCase) == true;
    }

    public static DateTimeOffset? ParseRetryAfter(string? detail, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(detail)) return null;
        var match = Regex.Match(detail,
            @"retry after\s+(?<date>\d{4}-\d{2}-\d{2}[ T]\d{2}:\d{2}:\d{2}(?:\s*UTC|Z|[+-]\d{2}:\d{2})?)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success) return null;
        var text = match.Groups["date"].Value;
        if (text.EndsWith("UTC", StringComparison.OrdinalIgnoreCase)) text = text[..^3].TrimEnd() + "+00:00";
        if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)) return null;
        return parsed > now ? parsed.AddMinutes(1) : now.AddMinutes(15);
    }

    private static string SafeMessage(Exception exception)
    {
        return exception switch
        {
            TimeoutException or ArgumentException or InvalidOperationException => exception.Message[
                ..Math.Min(1000, exception.Message.Length)],
            _ => "Certificate issuance failed. Review the management logs for details."
        };
    }
}

internal sealed class ManagedCertificateDeletedException : OperationCanceledException;

internal sealed class AcmeFinalizationException(string message) : InvalidOperationException(message)
{
    public static AcmeFinalizationException From(object? error)
    {
        var value = error as AcmeError;
        if (value is null && error is not null)
            try
            {
                value = JsonSerializer.Deserialize<AcmeError>(error.ToString() ?? string.Empty,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (JsonException)
            {
            }

        if (value is null)
            return new AcmeFinalizationException("The certificate authority rejected certificate finalization.");
        var detail = string.IsNullOrWhiteSpace(value.Detail)
            ? "No additional details were provided."
            : Regex.Replace(value.Detail, @"\s+", " ").Trim();
        if (detail.Length > 1000) detail = detail[..1000];
        return new AcmeFinalizationException($"The certificate authority rejected certificate finalization: {detail}");
    }
}

public static class AcmeProtocol
{
    public static async Task<IOrderContext> NewOrderAsync(AcmeContext context, IReadOnlyList<string> names,
        string? replaces)
    {
        if (replaces is null) return await context.NewOrder(names.ToList());
        var endpoint = (await context.GetDirectory()).NewOrder;
        var payload = new
        {
            identifiers = names.Select(x => new { type = "dns", value = x }).ToArray(),
            replaces
        };
        var signed = await context.Sign(payload, endpoint);
        var response = await context.HttpClient.Post<Order>(endpoint, signed);
        return context.Order(response.Location);
    }

    public static async Task<(DateTimeOffset Start, DateTimeOffset End)?> RenewalWindowAsync(HttpClient client,
        string directoryUrl, X509Certificate2 certificate, CancellationToken ct)
    {
        using var directoryResponse = await client.GetAsync(directoryUrl, ct);
        directoryResponse.EnsureSuccessStatusCode();
        using var directory = JsonDocument.Parse(await directoryResponse.Content.ReadAsStringAsync(ct));
        if (!directory.RootElement.TryGetProperty("renewalInfo", out var renewalInfo)) return null;
        var endpoint = renewalInfo.GetString()!.TrimEnd('/') + "/" + CertificateId(certificate);
        using var response = await client.GetAsync(endpoint, ct);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var window = json.RootElement.GetProperty("suggestedWindow");
        return (window.GetProperty("start").GetDateTimeOffset(), window.GetProperty("end").GetDateTimeOffset());
    }

    public static string CertificateId(X509Certificate2 certificate)
    {
        var source = certificate.Extensions["2.5.29.35"] ??
                     throw new InvalidOperationException("The certificate has no authority key identifier.");
        var extension = new X509AuthorityKeyIdentifierExtension(source.RawData, source.Critical);
        var keyIdentifier = extension.KeyIdentifier ??
                            throw new InvalidOperationException("The certificate authority key identifier is missing.");
        var serial = Convert.FromHexString(certificate.SerialNumber);
        return $"{WebEncoders.Base64UrlEncode(keyIdentifier.ToArray())}.{WebEncoders.Base64UrlEncode(serial)}";
    }

    public static DateTimeOffset SelectRenewalTime(DateTimeOffset start, DateTimeOffset end, DateTimeOffset now)
    {
        if (end <= start) throw new ArgumentException("The ARI renewal window is invalid.");
        var fraction = RandomNumberGenerator.GetInt32(int.MaxValue) / (double)int.MaxValue;
        var selected = start + TimeSpan.FromTicks((long)((end - start).Ticks * fraction));
        return selected < now ? now : selected;
    }
}
