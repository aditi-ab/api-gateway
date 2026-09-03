using System.Data;
using System.Text.Json;
using ApiGateway.Domain;
using Microsoft.EntityFrameworkCore;

namespace ApiGateway.Persistence;

public sealed record RetentionResult(bool LeaseAcquired, int ActivationEventsDeleted, int AuditEventsDeleted);

public sealed class RetentionMaintenanceService(GatewayDbContext db)
{
    public async Task<RetentionResult> RunAsync(string owner, DateTimeOffset activationBeforeUtc,
        DateTimeOffset auditBeforeUtc, string actor, string correlationId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var now = DateTimeOffset.UtcNow;
        var lease = await db.MaintenanceLeases.SingleOrDefaultAsync(x => x.JobName == "retention", ct);
        if (lease is not null && lease.LeaseExpiresAtUtc > now && lease.OwnerInstance != owner)
        {
            await transaction.RollbackAsync(ct);
            return new RetentionResult(false, 0, 0);
        }

        if (lease is null)
        {
            lease = new MaintenanceLease
                { JobName = "retention", OwnerInstance = owner, LeaseExpiresAtUtc = now.AddMinutes(10) };
            db.MaintenanceLeases.Add(lease);
            await db.SaveChangesAsync(ct);
        }
        else
        {
            lease.OwnerInstance = owner;
            lease.LeaseExpiresAtUtc = now.AddMinutes(10);
            lease.ConcurrencyVersion = Guid.NewGuid();
            await db.SaveChangesAsync(ct);
        }

        int activations, audits;
        if (db.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true)
        {
            var oldActivations = (await db.ActivationEvents.ToListAsync(ct))
                .Where(x => x.CompletedAtUtc < activationBeforeUtc).ToList();
            var oldAudits = (await db.AuditEvents.ToListAsync(ct)).Where(x => x.OccurredAtUtc < auditBeforeUtc)
                .ToList();
            activations = oldActivations.Count;
            audits = oldAudits.Count;
            db.ActivationEvents.RemoveRange(oldActivations);
            db.AuditEvents.RemoveRange(oldAudits);
        }
        else
        {
            activations = await db.ActivationEvents.Where(x => x.CompletedAtUtc < activationBeforeUtc)
                .ExecuteDeleteAsync(ct);
            audits = await db.AuditEvents.Where(x => x.OccurredAtUtc < auditBeforeUtc).ExecuteDeleteAsync(ct);
        }

        db.AuditEvents.Add(new AuditEvent
        {
            ActorType = actor.StartsWith("system:", StringComparison.Ordinal) ? "System" : "User", ActorId = actor,
            Action = "RetentionMaintenanceCompleted", TargetType = nameof(MaintenanceLease), TargetId = lease.JobName,
            CorrelationId = correlationId,
            DetailsJson = JsonSerializer.Serialize(new
            {
                activationBeforeUtc, auditBeforeUtc, activationEventsDeleted = activations, auditEventsDeleted = audits
            })
        });
        lease.LeaseExpiresAtUtc = now;
        lease.ConcurrencyVersion = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new RetentionResult(true, activations, audits);
    }
}