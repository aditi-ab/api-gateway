using System.Text.Json;
using ApiGateway.Application;
using ApiGateway.Domain;
using ApiGateway.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ApiGateway.Management;

public sealed class InboundCertificatePublicationValidator(GatewayDbContext db) : IConfigurationPublicationValidator
{
    public async Task<IReadOnlyList<ValidationIssue>> ValidateAsync(GatewayConfigDocument document,
        CancellationToken ct)
    {
        var issues = new List<ValidationIssue>();
        var ids = document.Routes.Where(x => x.Inbound.CertificateId is not null)
            .Select(x => x.Inbound.CertificateId!.Value).Distinct().ToArray();
        var records = await db.InboundCertificates.AsNoTracking().Where(x => ids.Contains(x.Id)).ToListAsync(ct);
        foreach (var route in document.Routes.Where(x => x.Inbound.CertificateId is not null))
        {
            var certificate = records.SingleOrDefault(x => x.Id == route.Inbound.CertificateId);
            if (certificate is null)
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Error, "INBOUND_CERTIFICATE_NOT_FOUND",
                    $"$.routes[{route.Id}].inbound.certificateId", "The selected inbound certificate does not exist.",
                    route.Id));
                continue;
            }

            var names = JsonSerializer.Deserialize<string[]>(certificate.DnsNamesJson) ?? [];
            foreach (var host in route.Match.Hosts.Where(host =>
                         !names.Any(name => InboundCertificateService.Covers(name, host))))
                issues.Add(new ValidationIssue(ValidationSeverity.Error, "INBOUND_CERTIFICATE_HOST_MISMATCH",
                    $"$.routes[{route.Id}].match.hosts", $"The selected certificate does not cover '{host}'.",
                    route.Id));
        }

        foreach (var grouping in document.Routes.Where(x => x.Enabled && x.Inbound.CertificateId is not null)
                     .SelectMany(route => route.Match.Hosts.Select(host =>
                         (Host: DnsHostPattern.Normalize(host), route.Inbound.CertificateId, route.Id)))
                     .GroupBy(x => x.Host).Where(x => x.Select(y => y.CertificateId).Distinct().Count() > 1))
            issues.Add(new ValidationIssue(ValidationSeverity.Error, "INBOUND_CERTIFICATE_SNI_CONFLICT",
                "$.routes", $"Active routes assign different certificates to SNI host '{grouping.Key}'."));
        return issues;
    }
}