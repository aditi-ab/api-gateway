using ApiGateway.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ApiGateway.Management;

public sealed class ApiKeyUsageWriter(
    ApiKeyUsageQueue queue,
    IServiceScopeFactory scopes,
    ILogger<ApiKeyUsageWriter> logger) : BackgroundService
{
    private readonly Dictionary<(Guid, bool), DateTimeOffset> written = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var usage in queue.ReadAllAsync(stoppingToken))
        {
            var identity = (usage.KeyId, usage.IsManagementKey);
            if (written.TryGetValue(identity, out var previous) &&
                usage.UsedAtUtc - previous < TimeSpan.FromMinutes(1)) continue;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();
                await db.ManagementApiKeys.Where(x => x.Id == usage.KeyId)
                    .ExecuteUpdateAsync(x => x.SetProperty(k => k.LastUsedAtUtc, usage.UsedAtUtc), stoppingToken);
                written[identity] = usage.UsedAtUtc;
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Unable to update management API key usage time.");
            }
        }
    }
}